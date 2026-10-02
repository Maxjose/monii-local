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
    & $dotnetCommand run --project tests/Monii.NetworkVerification -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'La verificación de red falló.' }
    if ($Publish) {
        & $dotnetCommand publish src/Monii.Desktop -c Release -r win-x64 --self-contained true -o artifacts/Monii-0.7.0-win-x64
        if ($LASTEXITCODE -ne 0) { throw 'La publicación local falló.' }
        & $dotnetCommand publish src/Monii.UpdateTool -c Release -r win-x64 --self-contained true -o artifacts/Monii-0.7.0-win-x64
        if ($LASTEXITCODE -ne 0) { throw 'La compilación del actualizador falló.' }
        & $dotnetCommand publish src/Monii.Server -c Release -r win-x64 --self-contained true -o artifacts/Monii-0.7.0-win-x64/server/runtime
        if ($LASTEXITCODE -ne 0) { throw 'La compilación del servidor falló.' }
        Copy-Item -LiteralPath scripts/Install-Server.ps1,scripts/Control-Server.ps1 -Destination artifacts/Monii-0.7.0-win-x64/server -Force
        Copy-Item -LiteralPath docs/RED.md -Destination artifacts/Monii-0.7.0-win-x64/RED.md -Force
    }
} finally { Pop-Location }
