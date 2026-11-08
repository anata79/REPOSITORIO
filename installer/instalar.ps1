# Instala o Monitor Portoconseg neste computador.
# Use pelo instalar.bat (clique com o botão direito > Executar como administrador).
param(
    [string]$Servidor,
    [string]$Chave
)

$ErrorActionPreference = 'Stop'
$TaskName = 'MonitorPortoconseg'
$ExeName = 'MonitorPortoconseg.exe'

function Write-Step($text) { Write-Host "`n> $text" -ForegroundColor Cyan }
function Fail($text) { Write-Host "`nERRO: $text" -ForegroundColor Red; exit 1 }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Fail 'Execute como administrador (botão direito em instalar.bat > Executar como administrador).' }

# O executável vem com a versão no nome (ex.: MonitorPortoconseg-1.0.0.exe). No computador ele é
# instalado sempre como MonitorPortoconseg.exe, para as atualizações substituírem o anterior.
$sourceFile = Get-ChildItem -Path $PSScriptRoot -Filter 'MonitorPortoconseg*.exe' | Sort-Object Name -Descending | Select-Object -First 1
if (-not $sourceFile) { Fail "Arquivo MonitorPortoconseg-<versão>.exe não encontrado ao lado deste instalador. Extraia o .zip inteiro antes de instalar." }
$source = $sourceFile.FullName
$version = $sourceFile.VersionInfo.ProductVersion

Write-Host "Monitor Portoconseg $version - instalação" -ForegroundColor White
if (-not $Servidor) { $Servidor = Read-Host 'Endereço do painel (ex.: https://monitor.portoconseg.com.br)' }
if (-not $Chave) { $Chave = Read-Host 'Chave deste computador (copiada do painel, em Computadores)' }
$Servidor = $Servidor.Trim().TrimEnd('/')
$Chave = $Chave.Trim()
if ($Servidor -notmatch '^https?://') { $Servidor = "https://$Servidor" }

Write-Step 'Conferindo a chave no servidor'
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $null = Invoke-RestMethod -Uri "$Servidor/api/agent/config" -Headers @{ 'X-Agent-Token' = $Chave } -TimeoutSec 20 -UseBasicParsing
    Write-Host 'Chave aceita.' -ForegroundColor Green
} catch {
    Fail "O servidor não aceitou a chave ou não respondeu. Confira o endereço e a chave. Detalhe: $($_.Exception.Message)"
}

$dest = Join-Path $env:ProgramFiles 'MonitorPortoconseg'
$cfgDir = Join-Path $env:ProgramData 'MonitorPortoconseg'

Write-Step 'Parando versão anterior, se houver'
Get-Process -Name 'MonitorPortoconseg' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Write-Step "Copiando o programa para $dest"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Path $source -Destination (Join-Path $dest $ExeName) -Force

Write-Step 'Gravando endereço e chave'
New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
@{ serverUrl = $Servidor; token = $Chave } | ConvertTo-Json | Set-Content -Path (Join-Path $cfgDir 'config.json') -Encoding UTF8
# Administradores e o sistema podem alterar; usuários comuns só podem ler.
& icacls $cfgDir /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null

Write-Step 'Adicionando exceção no Windows Defender'
try {
    Add-MpPreference -ExclusionPath $dest
    Write-Host 'Exceção adicionada.' -ForegroundColor Green
} catch {
    Write-Host 'Não foi possível adicionar a exceção (outro antivírus ou Defender desligado). Se o antivírus bloquear o programa, libere a pasta acima manualmente.' -ForegroundColor Yellow
}

Write-Step 'Configurando para iniciar com o Windows'
$usersGroup = (New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-545')).Translate([System.Security.Principal.NTAccount]).Value
$action = New-ScheduledTaskAction -Execute (Join-Path $dest $ExeName)
$trigger = New-ScheduledTaskTrigger -AtLogOn
# Tenta abrir de novo a cada 5 minutos; se já estiver aberto, a segunda cópia sai sozinha.
$repeat = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5)
$trigger.Repetition = $repeat.Repetition
$principal = New-ScheduledTaskPrincipal -GroupId $usersGroup -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Write-Host 'Tarefa agendada criada.' -ForegroundColor Green

Write-Step 'Iniciando agora'
try { Start-ScheduledTask -TaskName $TaskName } catch { }
Start-Sleep -Seconds 8
if (Get-Process -Name 'MonitorPortoconseg' -ErrorAction SilentlyContinue) {
    Write-Host 'Programa em execução. O ícone azul aparece ao lado do relógio (pode estar na setinha ^).' -ForegroundColor Green
} else {
    Write-Host 'O programa vai iniciar no próximo login. Para começar já, saia da conta do Windows e entre de novo.' -ForegroundColor Yellow
}

Write-Host "`nVersão instalada: $version" -ForegroundColor White
Write-Host "Instalação concluída. Em cerca de 1 minuto o computador aparece como 'Conectado' no painel." -ForegroundColor White
