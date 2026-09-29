# OpenAI Secure MCP Tunnel

MCP-PC stays local. The supported OpenAI path for a private MCP server is Secure MCP Tunnel: tunnel-client runs on the Windows PC, opens outbound HTTPS to OpenAI, and forwards MCP requests to the local stdio gateway.

Official documentation:
https://developers.openai.com/api/docs/guides/secure-mcp-tunnels

## Important ChatGPT plan limitation

As of 2026-09-29, OpenAI documents full MCP support with write/modify actions for ChatGPT Business and Enterprise/Edu. Pro can connect read/fetch MCPs in developer mode, but full MCP actions are not currently available there. A Plus account therefore cannot currently use MCP-PC ACTION tools directly from a normal ChatGPT chat.

The tunnel work in this repository is still useful because the same private MCP can be used by supported OpenAI surfaces, and the repository will already be ready if ChatGPT plan availability changes.

Current ChatGPT MCP availability:
https://help.openai.com/en/articles/12584461-developer-mode-and-mcp-apps-in-chatgpt

## Security model

The tunnel does not open an inbound port on the router or expose the Windows named pipe publicly.

Flow:

~~~text
Supported OpenAI product
        |
        | OpenAI-hosted tunnel endpoint
        v
outbound HTTPS from this PC
        |
        v
tunnel-client
        |
        | stdio
        v
gateway/dist/index.js
        |
        | local named pipe
        v
Windows agent
~~~

The existing MCP-PC policy, process allow/deny rules, audit log, and local STOP file remain authoritative.

Never commit:
- CONTROL_PLANE_API_KEY
- tunnel runtime credentials
- private tunnel configuration exports
- local policy files containing private application names

## Prerequisites

1. Build the gateway.
2. Start the Windows agent.
3. Download the latest tunnel-client from OpenAI Platform tunnel settings or the official OpenAI tunnel-client release.
4. Create an OpenAI-hosted tunnel and obtain its tunnel_id.
5. Obtain a runtime API key authorized for that tunnel.
6. Keep outbound HTTPS access to api.openai.com:443 available.

OpenAI Platform tunnel settings:
https://platform.openai.com/settings/organization/tunnels

## Build MCP-PC

~~~powershell
cd gateway
npm install
npm run typecheck
npm run build
cd ..
~~~

Start the local Windows agent:

~~~powershell
dotnet run --project .\agent-windows\McpPc.Agent.csproj
~~~

## Configure the tunnel

Set the runtime key only in the current PowerShell session:

~~~powershell
$env:CONTROL_PLANE_API_KEY = "YOUR_RUNTIME_KEY"
~~~

Do not put this value in the repository.

Then:

~~~powershell
.\scripts\setup-openai-tunnel.ps1 -TunnelId "tunnel_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
~~~

The script configures profile mcp-pc to launch the built gateway over stdio and then runs tunnel-client doctor.

## Run the tunnel

In a separate PowerShell terminal with CONTROL_PLANE_API_KEY set:

~~~powershell
.\scripts\run-openai-tunnel.ps1
~~~

Keep the process running while using the remote MCP connection.

tunnel-client also exposes local health/readiness/admin surfaces; use the URLs printed by the client rather than assuming a fixed port.

## ChatGPT connection when the account/workspace supports full MCP

OpenAI currently documents the flow as:

1. Enable ChatGPT developer mode for the eligible workspace/account.
2. Create a developer-mode app.
3. Choose Tunnel as the connection type.
4. Select the associated tunnel or enter the tunnel_id.
5. Scan the MCP tools.
6. Review ACTION tools carefully before enabling them.
7. Test READ first, then ACTION with the MCP-PC tray visible.

MCP-PC intentionally keeps DANGEROUS tools unavailable.

## Recommended first remote test

Use this order:

1. policy.get_status
2. desktop.list_windows
3. desktop.capture_window
4. desktop.get_cursor_position
5. desktop.focus_window
6. desktop.move_mouse
7. desktop.click
8. desktop.type_text
9. desktop.capture_window again
10. audit.get_recent_logs

Keep a harmless application such as Notepad as the target for the first ACTION test.

## Emergency stop

The local tray controller can enable Emergency STOP immediately.

The file remains:

~~~text
%LOCALAPPDATA%\MCP-PC\STOP
~~~

A remote MCP caller cannot remove this file through any MCP-PC tool.
