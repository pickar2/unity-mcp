# Run MCP for Unity server from local source
# Usage: .\run-local.ps1 [-Port 8681]
param(
    [int]$Port = 8681
)

$url = "http://localhost:$Port"
Set-Location $PSScriptRoot\Server
uv run --directory . mcp-for-unity --transport http --http-url $url
