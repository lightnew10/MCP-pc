using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace McpPc.Agent;

internal static class WindowsDesktopService
{
    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    private const int SwRestore = 9;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventWheel = 0x0800;

    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    public static void EnablePerMonitorDpiAwareness()
    {
        try
        {
            _ = SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
        }
        catch
        {
            // Best effort. Coordinates may be DPI-virtualized on mixed-scale displays.
        }
    }

    public static IReadOnlyList<WindowInfo> ListWindows()
    {
        var windows = new List<WindowInfo>();

        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;

            var title = GetTitle(handle);
            if (string.IsNullOrWhiteSpace(title)) return true;

            if (!TryGetBounds(handle, out var bounds) || bounds.Width <= 0 || bounds.Height <= 0)
            {
                return true;
            }

            windows.Add(BuildWindowInfo(handle, title, bounds));
            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(window => window.ProcessName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static WindowInfo? GetActiveWindow()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero || !IsWindow(handle)) return null;
        if (!TryGetBounds(handle, out var bounds)) return null;

        return BuildWindowInfo(handle, GetTitle(handle), bounds);
    }

    public static CursorPosition GetCursorPosition()
    {
        if (!GetCursorPos(out var point))
        {
            throw new InvalidOperationException("Windows could not read the cursor position.");
        }

        return new CursorPosition(point.X, point.Y);
    }

    public static ScreenshotResult CaptureScreen()
    {
        var rectangle = SystemInformation.VirtualScreen;
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            throw new InvalidOperationException("Windows reported an invalid virtual desktop size.");
        }

        return CaptureRectangle(rectangle, "virtual-desktop");
    }

    public static ScreenshotResult CaptureWindow(string windowId)
    {
        var handle = ParseWindowId(windowId);
        ValidateWindow(handle, windowId);

        if (IsIconic(handle))
        {
            throw new InvalidOperationException("The selected window is minimized. Restore it before capturing it.");
        }

        var bounds = GetRequiredBounds(handle);
        return CaptureRectangle(
            new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            "visible-window-region");
    }

    public static WindowInfo FocusWindow(string windowId)
    {
        var (handle, info) = GetActionTarget(windowId);

        if (IsIconic(handle))
        {
            _ = ShowWindowAsync(handle, SwRestore);
        }

        if (GetForegroundWindow() != handle && !SetForegroundWindow(handle))
        {
            throw new InvalidOperationException(
                "Windows refused to focus the selected window. Click it once manually and retry.");
        }

        Thread.Sleep(60);
        return GetWindowInfo(handle) ?? info;
    }

    public static CursorPosition MoveMouse(string windowId, int x, int y)
    {
        var (handle, _) = GetActionTarget(windowId);
        var point = ResolveWindowPoint(handle, x, y);

        if (!SetCursorPos(point.X, point.Y))
        {
            throw new InvalidOperationException("Windows could not move the cursor.");
        }

        return new CursorPosition(point.X, point.Y);
    }

    public static InputActionResult Click(string windowId, int x, int y, string button)
    {
        var (handle, _) = GetActionTarget(windowId);
        var point = ResolveWindowPoint(handle, x, y);
        MoveCursorTo(point);

        var (down, up) = ResolveMouseButton(button);
        mouse_event(down, 0, 0, 0, UIntPtr.Zero);
        mouse_event(up, 0, 0, 0, UIntPtr.Zero);

        return new InputActionResult(windowId, point.X, point.Y, button, 1);
    }

    public static InputActionResult DoubleClick(string windowId, int x, int y, string button)
    {
        var (handle, _) = GetActionTarget(windowId);
        var point = ResolveWindowPoint(handle, x, y);
        MoveCursorTo(point);

        var (down, up) = ResolveMouseButton(button);
        for (var index = 0; index < 2; index++)
        {
            mouse_event(down, 0, 0, 0, UIntPtr.Zero);
            mouse_event(up, 0, 0, 0, UIntPtr.Zero);
            if (index == 0) Thread.Sleep(80);
        }

        return new InputActionResult(windowId, point.X, point.Y, button, 2);
    }

    public static ScrollActionResult Scroll(string windowId, int x, int y, int delta)
    {
        if (delta is < -1200 or > 1200 || delta == 0)
        {
            throw new InvalidOperationException("Scroll delta must be between -1200 and 1200 and cannot be zero.");
        }

        var (handle, _) = GetActionTarget(windowId);
        var point = ResolveWindowPoint(handle, x, y);
        MoveCursorTo(point);

        mouse_event(MouseEventWheel, 0, 0, unchecked((uint)delta), UIntPtr.Zero);
        return new ScrollActionResult(windowId, point.X, point.Y, delta);
    }

    public static TextActionResult TypeText(string windowId, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException("Text cannot be empty.");
        }

        var maxLength = AgentPolicyService.GetMaxTextLength();
        if (text.Length > maxLength)
        {
            throw new InvalidOperationException(
                $"Text length {text.Length} exceeds policy limit {maxLength}.");
        }

        var (handle, _) = GetActionTarget(windowId);
        EnsureForeground(handle);

        foreach (var character in text)
        {
            SendUnicodeCharacter(character);
        }

        return new TextActionResult(windowId, text.Length);
    }

    public static KeyActionResult PressKey(string windowId, string key)
    {
        var (handle, _) = GetActionTarget(windowId);
        EnsureForeground(handle);

        var virtualKey = ResolveVirtualKey(key);
        SendVirtualKey(virtualKey, false);
        SendVirtualKey(virtualKey, true);

        return new KeyActionResult(windowId, key, []);
    }

    public static KeyActionResult KeyCombination(
        string windowId,
        string key,
        IReadOnlyList<string> modifiers)
    {
        if (modifiers.Count > 4)
        {
            throw new InvalidOperationException("At most four modifiers are allowed.");
        }

        var (handle, _) = GetActionTarget(windowId);
        EnsureForeground(handle);

        var modifierKeys = modifiers.Select(ResolveModifier).Distinct().ToArray();
        var virtualKey = ResolveVirtualKey(key);

        foreach (var modifier in modifierKeys)
        {
            SendVirtualKey(modifier, false);
        }

        try
        {
            SendVirtualKey(virtualKey, false);
            SendVirtualKey(virtualKey, true);
        }
        finally
        {
            foreach (var modifier in modifierKeys.Reverse())
            {
                SendVirtualKey(modifier, true);
            }
        }

        return new KeyActionResult(windowId, key, modifiers);
    }

    private static (IntPtr Handle, WindowInfo Info) GetActionTarget(string windowId)
    {
        var handle = ParseWindowId(windowId);
        ValidateWindow(handle, windowId);

        var info = GetWindowInfo(handle)
            ?? throw new InvalidOperationException("Could not inspect the selected window.");

        AgentPolicyService.AssertActionAllowed(info.ProcessName);
        return (handle, info);
    }

    private static WindowInfo? GetWindowInfo(IntPtr handle)
    {
        if (!IsWindow(handle) || !TryGetBounds(handle, out var bounds)) return null;
        return BuildWindowInfo(handle, GetTitle(handle), bounds);
    }

    private static void ValidateWindow(IntPtr handle, string windowId)
    {
        if (!IsWindow(handle))
        {
            throw new InvalidOperationException($"Window does not exist: {windowId}");
        }
    }

    private static WindowBounds GetRequiredBounds(IntPtr handle)
    {
        if (!TryGetBounds(handle, out var bounds) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("The selected window has invalid bounds.");
        }

        return bounds;
    }

    private static NativePoint ResolveWindowPoint(IntPtr handle, int x, int y)
    {
        var bounds = GetRequiredBounds(handle);

        if (x < 0 || y < 0 || x >= bounds.Width || y >= bounds.Height)
        {
            throw new InvalidOperationException(
                $"Coordinates ({x}, {y}) are outside the selected window ({bounds.Width}x{bounds.Height}).");
        }

        return new NativePoint { X = bounds.X + x, Y = bounds.Y + y };
    }

    private static void MoveCursorTo(NativePoint point)
    {
        if (!SetCursorPos(point.X, point.Y))
        {
            throw new InvalidOperationException("Windows could not move the cursor.");
        }
    }

    private static void EnsureForeground(IntPtr handle)
    {
        if (IsIconic(handle))
        {
            _ = ShowWindowAsync(handle, SwRestore);
        }

        if (GetForegroundWindow() != handle)
        {
            if (!SetForegroundWindow(handle))
            {
                throw new InvalidOperationException(
                    "Windows refused to focus the target before keyboard input.");
            }

            Thread.Sleep(60);
        }

        if (GetForegroundWindow() != handle)
        {
            throw new InvalidOperationException(
                "The target window did not become foreground; keyboard input was cancelled.");
        }
    }

    private static (uint Down, uint Up) ResolveMouseButton(string button) =>
        button.ToLowerInvariant() switch
        {
            "left" => (MouseEventLeftDown, MouseEventLeftUp),
            "right" => (MouseEventRightDown, MouseEventRightUp),
            "middle" => (MouseEventMiddleDown, MouseEventMiddleUp),
            _ => throw new InvalidOperationException($"Unsupported mouse button: {button}")
        };

    private static ushort ResolveModifier(string modifier) =>
        modifier.ToLowerInvariant() switch
        {
            "ctrl" or "control" => (ushort)Keys.ControlKey,
            "alt" => (ushort)Keys.Menu,
            "shift" => (ushort)Keys.ShiftKey,
            "win" or "windows" => (ushort)Keys.LWin,
            _ => throw new InvalidOperationException($"Unsupported modifier: {modifier}")
        };

    private static ushort ResolveVirtualKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Key cannot be empty.");
        }

        if (key.Length == 1)
        {
            var mapped = VkKeyScan(key[0]);
            if (mapped == -1)
            {
                throw new InvalidOperationException($"Windows cannot map key: {key}");
            }

            return unchecked((ushort)(mapped & 0xFF));
        }

        var aliases = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase)
        {
            ["enter"] = Keys.Enter,
            ["escape"] = Keys.Escape,
            ["esc"] = Keys.Escape,
            ["tab"] = Keys.Tab,
            ["backspace"] = Keys.Back,
            ["delete"] = Keys.Delete,
            ["space"] = Keys.Space,
            ["left"] = Keys.Left,
            ["right"] = Keys.Right,
            ["up"] = Keys.Up,
            ["down"] = Keys.Down,
            ["home"] = Keys.Home,
            ["end"] = Keys.End,
            ["pageup"] = Keys.PageUp,
            ["pagedown"] = Keys.PageDown,
            ["insert"] = Keys.Insert
        };

        if (aliases.TryGetValue(key, out var alias))
        {
            return (ushort)alias;
        }

        if (Enum.TryParse<Keys>(key, true, out var parsed))
        {
            return (ushort)parsed;
        }

        throw new InvalidOperationException($"Unsupported key: {key}");
    }

    private static void SendUnicodeCharacter(char character)
    {
        var inputs = new[]
        {
            new NativeInput
            {
                Type = InputKeyboard,
                Union = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        Scan = character,
                        Flags = KeyEventUnicode
                    }
                }
            },
            new NativeInput
            {
                Type = InputKeyboard,
                Union = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        Scan = character,
                        Flags = KeyEventUnicode | KeyEventKeyUp
                    }
                }
            }
        };

        SendInputs(inputs);
    }

    private static void SendVirtualKey(ushort virtualKey, bool keyUp)
    {
        var inputs = new[]
        {
            new NativeInput
            {
                Type = InputKeyboard,
                Union = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = virtualKey,
                        Flags = keyUp ? KeyEventKeyUp : 0
                    }
                }
            }
        };

        SendInputs(inputs);
    }

    private static void SendInputs(NativeInput[] inputs)
    {
        var sent = SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeInput>());

        if (sent != (uint)inputs.Length)
        {
            throw new InvalidOperationException(
                $"Windows accepted {sent} of {inputs.Length} input events.");
        }
    }

    private static ScreenshotResult CaptureRectangle(Rectangle rectangle, string captureMode)
    {
        using var bitmap = new Bitmap(rectangle.Width, rectangle.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                rectangle.Left,
                rectangle.Top,
                0,
                0,
                rectangle.Size,
                CopyPixelOperation.SourceCopy);
        }

        using var memory = new MemoryStream();
        bitmap.Save(memory, ImageFormat.Png);

        return new ScreenshotResult(
            "image/png",
            bitmap.Width,
            bitmap.Height,
            captureMode,
            Convert.ToBase64String(memory.ToArray()));
    }

    private static WindowInfo BuildWindowInfo(IntPtr handle, string title, WindowBounds bounds)
    {
        _ = GetWindowThreadProcessId(handle, out var processId);

        string? processName = null;
        try
        {
            processName = Process.GetProcessById(unchecked((int)processId)).ProcessName;
        }
        catch
        {
            // Some protected/system processes cannot be opened. PID is still useful.
        }

        return new WindowInfo(
            ToWindowId(handle),
            title,
            processId,
            processName,
            bounds);
    }

    private static string GetTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0) return string.Empty;

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool TryGetBounds(IntPtr handle, out WindowBounds bounds)
    {
        if (!GetWindowRect(handle, out var rect))
        {
            bounds = default!;
            return false;
        }

        bounds = new WindowBounds(
            rect.Left,
            rect.Top,
            rect.Right - rect.Left,
            rect.Bottom - rect.Top);

        return true;
    }

    private static IntPtr ParseWindowId(string value)
    {
        var normalized = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value[2..]
            : value;

        if (!long.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, null, out var raw))
        {
            throw new InvalidOperationException($"Invalid window id: {value}");
        }

        return new IntPtr(raw);
    }

    private static string ToWindowId(IntPtr handle) => $"0x{handle.ToInt64():X}";

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(
        uint flags,
        uint dx,
        uint dy,
        uint data,
        UIntPtr extraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        NativeInput[] inputs,
        int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScan(char character);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}

internal sealed record WindowBounds(int X, int Y, int Width, int Height);
internal sealed record WindowInfo(string Id, string Title, uint Pid, string? ProcessName, WindowBounds Bounds);
internal sealed record ScreenshotResult(string MimeType, int Width, int Height, string CaptureMode, string ImageBase64);
internal sealed record CursorPosition(int X, int Y);
internal sealed record InputActionResult(string WindowId, int ScreenX, int ScreenY, string Button, int ClickCount);
internal sealed record ScrollActionResult(string WindowId, int ScreenX, int ScreenY, int Delta);
internal sealed record TextActionResult(string WindowId, int CharactersTyped);
internal sealed record KeyActionResult(string WindowId, string Key, IReadOnlyList<string> Modifiers);
