param([Parameter(Mandatory=$true)][string]$LocalDirectory)
$ErrorActionPreference='Stop'
$principalRoot=[IO.Path]::GetFullPath((Join-Path $env:ProgramData 'MoniiServer'))
$principalData=Join-Path $principalRoot 'data'
$principalApp=Join-Path $principalRoot 'app'
$serverExecutable=Join-Path $principalApp 'runtime\Monii.Server.exe'
$exported=$false
$removed=$false
$wasRunning=$false
Start-Transcript -Path (Join-Path $principalRoot 'uninstall.log') -Append | Out-Null
try {
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    if(-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Ejecuta como administrador de Windows.'}
    $resolvedLocal=[IO.Path]::GetFullPath($LocalDirectory)
    if($resolvedLocal.Equals($principalRoot,[StringComparison]::OrdinalIgnoreCase) -or $resolvedLocal.StartsWith($principalRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'La carpeta local debe estar fuera de la carpeta del servidor.'}
    $service=Get-Service -Name MoniiServer -ErrorAction SilentlyContinue
    if($service){$wasRunning=$service.Status -eq 'Running';Stop-Service MoniiServer;$service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30));$service.Dispose()}
    & $serverExecutable --export-local --data-dir $principalData --local-dir $resolvedLocal
    if($LASTEXITCODE -ne 0){throw 'No se pudo preparar y validar la base local. El servidor no se eliminara.'}
    $exported=$true
    if($service){& sc.exe delete MoniiServer;if($LASTEXITCODE -ne 0){throw 'No se pudo eliminar el servicio.'}}
    $removed=$true
    & $serverExecutable --activate-local --local-dir $resolvedLocal
    if($LASTEXITCODE -ne 0){throw 'No se pudo activar el modo local.'}
    Get-NetFirewallRule -DisplayName 'Monii Server - Red privada' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    # Only installed binaries are removed. Data, certificates, backups and logs are retained.
    $resolvedApp=(Resolve-Path -LiteralPath $principalApp).Path
    if(-not $resolvedApp.Equals((Join-Path $principalRoot 'app'),[StringComparison]::OrdinalIgnoreCase)){throw 'Destino de desinstalacion inesperado.'}
    $items=@(Get-Item -LiteralPath $resolvedApp)+@(Get-ChildItem -LiteralPath $resolvedApp -Recurse -Force)
    if($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }){throw 'La carpeta contiene enlaces. Los binarios se conservan para revision manual.'}
    Remove-Item -LiteralPath $resolvedApp -Recurse -Force
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    if(-not $removed -and $wasRunning){try{Start-Service MoniiServer}catch{Write-Error $_ -ErrorAction Continue}}
    if($removed -and $exported -and (Test-Path -LiteralPath $serverExecutable)){& $serverExecutable --activate-local --local-dir $resolvedLocal}
    exit 1
} finally { Stop-Transcript | Out-Null }
