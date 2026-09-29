# Security model

MCP-PC is designed around least privilege. The MCP gateway must never imply that a capability exists unless a corresponding tool is explicitly registered and permitted.

## Permission classes

### READ
Observation only. The current MVP is restricted to this class.

Examples:
- Enumerate visible windows
- Read the active window
- Capture the desktop or a visible window region
- Read MCP-PC audit logs

### ACTION
Future capability. Changes interactive state without being inherently destructive.

Examples:
- Mouse movement/clicks
- Keyboard input
- Opening an allow-listed application

### DANGEROUS
Future capability. May modify persistent data, execute arbitrary code, or cause irreversible effects.

Examples:
- Shell execution
- File deletion/write outside approved scopes
- Process termination
- Elevated operations

DANGEROUS tools must not be added without an explicit confirmation and policy layer.

## Current trust boundaries

```text
MCP client
  |
  | stdio / future authenticated HTTP transport
  v
TypeScript gateway
  |
  | local Windows named pipe: mcp-pc-agent
  v
Windows agent
  |
  v
Win32 / desktop capture APIs
```

The Windows agent currently exposes only four internal methods:

- `windows.list`
- `windows.active`
- `screen.capture`
- `screen.captureWindow`

## Screenshots

Screenshots can contain sensitive information visible on the desktop. Image bytes are returned to the requesting MCP client but are not written into the audit log by the gateway.

The MVP window capture uses the visible screen rectangle occupied by the selected window. This means overlapping windows can appear in the capture. Minimized windows are rejected.

## Audit log

The gateway writes JSONL records under `%LOCALAPPDATA%\\MCP-PC\\audit.jsonl` by default. The path can be overridden with `MCP_PC_DATA_DIR`.

Audit entries contain tool name, arguments, permission class, status, duration, and error text. Binary screenshot data is excluded.

## Future hardening

Before ACTION or DANGEROUS tools are introduced:

1. Add an explicit local policy engine.
2. Add per-tool allow/deny configuration.
3. Add user confirmation for dangerous operations.
4. Add a local emergency stop.
5. Authenticate any non-local MCP transport.
6. Add rate limits and request-size limits.
7. Redact sensitive arguments in audit records.
