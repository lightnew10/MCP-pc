# MCP-PC

Local MCP gateway and Windows agent for controlled PC observation and automation.

## Current MVP

The first milestone is intentionally **read-only**. It exposes these MCP tools:

- `desktop.list_windows`
- `desktop.get_active_window`
- `desktop.capture_screen`
- `desktop.capture_window`
- `audit.get_recent_logs`

Mouse, keyboard, shell execution, process control, and file writes are intentionally excluded until the permission model is validated.

## Architecture

```text
MCP client / MCP Inspector
          |
          | stdio
          v
TypeScript MCP Gateway
          |
          | \\.\pipe\mcp-pc-agent
          v
C# / .NET Windows Agent
          |
          +-- Win32 window enumeration
          +-- Foreground window
          +-- Desktop/window screenshots

Gateway audit log
          |
          +-- %LOCALAPPDATA%\MCP-PC\audit.jsonl
```

## Requirements

- Windows 10/11
- Node.js 20 or newer
- .NET 10 SDK

The project uses MCP TypeScript SDK v2 and .NET 10 LTS.

## Run locally

Clone the repository and install/build the gateway:

```powershell
git clone https://github.com/lightnew10/MCP-pc.git
cd MCP-pc
cd gateway
npm install
npm run typecheck
npm run build
cd ..
```

Start the Windows agent in terminal 1:

```powershell
dotnet run --project .\agent-windows\McpPc.Agent.csproj
```

You should see:

```text
MCP-PC Windows agent listening on \\.\pipe\mcp-pc-agent
```

Then launch MCP Inspector in terminal 2 against the built gateway:

```powershell
npx @modelcontextprotocol/inspector node .\gateway\dist\index.js
```

From Inspector, call `desktop.list_windows`, copy one returned window id, then call `desktop.capture_window` with that id.

## Screenshot behavior

`desktop.capture_screen` captures the complete Windows virtual desktop.

`desktop.capture_window` currently captures the **visible screen rectangle** occupied by the chosen window. This is deliberately simple for the MVP:

- minimized windows are rejected;
- overlapping windows can appear in the image;
- the image is returned to MCP as `image/png` data, not as a local file path.

A later phase can replace this implementation with Windows Graphics Capture for more reliable application/window capture.

## Audit logging

Every gateway tool call is written as JSONL to:

```text
%LOCALAPPDATA%\MCP-PC\audit.jsonl
```

Override the location with:

```powershell
$env:MCP_PC_DATA_DIR = "C:\path\to\logs"
```

Screenshot bytes are **not** stored in the audit log.

## Configuration

| Variable | Default | Purpose |
| --- | --- | --- |
| `MCP_PC_PIPE` | `\\.\pipe\mcp-pc-agent` | Windows agent named pipe |
| `MCP_PC_AGENT_TIMEOUT_MS` | `10000` | Gateway-to-agent timeout |
| `MCP_PC_DATA_DIR` | `%LOCALAPPDATA%\MCP-PC` | Audit log directory |

## Repository layout

```text
gateway/        TypeScript MCP server
agent-windows/  .NET 10 Windows agent
docs/           Security and architecture notes
.github/         CI build validation
```

## Security

The current MCP surface is `READ` only. See `docs/SECURITY.md` for the trust boundaries and the planned `ACTION` / `DANGEROUS` permission classes.

## Next milestones

1. Validate the MVP against MCP Inspector on a real Windows desktop.
2. Improve window capture with Windows Graphics Capture.
3. Add a local permission/policy service.
4. Only then add mouse and keyboard tools.
5. Add authenticated remote/Secure MCP transport for ChatGPT integration.
