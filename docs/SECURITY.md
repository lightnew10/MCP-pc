# Security model

MCP-PC is designed around least privilege. The MCP gateway must never imply that a capability exists unless a corresponding tool is explicitly registered and permitted.

## Permission classes

### READ

Observation only.

Examples:
- Enumerate visible windows
- Read the active window and cursor position
- Capture the desktop or a visible window region
- Read policy status and MCP-PC audit logs

### ACTION

Interactive desktop state changes. Implemented in v0.2.0 and guarded by the local policy engine.

Examples:
- Focus a window
- Move/click/scroll the mouse inside a selected window
- Type text into a selected window
- Press a key or key combination in a selected window

Every ACTION tool targets a window id returned by desktop.list_windows. Mouse coordinates are relative to that window rather than unrestricted virtual-desktop coordinates.

### DANGEROUS

Not exposed by the MCP server.

Examples:
- Shell execution
- File deletion/write
- Process termination
- Elevated operations

DANGEROUS is disabled by default and no DANGEROUS MCP tools are registered.

## Local policy

The shared policy file is:

~~~text
%LOCALAPPDATA%\MCP-PC\policy.json
~~~

The gateway creates a default policy on first run:

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

allowedProcesses set to an empty array means all normal desktop processes are eligible except entries in deniedProcesses. Process names use the Windows process name without the .exe extension.

The policy is checked in both the TypeScript gateway and the Windows agent for ACTION calls.

## Emergency stop

Create this file locally:

~~~text
%LOCALAPPDATA%\MCP-PC\STOP
~~~

As long as it exists, ACTION requests are rejected by both the gateway policy flow and Windows agent.

PowerShell:

~~~powershell
New-Item "$env:LOCALAPPDATA\MCP-PC\STOP" -ItemType File -Force
~~~

Resume actions locally:

~~~powershell
Remove-Item "$env:LOCALAPPDATA\MCP-PC\STOP"
~~~

There is deliberately no MCP tool that can remove the STOP file. A remote/client-side request therefore cannot disable the local emergency stop.

## Trust boundaries

~~~text
MCP client
  |
  | stdio / future authenticated transport
  v
TypeScript gateway
  |
  | policy check + audit
  v
local Windows named pipe: mcp-pc-agent
  |
  | second ACTION policy check
  v
Windows agent
  |
  v
Win32 capture / input APIs
~~~

## Internal agent methods

READ:
- windows.list
- windows.active
- screen.capture
- screen.captureWindow
- cursor.position

ACTION:
- windows.focus
- input.moveMouse
- input.click
- input.doubleClick
- input.scroll
- input.typeText
- input.pressKey
- input.keyCombination

## Screenshots

Screenshots can contain sensitive information visible on the desktop. Image bytes are returned to the requesting MCP client but are not written into the audit log by the gateway.

Window capture still uses the visible screen rectangle occupied by the selected window. Overlapping windows can therefore appear in the capture and minimized windows are rejected. A later capture milestone will move to Windows Graphics Capture or another compositor-backed implementation.

## Keyboard safety

Keyboard tools require a target windowId. Before text or keys are injected, the Windows agent attempts to bring that exact window to the foreground and verifies that it became foreground. If it cannot do so, keyboard input is cancelled.

desktop.type_text is capped by maxTextLength. The actual text is sent to the Windows agent but is redacted from audit.jsonl; only the target window and text length are logged.

## Audit log

The gateway writes JSONL records under:

~~~text
%LOCALAPPDATA%\MCP-PC\audit.jsonl
~~~

The path can be overridden with MCP_PC_DATA_DIR.

Audit entries contain tool name, sanitized arguments, permission class, status, duration, and error text. Screenshot bytes and typed text are excluded.

## Remaining hardening

Before DANGEROUS capabilities or remote production use:

1. Add an authenticated remote transport/tunnel.
2. Add request-size and action-rate limits.
3. Add a local tray/controller UI for policy and STOP state.
4. Improve window capture with Windows Graphics Capture.
5. Add stronger protected-window/session detection.
6. Add tests for UAC/elevated-window behavior and mixed-DPI/multi-monitor setups.
