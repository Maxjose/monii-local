param([Parameter(Mandatory=$true)][string]$Import)
$ErrorActionPreference='Stop'
$principalRoot=Join-Path $env:ProgramData 'MoniiServer'
$principalData=Join-Path $principalRoot 'data'
$principalApp=Join-Path $principalRoot 'app'
New-Item -ItemType Directory -Path $principalRoot -Force | Out-Null
Start-Transcript -Path (Join-Path $principalRoot 'installation.log') -Append | Out-Null
try {
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if(-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Ejecuta como administrador de Windows.'}
    if(Get-Service -Name MoniiServer -ErrorAction SilentlyContinue){ & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Update-Server.ps1"); exit $LASTEXITCODE }
    New-Item -ItemType Directory -Path $principalData,$principalApp -Force | Out-Null
    & icacls.exe $principalData /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-19:(OI)(CI)F' | Out-Null
    if($LASTEXITCODE -ne 0){throw 'No se pudieron proteger los datos.'}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'runtime') -Destination $principalApp -Recurse -Force
    $serverExecutable=Join-Path $principalApp 'runtime\Monii.Server.exe'
    if(Test-Path -LiteralPath (Join-Path $principalData 'monii.db')) {
        & $serverExecutable --prepare --data-dir $principalData
    } else {
        & $serverExecutable --prepare --data-dir $principalData --import (Resolve-Path -LiteralPath $Import).Path
    }
    if($LASTEXITCODE -ne 0){throw 'No se pudo preparar el servidor.'}
    $logs=Join-Path $principalRoot 'logs';New-Item -ItemType Directory -Path $logs -Force | Out-Null
    & icacls.exe $logs /grant '*S-1-5-19:(OI)(CI)M' | Out-Null
    $binary='"'+$serverExecutable+'" --data-dir "'+$principalData+'"'
    & sc.exe create MoniiServer binPath= $binary start= auto obj= 'NT AUTHORITY\LocalService' DisplayName= 'Monii - Servidor del negocio'
    if($LASTEXITCODE -ne 0){throw 'No se pudo registrar el servicio.'}
    & sc.exe failure MoniiServer reset= 86400 actions= restart/5000/restart/15000/restart/30000 | Out-Null
    New-NetFirewallRule -DisplayName 'Monii Server - Red privada' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 58443 -Profile Private -RemoteAddress LocalSubnet -Program $serverExecutable | Out-Null
    New-NetFirewallRule -DisplayName "Monii Server - Descubrimiento local" -Direction Inbound -Action Allow -Protocol UDP -LocalPort 58444 -Profile Private -RemoteAddress LocalSubnet -Program $serverExecutable | Out-Null
    Start-Service MoniiServer
    (Get-Service MoniiServer).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
    exit 0
} catch { Write-Error $_ -ErrorAction Continue;exit 1 }
finally { Stop-Transcript | Out-Null }
