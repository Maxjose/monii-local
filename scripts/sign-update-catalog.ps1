param([string]$Source='updates/catalog.source.json',[string]$Output='updates/stable.json')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$dotnetCommand=Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if(!(Test-Path -LiteralPath $dotnetCommand)) { $dotnetCommand='dotnet' }
$privateKey=Join-Path $projectRoot '.tools\signing\update-private.pem'
if(!(Test-Path -LiteralPath $privateKey)) { throw 'Recupera la clave privada original. No crees otra para clientes ya distribuidos.' }
Push-Location $projectRoot
try {
    & $dotnetCommand run --project src/Monii.UpdateTool -c Release --no-build -- catalog $Source $privateKey $Output
    if($LASTEXITCODE -ne 0) { throw 'No se pudo firmar el catálogo.' }
} finally { Pop-Location }
