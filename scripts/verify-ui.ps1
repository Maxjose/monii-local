$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$testIdentity = [Guid]::NewGuid().ToString('N')
$testDirectory = Join-Path $projectRoot "artifacts\ui-test-$testIdentity"
$reviewDirectory = Join-Path $projectRoot 'artifacts\ui-review'
Push-Location $projectRoot
try {
    & $dotnetCommand build src/Monii.Desktop -c Release
    if ($LASTEXITCODE -ne 0) { throw 'La compilación falló.' }
    & $dotnetCommand 'src\Monii.Desktop\bin\Release\net10.0-windows\Monii.dll' --data-dir $testDirectory --ui-verify $reviewDirectory
    if ($LASTEXITCODE -ne 0) { throw 'La verificación WPF falló. Revisa artifacts/ui-review/ui-error.txt.' }
    Get-Content -LiteralPath (Join-Path $reviewDirectory 'ui-verification.txt')
} finally { Pop-Location }
