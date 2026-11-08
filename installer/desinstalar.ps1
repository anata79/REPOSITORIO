# Remove o Monitor Portoconseg deste computador.
$ErrorActionPreference = 'Continue'
$TaskName = 'MonitorPortoconseg'
$dest = Join-Path $env:ProgramFiles 'MonitorPortoconseg'
$cfgDir = Join-Path $env:ProgramData 'MonitorPortoconseg'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Write-Host 'Execute como administrador.' -ForegroundColor Red; exit 1 }

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
Get-Process -Name 'MonitorPortoconseg' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
try { Remove-MpPreference -ExclusionPath $dest } catch { }
Remove-Item -Recurse -Force $dest -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $cfgDir -ErrorAction SilentlyContinue
Write-Host 'Monitor Portoconseg removido. Os dados já enviados continuam no painel.' -ForegroundColor Green
