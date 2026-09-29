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

    public static void EnablePerMonitorDpiAwareness()
    {
        try
        {
            _ = SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);
        }
        catch
        {
            // Best effort. Window enumeration and capture still work without it,
            // but coordinates may be DPI-virtualized on mixed-scale displays.
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
        if (!IsWindow(handle))
        {
            throw new InvalidOperationException($"Window does not exist: {windowId}");
        }

        if (IsIconic(handle))
        {
            throw new InvalidOperationException("The selected window is minimized. Restore it before capturing it.");
        }

        if (!TryGetBounds(handle, out var bounds) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("The selected window has invalid bounds.");
        }

        return CaptureRectangle(
            new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            "visible-window-region");
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
}

internal sealed record WindowBounds(int X, int Y, int Width, int Height);
internal sealed record WindowInfo(string Id, string Title, uint Pid, string? ProcessName, WindowBounds Bounds);
internal sealed record ScreenshotResult(string MimeType, int Width, int Height, string CaptureMode, string ImageBase64);
