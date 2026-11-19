param(
    [ValidateSet('Import','Verify','Build','Open')][string]$Action = 'Open'
)
$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path $PSScriptRoot -Parent) 'Unity\TankTogether'
$editor = 'D:\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe'
if (!(Test-Path -LiteralPath $editor)) { throw "Missing Unity editor: $editor" }
New-Item -ItemType Directory -Path (Join-Path $root 'Logs') -Force | Out-Null
if (Test-Path -LiteralPath (Join-Path $root 'Temp\UnityLockfile')) {
    $running = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($root) }
    if ($running) { throw 'Project is already open. Use MCP for live operations, or close the editor before batch commands.' }
}
$log = Join-Path $root "Logs\unity-$($Action.ToLower()).log"
$arguments = "-projectPath `"$root`" -logFile `"$log`""
if ($Action -eq 'Open') {
    & (Join-Path $PSScriptRoot 'start-unity-mcp.ps1')
    $process = Start-Process -FilePath $editor -ArgumentList $arguments -PassThru
    Write-Output "Editor PID $($process.Id)"
    return
}
$arguments += ' -batchmode -nographics -quit'
if ($Action -eq 'Verify') { $arguments += ' -executeMethod TankTogether.Editor.ProjectSetup.Verify' }
if ($Action -eq 'Build') { $arguments += ' -executeMethod TankTogether.Editor.ProjectSetup.BuildWindows' }
$process = Start-Process -FilePath $editor -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Unity $Action failed ($($process.ExitCode)). See $log" }
Write-Output "Unity $Action passed. Log: $log"
