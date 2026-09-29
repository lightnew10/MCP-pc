# MCP-PC

Local MCP gateway and Windows agent for controlled PC observation and desktop automation.

## Current version: v0.2.0

The read-only MVP has been validated on a real Windows machine. v0.2.0 adds a local policy engine and window-targeted ACTION tools while keeping DANGEROUS capabilities out of the MCP surface.

### READ tools

- desktop.list_windows
- desktop.get_active_window
- desktop.get_cursor_position
- desktop.capture_screen
- desktop.capture_window
- policy.get_status
- audit.get_recent_logs

### ACTION tools

- desktop.focus_window
- desktop.move_mouse
- desktop.click
- desktop.double_click
- desktop.scroll
- desktop.type_text
- desktop.press_key
- desktop.key_combination

### Not exposed

- Shell / arbitrary command execution
- File writes or deletion
- Process termination
- UAC/elevation

## Architecture

~~~text
MCP client / MCP Inspector
          |
          | stdio
          v
TypeScript MCP Gateway
          |
          +-- policy + audit
          |
          | \\.\pipe\mcp-pc-agent
          v
C# / .NET Windows Agent
          |
          +-- second ACTION policy check
          +-- Win32 window enumeration
          +-- Desktop/window screenshots
          +-- Mouse / keyboard input

Local state
          |
          +-- %LOCALAPPDATA%\MCP-PC\policy.json
          +-- %LOCALAPPDATA%\MCP-PC\audit.jsonl
          +-- %LOCALAPPDATA%\MCP-PC\STOP
~~~

## Requirements

- Windows 10/11
- Node.js 22.19.0 or newer for the current MCP Inspector workflow
- .NET 10 SDK

The project uses MCP TypeScript SDK v2 and .NET 10 LTS.

## Update and build

~~~powershell
git pull
cd gateway
npm install
npm run typecheck
npm run build
cd ..
~~~

## Run locally

Terminal 1:

~~~powershell
dotnet run --project .\agent-windows\McpPc.Agent.csproj
~~~

Expected:

~~~text
MCP-PC Windows agent listening on \\.\pipe\mcp-pc-agent
~~~

Terminal 2:

~~~powershell
npx @modelcontextprotocol/inspector node .\gateway\dist\index.js
~~~

Reconnect the node server in Inspector after rebuilding.

## Policy

On first run MCP-PC creates:

~~~text
%LOCALAPPDATA%\MCP-PC\policy.json
~~~

Default:

~~~json
{
  "version": 1,
  "permissions": {
    "READ": true,
    "ACTION": true,
    "DANGEROUS": false
  },
  "allowedProcesses": [],
  "deniedProcesses": [
    "CredentialUIBroker",
    "LockApp",
    "LogonUI"
  ],
  "maxTextLength": 4000
}
~~~

An empty allowedProcesses list allows normal desktop applications except explicitly denied process names.

Use policy.get_status in Inspector to verify the effective configuration and emergency-stop state.

## Emergency stop

To immediately block all ACTION tools:

~~~powershell
New-Item "$env:LOCALAPPDATA\MCP-PC\STOP" -ItemType File -Force
~~~

To resume:

~~~powershell
Remove-Item "$env:LOCALAPPDATA\MCP-PC\STOP"
~~~

There is intentionally no MCP tool that removes STOP. It must be removed locally.

## Coordinates

Mouse ACTION tools require a windowId and use coordinates relative to that window.

If desktop.capture_window returns an image 1000 x 700:

~~~json
{
  "windowId": "0x123456",
  "x": 500,
  "y": 350
}
~~~

targets the approximate center of that captured window, regardless of the window's absolute desktop position.

Coordinates outside the current window bounds are rejected.

## Recommended v0.2 test

Open Notepad manually, then:

1. desktop.list_windows
2. Copy the Notepad windowId.
3. desktop.capture_window to see its current dimensions.
4. desktop.focus_window.
5. desktop.click somewhere inside the text editor using relative x/y coordinates.
6. desktop.type_text with a short test string.
7. desktop.capture_window again and confirm the text is visible.
8. audit.get_recent_logs and confirm ACTION entries are present.

For desktop.type_text, the actual text is redacted from the audit log. Only its length and target window are logged.

Then validate the emergency stop:

1. Create %LOCALAPPDATA%\MCP-PC\STOP with the PowerShell command above.
2. Retry desktop.move_mouse or desktop.click.
3. The ACTION must be rejected.
4. Remove STOP locally.
5. Retry the action.

## Screenshot behavior

desktop.capture_screen captures the complete Windows virtual desktop.

desktop.capture_window currently captures the visible screen rectangle occupied by the chosen window:

- minimized windows are rejected;
- overlapping windows can appear in the image;
- the image is returned to MCP as image/png data, not as a local file path.

A later milestone will replace this with Windows Graphics Capture or another compositor-backed implementation.

## Audit logging

Every gateway tool call is written as JSONL to:

~~~text
%LOCALAPPDATA%\MCP-PC\audit.jsonl
~~~

Override the location with:

~~~powershell
$env:MCP_PC_DATA_DIR = "C:\path\to\logs"
~~~

Screenshot bytes and typed text are not stored in the audit log.

## Configuration

| Variable | Default | Purpose |
| --- | --- | --- |
| MCP_PC_PIPE | \\.\pipe\mcp-pc-agent | Windows agent named pipe |
| MCP_PC_AGENT_TIMEOUT_MS | 10000 | Gateway-to-agent timeout |
| MCP_PC_DATA_DIR | %LOCALAPPDATA%\MCP-PC | Policy/audit/STOP directory |

## Repository layout

~~~text
gateway/        TypeScript MCP server + policy engine
agent-windows/  .NET 10 Windows agent + Win32 input
docs/           Security and architecture notes
.github/         CI build validation
~~~

## Security

ACTION requests are checked by the gateway and again by the Windows agent. Keyboard input is cancelled if the requested target cannot be made the foreground window.

See docs/SECURITY.md for the full model.

## Next milestones

1. Validate all v0.2 ACTION tools on the real Windows desktop.
2. Improve window capture with Windows Graphics Capture.
3. Add a local tray/controller UI for policy and emergency-stop state.
4. Add action rate limits and stronger protected-window detection.
5. Add authenticated remote transport for ChatGPT/OpenAI integration.
6. Keep DANGEROUS tools disabled until a separate confirmation design is implemented.
