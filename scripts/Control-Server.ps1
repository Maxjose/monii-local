param([ValidateSet('Start','Stop','Restart')][string]$Action='Start')
$ErrorActionPreference='Stop'
try {
    switch($Action){ 'Start' {Start-Service MoniiServer} 'Stop' {Stop-Service MoniiServer} 'Restart' {Restart-Service MoniiServer} }
    $expected=if($Action -eq 'Stop'){'Stopped'}else{'Running'}
    (Get-Service MoniiServer).WaitForStatus($expected,[TimeSpan]::FromSeconds(30))
    exit 0
} catch { Write-Error $_ -ErrorAction Continue;exit 1 }
