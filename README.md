# MCP-PC

Local MCP gateway and Windows agent for controlled PC observation and desktop automation.

## Current version: v0.3.0

v0.2 desktop ACTION tools have been validated on a real Windows machine. v0.3 adds a local tray safety controller, improves window capture when windows overlap, and prepares the repository for OpenAI Secure MCP Tunnel.

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
MCP Inspector / supported remote MCP client
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
          +-- local tray controller
          +-- second ACTION policy check
          +-- Win32 window capture
          +-- Mouse / keyboard input

Optional remote path
          |
Supported OpenAI product
          |
OpenAI Secure MCP Tunnel
          |
tunnel-client on this PC
          |
          +--> local stdio gateway

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

A shield icon named MCP-PC v0.3.0 should also appear in the Windows notification area.

Terminal 2:

~~~powershell
npx @modelcontextprotocol/inspector node .\gateway\dist\index.js
~~~

Reconnect the node server in Inspector after rebuilding.

## v0.3 tray controller

Right-click the MCP-PC shield icon to:

- see whether ACTION is enabled, READ-only, or STOPPED;
- activate/deactivate the local Emergency STOP;
- open policy.json;
- open audit.jsonl;
- open the MCP-PC data directory;
- exit the Windows agent.

The tray runs only in an interactive Windows session. Set MCP_PC_NO_TRAY=1 to disable it.

The tray is intentionally local. There is no MCP tool that can remotely clear the STOP state.

## Policy

Default policy location:

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

## Emergency stop

PowerShell still works in addition to the tray:

~~~powershell
New-Item "$env:LOCALAPPDATA\MCP-PC\STOP" -ItemType File -Force
~~~

Resume:

~~~powershell
Remove-Item "$env:LOCALAPPDATA\MCP-PC\STOP"
~~~

## Window capture in v0.3

desktop.capture_window now tries the Win32 PrintWindow path first. This asks the target application to render its own window into the capture bitmap, so a normal overlapping window is not normally included.

If the application rejects PrintWindow, MCP-PC falls back to the proven visible-window-region capture.

The returned captureMode identifies the path:

~~~text
window-printwindow
visible-window-region-fallback
~~~

Some GPU/composition-heavy applications can still return incomplete or blank content through PrintWindow. Windows Graphics Capture remains the next capture backend for those applications. Microsoft documents CreateForWindow for Windows 10 version 1903 and later.

## Coordinates and keyboard

Mouse ACTION tools require a windowId and use coordinates relative to that window.

Keyboard tools attempt to bring that exact target to the foreground before SendInput is allowed. Typed text is redacted from the audit log.

## Recommended v0.3 local test

1. Start the updated Windows agent and verify the tray icon appears.
2. Use desktop.capture_window on Notepad.
3. Put another normal window partly over Notepad.
4. Capture Notepad again. Check captureMode and verify the overlapping window is not included when window-printwindow is used.
5. Right-click the tray and enable Emergency STOP.
6. Retry desktop.click: it must be rejected.
7. Clear STOP from the tray.
8. Retry desktop.click: it must work.
9. Verify audit.get_recent_logs.

## OpenAI Secure MCP Tunnel

The repository contains:

~~~text
scripts/setup-openai-tunnel.ps1
scripts/run-openai-tunnel.ps1
docs/OPENAI_TUNNEL.md
~~~

The tunnel keeps MCP-PC private and uses outbound HTTPS rather than opening an inbound port.

Do not put runtime API keys or tunnel secrets in this repository.

See docs/OPENAI_TUNNEL.md for the current setup and ChatGPT plan limitations.

## Audit logging

Every gateway tool call is written as JSONL to:

~~~text
%LOCALAPPDATA%\MCP-PC\audit.jsonl
~~~

Screenshot bytes and typed text are not stored in the audit log.

## Configuration

| Variable | Default | Purpose |
| --- | --- | --- |
| MCP_PC_PIPE | \\.\pipe\mcp-pc-agent | Windows agent named pipe |
| MCP_PC_AGENT_TIMEOUT_MS | 10000 | Gateway-to-agent timeout |
| MCP_PC_DATA_DIR | %LOCALAPPDATA%\MCP-PC | Policy/audit/STOP directory |
| MCP_PC_NO_TRAY | unset | Set to 1 to disable the local tray |

## Repository layout

~~~text
gateway/        TypeScript MCP server + policy engine
agent-windows/  .NET 10 Windows agent + tray + Win32 integration
scripts/        Local setup/run helpers
docs/           Security, tunnel, and architecture notes
.github/         CI build validation
~~~

## Security

ACTION requests are checked by the gateway and again by the Windows agent. The local STOP state remains outside remote MCP control.

See docs/SECURITY.md for the full model.

## Next milestones

1. Validate v0.3 tray and occlusion-resistant capture on the real Windows desktop.
2. Add Windows Graphics Capture for GPU/composition-heavy windows.
3. Add action rate limits and stronger protected-window detection.
4. Validate Secure MCP Tunnel on an eligible OpenAI surface.
5. Keep DANGEROUS tools disabled until a separate confirmation design is implemented.
