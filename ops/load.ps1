param(
  [int]$Rps = 50,
  [string]$Duration = '15s',
  [double]$MaxLoss = 0.05,
  [string]$Date = (Get-Date -Format 'yyyy-MM-dd')
)
$ErrorActionPreference = 'Stop'
$api = "http://localhost:8080"
$stopAt = (Get-Date).Add([System.TimeSpan]::Parse($Duration))
[int]$ok = 0; [int]$err = 0

$script = {
  param($u)
  try { Invoke-RestMethod -Method Get -Uri $u -TimeoutSec 5 | Out-Null; 0 }
  catch { 1 }
}

while ((Get-Date) -lt $stopAt) {
  $urls = 1..$Rps | ForEach-Object { "$api/balances/daily?date=$Date" }
  $jobs = foreach ($u in $urls) { Start-ThreadJob -ScriptBlock $script -ArgumentList $u }
  Receive-Job -Job $jobs -Wait -AutoRemoveJob | ForEach-Object { if ($_ -eq 0) { $ok++ } else { $err++ } }
  Start-Sleep -Milliseconds 1000
}
$total = [math]::Max(1, $ok + $err)
$loss = $err / $total
if ($loss -gt $MaxLoss) { Write-Error "Loss ratio $([math]::Round($loss,4)) > $MaxLoss"; exit 1 }
Write-Host "OK: rps=$Rps duration=$Duration loss=$([math]::Round($loss,4))"
