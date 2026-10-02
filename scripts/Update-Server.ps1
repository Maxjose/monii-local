$ErrorActionPreference='Stop'
$serverRoot=[IO.Path]::GetFullPath((Join-Path $env:ProgramData 'MoniiServer'))
$data=Join-Path $serverRoot 'data'
$runtime=Join-Path $serverRoot 'app\runtime'
$bundle=Join-Path $PSScriptRoot 'runtime'
$helper=Join-Path $bundle 'Monii.Server.exe'
$changed=$false
$started=$false
$stopped=$false
$wasRunning=$false
$databaseSaved=$false
$backup=Join-Path $serverRoot ('maintenance\'+[Guid]::NewGuid().ToString('N'))
Start-Transcript -Path (Join-Path $serverRoot 'maintenance.log') -Append | Out-Null
try {
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if(-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Se requieren permisos de administrador de Windows.'}
    if(-not (Test-Path -LiteralPath $helper)){throw 'El portable no incluye el servidor actualizado.'}
    if([IO.Path]::GetFullPath($bundle).Equals($runtime,[StringComparison]::OrdinalIgnoreCase)){throw 'Ejecuta el mantenimiento desde el portable, fuera del servidor instalado.'}
    $service=Get-Service MoniiServer
    $wasRunning=$service.Status -eq 'Running'
    Stop-Service MoniiServer
    $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30));$service.Dispose();$stopped=$true
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    & icacls.exe $backup /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
    if($LASTEXITCODE -ne 0){throw 'No se pudo proteger el respaldo de mantenimiento.'}
    & $helper --maintenance-backup --data-dir $data --target (Join-Path $backup 'monii.db')
    if($LASTEXITCODE -ne 0){throw 'No se pudo respaldar la base; no se reemplazan los binarios.'}
    $databaseSaved=$true
    Copy-Item -LiteralPath $runtime -Destination $backup -Recurse -Force
    $changed=$true
    Copy-Item -Path (Join-Path $bundle '*') -Destination $runtime -Recurse -Force
    & (Join-Path $runtime "Monii.Server.exe") --prepare --data-dir $data
    if($LASTEXITCODE -ne 0){throw "No se pudo preparar el servidor actualizado."}
    if(-not (Get-NetFirewallRule -DisplayName "Monii Server - Descubrimiento local" -ErrorAction SilentlyContinue)){New-NetFirewallRule -DisplayName "Monii Server - Descubrimiento local" -Direction Inbound -Action Allow -Protocol UDP -LocalPort 58444 -Profile Private -RemoteAddress LocalSubnet -Program (Join-Path $runtime "Monii.Server.exe") | Out-Null}
    $started=$true
    Start-Service MoniiServer
    (Get-Service MoniiServer).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
    Start-Sleep -Seconds 2
    if((Get-Service MoniiServer).Status -ne 'Running'){throw 'El servidor actualizado no se mantiene iniciado.'}
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    try {
        if($changed -and $started){throw "El servidor pudo atender nuevas operaciones. No se reemplaza su base por el respaldo; revisa el servicio y maintenance.log."}
        if($changed) {
            Stop-Service MoniiServer -ErrorAction SilentlyContinue
            (Get-Service MoniiServer).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
            if($databaseSaved){& $helper --maintenance-restore --data-dir $data --source (Join-Path $backup 'monii.db');if($LASTEXITCODE -ne 0){throw 'La recuperación de la base requiere revisión manual.'}}
            Copy-Item -Path (Join-Path $backup 'runtime\*') -Destination $runtime -Recurse -Force
        }
        if($stopped -and $wasRunning){Start-Service MoniiServer}
    } catch { Write-Error $_ -ErrorAction Continue }
    exit 1
} finally { Stop-Transcript | Out-Null }
