param([Parameter(Mandatory=$true)][string]$Version,[string]$Notes='Actualización de Monii',[string]$PackageUrl='',[string]$PortableDirectory='artifacts/Monii-0.6.0-win-x64')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$dotnetCommand=Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if(!(Test-Path -LiteralPath $dotnetCommand)) { $dotnetCommand='dotnet' }
$privateKey=Join-Path $projectRoot '.tools\signing\update-private.pem'
if(!(Test-Path -LiteralPath $privateKey)) { throw 'Falta la clave privada de firma. Recupera la clave original; no generes otra para versiones ya distribuidas.' }
$package=Join-Path $projectRoot ("artifacts\Monii-"+$Version+'-update.zip')
if(Test-Path -LiteralPath $package) { throw 'El paquete ya existe. Usa un nuevo número de versión o conserva el paquete actual.' }
Push-Location $projectRoot
try {
    & $dotnetCommand run --project src/Monii.UpdateTool -c Release --no-build -- pack $PortableDirectory $privateKey $package $Version $Notes $PackageUrl
    if($LASTEXITCODE -ne 0) { throw 'No se pudo firmar el paquete.' }
} finally { Pop-Location }
