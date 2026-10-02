$ErrorActionPreference='Stop'
$root=Join-Path $env:ProgramData 'MoniiServer'
try {
    $principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Se requieren permisos de administrador.'}
    $runtime=Join-Path $root 'app\runtime\Monii.Server.exe'
    if(-not (Test-Path -LiteralPath $runtime)){throw 'El servidor no está instalado.'}
    $logs=Join-Path $root 'logs';New-Item -ItemType Directory -Path $logs -Force | Out-Null
    Start-Transcript -Path (Join-Path $logs 'firewall.log') -Append | Out-Null
    $config=Get-Content -LiteralPath (Join-Path $root 'data\server.json') -Raw | ConvertFrom-Json
    $tcp=if($config.Port){[int]$config.Port}else{58443}
    $udp=if($config.DiscoveryPort){[int]$config.DiscoveryPort}else{58444}
    foreach($rule in @(@{Name='Monii Server - Red privada';Protocol='TCP';Port=$tcp},@{Name='Monii Server - Descubrimiento local';Protocol='UDP';Port=$udp})) {
        if($rule.Port -lt 1 -or $rule.Port -gt 65535){throw 'Puerto de servidor inválido.'}
        Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        New-NetFirewallRule -DisplayName $rule.Name -Direction Inbound -Action Allow -Enabled True -Protocol $rule.Protocol -LocalPort $rule.Port -Profile Private -RemoteAddress LocalSubnet -Program $runtime | Out-Null
    }
    Stop-Transcript | Out-Null
    exit 0
} catch { Write-Error $_ -ErrorAction Continue;try {Stop-Transcript | Out-Null}catch{};exit 1 }
