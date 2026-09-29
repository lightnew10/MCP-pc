param(
    [string]$Profile = "mcp-pc",
    [string]$TunnelClient = "tunnel-client"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:CONTROL_PLANE_API_KEY)) {
    throw "CONTROL_PLANE_API_KEY is not set in this PowerShell session."
}

Write-Host "Checking MCP-PC Secure MCP Tunnel profile '$Profile'..."
& $TunnelClient doctor --profile $Profile --explain

if ($LASTEXITCODE -ne 0) {
    throw "tunnel-client doctor failed. Fix the reported issue before starting the tunnel."
}

Write-Host ""
Write-Host "Starting MCP-PC Secure MCP Tunnel. Keep this terminal open."
& $TunnelClient run --profile $Profile
exit $LASTEXITCODE
