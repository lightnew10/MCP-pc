# MCP-PC

Local MCP gateway and Windows agent for controlled PC observation and automation.

## MVP scope

The first milestone is intentionally read-only:

- List visible Windows desktop windows
- Get the active foreground window
- Capture the full screen
- Capture a selected window
- Read recent audit logs

Mouse, keyboard, shell execution, process control, and file writes are intentionally excluded from the MVP until the permission model is validated.

## Architecture

```text
MCP client
    |
    v
TypeScript MCP Gateway
    |
    | Windows Named Pipe
    v
C#/.NET Windows Agent
    |
    +-- Window enumeration
    +-- Foreground window
    +-- Screen/window capture
    +-- Audit logging
```

## Repository layout

```text
gateway/        TypeScript MCP server
agent-windows/  .NET Windows agent
shared/         Protocol documentation and shared schemas
data/           Runtime data (ignored by Git)
docs/           Architecture and security documentation
```

## Security model

Every tool is assigned a permission level. The MVP exposes only `READ` tools. Later phases will add `ACTION` and `DANGEROUS` operations behind explicit policy checks and user confirmation.

## Status

Phase 1 scaffold in progress.
