param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
Push-Location $projectRoot
try {
    & $dotnetCommand build Monii.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'La compilación falló.' }
    & $dotnetCommand run --project tests/Monii.Verification -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'La verificación falló.' }
    if ($Publish) {
        & $dotnetCommand publish src/Monii.Desktop -c Release -r win-x64 --self-contained true -o artifacts/Monii-0.6.0-win-x64
        if ($LASTEXITCODE -ne 0) { throw 'La publicación local falló.' }
        & $dotnetCommand publish src/Monii.UpdateTool -c Release -r win-x64 --self-contained true -o artifacts/Monii-0.6.0-win-x64
        if ($LASTEXITCODE -ne 0) { throw 'La compilación del actualizador falló.' }
    }
} finally { Pop-Location }
