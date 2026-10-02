param([string]$DataDirectory = '')
$projectRoot = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$projectPath = Join-Path $projectRoot 'src\Monii.Desktop'
if ($DataDirectory) { & $dotnetCommand run --project $projectPath -- --data-dir $DataDirectory }
else { & $dotnetCommand run --project $projectPath }
