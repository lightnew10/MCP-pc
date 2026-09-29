param(
    [Parameter(Mandatory = $true)]
    [string]$TunnelId,
    [string]$Profile = "mcp-pc",
    [string]$TunnelClient = "tunnel-client"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:CONTROL_PLANE_API_KEY)) {
    throw "CONTROL_PLANE_API_KEY is not set. Set it only in your local PowerShell session before running this script."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$gatewayEntry = Join-Path $repoRoot "gateway\dist\index.js"

if (-not (Test-Path $gatewayEntry)) {
    throw "Gateway build not found at $gatewayEntry. Run npm run build in gateway first."
}

$mcpCommand = "node `"$gatewayEntry`""

Write-Host "Configuring OpenAI Secure MCP Tunnel profile '$Profile' for MCP-PC..."
Write-Host "Gateway: $gatewayEntry"

& $TunnelClient init --sample sample_mcp_stdio_local --profile $Profile --tunnel-id $TunnelId --mcp-command $mcpCommand

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Running tunnel diagnostics..."
& $TunnelClient doctor --profile $Profile --explain
exit $LASTEXITCODE
