$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$server = 'D:\Tools\unity-mcp-v10.3.0\.venv\Scripts\mcp-for-unity.exe'
if (!(Test-Path -LiteralPath $server)) { throw "Missing MCP server: $server" }
try {
    $health = Invoke-RestMethod 'http://127.0.0.1:8088/health' -TimeoutSec 2
    if ($health.status -eq 'healthy' -or $health.status -eq 'ok') {
        Write-Output 'Unity MCP already running on 127.0.0.1:8088'
        return
    }
} catch {}
if (Get-NetTCPConnection -LocalPort 8088 -State Listen -ErrorAction SilentlyContinue) {
    throw 'Port 8088 is in use by an unidentified service; refusing to start another server.'
}
New-Item -ItemType Directory -Path (Join-Path $root 'Logs') -Force | Out-Null
$env:UNITY_MCP_TELEMETRY_ENABLED = 'false'
$process = Start-Process -FilePath $server -ArgumentList '--transport http --http-host 127.0.0.1 --http-port 8088' -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'Logs\mcp-server.stdout.log') -RedirectStandardError (Join-Path $root 'Logs\mcp-server.stderr.log')
$process.Id | Set-Content -LiteralPath (Join-Path $root 'Logs\mcp-server.pid')
Write-Output "Unity MCP starting: PID $($process.Id), http://127.0.0.1:8088/mcp"
