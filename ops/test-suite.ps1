Param(
  [string]$Api = "http://localhost:8080",
  [string]$ApiKey = "admin",
  [switch]$NoTeardown,   # mantém, mas agora é ignorado (não há teardown automático)
  [switch]$Verbose,
  [switch]$Load
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSVersion.Major -lt 7) {
  Write-Error 'PowerShell 7 (pwsh) é obrigatório. Instale PS7 e execute novamente.'
  exit 1
}

#--- Run context & output folder ---
$Global:RunId   = Get-Date -Format "yyyyMMdd-HHmmss"
$Global:OutDir  = Join-Path "out" $RunId
New-Item -ItemType Directory -Force -Path $Global:OutDir | Out-Null

$Global:Steps = @()  # will collect structured step info
$ProgressPreference = 'SilentlyContinue'  # cleaner console

function Compose {
  try { docker compose version | Out-Null; docker compose @Args }
  catch { docker-compose @Args }
}

function Invoke-HttpStep {
  param(
    [Parameter(Mandatory)][ValidateSet('GET','POST','PUT','DELETE','PATCH')] [string]$Method,
    [Parameter(Mandatory)][string]$Url,
    [hashtable]$Headers,
    [string]$Body,
    [string]$Label = "step"
  )

  $ts   = Get-Date -Format "yyyyMMdd-HHmmssfff"
  $base = Join-Path $Global:OutDir "$ts-$Label"

  if ($Headers) { $Headers | ConvertTo-Json | Out-File "$base-headers.json" -Encoding utf8 }
  if ($Body)    { $Body                | Out-File "$base-req.json"     -Encoding utf8 }

  $invokeParams = @{ Method = $Method; Uri = $Url; SkipHttpErrorCheck = $true }
  if ($Headers) { $invokeParams.Headers = $Headers }
  if ($Body)    { $invokeParams.ContentType = 'application/json'; $invokeParams.Body = $Body }

  $resp = Invoke-WebRequest @invokeParams

  # ---- Console output (human-friendly) ----
  Write-Host "`n== $Label ==" -ForegroundColor Cyan
  Write-Host "-> $Method $Url"
  if ($Headers) { Write-Host "-> Headers:"; ($Headers.GetEnumerator() | Sort-Object Name | Format-Table -AutoSize | Out-String).Trim() | Write-Host }
  if ($Body)    { Write-Host "-> Payload:"; $Body | Write-Host }

  $statusColor = if([int]$resp.StatusCode -ge 400){'Red'}else{'Green'}
  Write-Host "<- Status: $($resp.StatusCode)" -ForegroundColor $statusColor
  if ($resp.Headers.Location) { Write-Host "<- Location: $($resp.Headers.Location)" }

  $pretty = $null
  if ($resp.Content) {
    try   { $pretty = $resp.Content | ConvertFrom-Json | ConvertTo-Json -Depth 15 }
    catch { $pretty = $resp.Content }
    Write-Host $pretty
  }

  # ---- Artifacts ----
  $resp.Content | Out-File "$base-resp.json" -Encoding utf8
  "$( [int]$resp.StatusCode )" | Out-File "$base-status.txt" -Encoding ascii
  if ($resp.Headers.Location) { "$($resp.Headers.Location)" | Out-File "$base-location.txt" -Encoding utf8 }

  # ---- Append to in-memory step log ----
  $entry = [pscustomobject]@{
    ts       = (Get-Date).ToString("o")
    label    = $Label
    method   = $Method
    url      = $Url
    status   = [int]$resp.StatusCode
    location = "$($resp.Headers.Location)"
    request  = if ($Body) { try { $Body | ConvertFrom-Json } catch { $Body } } else { $null }
    response = if ($pretty) { try { $pretty | ConvertFrom-Json } catch { $pretty } } else { $null }
  }
  $Global:Steps += $entry

  return $resp
}

# ---- Run totals & helpers ----
$RunTotals = [ordered]@{
  DepositedCents      = 0
  WithdrawnCents      = 0
  LatestBalanceCents  = $null
}

function Format-BRL([int]$cents) {
  $v = [decimal]$cents / 100
  return ("R$ {0:N2}" -f $v)
}

function Print-RunSummary([string]$context = "Done") {
  $dep = Format-BRL $RunTotals.DepositedCents
  $wdw = Format-BRL $RunTotals.WithdrawnCents
  $bal = if ($RunTotals.LatestBalanceCents -ne $null) { Format-BRL $RunTotals.LatestBalanceCents } else { "(unknown)" }
  Write-Host ("`n{0}. Summary → Deposited {1}, Withdrawn {2}, Balance {3}`n" -f $context, $dep, $wdw, $bal) -ForegroundColor Green
}

function Submit-Entry {
  param(
    [Parameter(Mandatory)] [ValidateSet('CREDIT','DEBIT')] [string]$Type,
    [Parameter(Mandatory)] [int]$AmountCents,
    [string]$OnDate = (Get-Date -Format 'yyyy-MM-dd'),
    [string]$Description = "",
    [string]$Label = "entry-create"
  )
  $body = @{ occurredOn=$OnDate; type=$Type; amountCents=$AmountCents; description=$Description } | ConvertTo-Json
  $headers = @{ 'X-API-Key'=$ApiKey; 'Idempotency-Key'=[guid]::NewGuid().Guid }
  return Invoke-HttpStep -Method POST -Url "$Api/ledger/entries" -Headers $headers -Body $body -Label $Label
}

function Show-DailyBalance {
  param([string]$Date, [string]$Label = 'balance-daily')
  if (-not $Date) { $Date = (Get-Date -Format 'yyyy-MM-dd') }
  $r = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$Date" -Label $Label
  try {
    $obj = $r.Content | ConvertFrom-Json
    $RunTotals.LatestBalanceCents = [int]$obj.balanceCents
    Write-Host ("Balance on {0}: {1}" -f $Date, (Format-BRL $RunTotals.LatestBalanceCents))
    return $RunTotals.LatestBalanceCents
  } catch {
    Write-Warning "Could not parse balance response."
    return $null
  }
}

Write-Host "`n== Subindo stack ==" -ForegroundColor Cyan
Compose down -v | Out-Null          # limpa antes de subir
Compose up -d --build | Out-Null
Compose ps

# Aguardar healthchecks via endpoints (inclui gateway)
function Wait-Healthy {
  param([string]$Url, [int]$TimeoutSec = 60)
  $deadline = (Get-Date).AddSeconds($TimeoutSec)
  do {
    try {
      $r = Invoke-RestMethod -Method Get -Uri $Url -TimeoutSec 5 -ErrorAction Stop
      if ($r.status -eq 'UP') { return $true }
    } catch { }
    Start-Sleep -Seconds 1
  } while ((Get-Date) -lt $deadline)
  return $false
}

# NUNCA derruba em falha: apenas lança erro
if(-not (Wait-Healthy -Url 'http://localhost:8080/actuator/health' -TimeoutSec 60)) { throw 'Gateway not healthy' }
if(-not (Wait-Healthy -Url 'http://localhost:8081/actuator/health' -TimeoutSec 60)) { throw 'Ledger not healthy' }
if(-not (Wait-Healthy -Url 'http://localhost:8082/actuator/health' -TimeoutSec 60)) { throw 'Consolidator not healthy' }
if(-not (Wait-Healthy -Url 'http://localhost:8083/actuator/health' -TimeoutSec 60)) { throw 'Balance not healthy' }

function Dump-Logs {
  Write-Host "`n== Últimos logs (10m, tail 200) ==" -ForegroundColor Yellow
  try { Compose ps | Out-String | Write-Host } catch {}
  foreach ($s in 'postgres','rabbitmq','ledger-service','consolidator-service','balance-query-service','api-gateway') {
    Write-Host "`n-- $s --" -ForegroundColor Yellow
    try { Compose logs --no-color $s --since=10m | Select-Object -Last 500 | Out-String | Write-Host } catch {}
  }
}

# TRAP sem teardown
trap {
  Dump-Logs
  exit 1
}

$day  = Get-Date -Format 'yyyy-MM-dd'
$idem = [guid]::NewGuid().Guid
$body = @{ occurredOn=$day; type='CREDIT'; amountCents=1000; description='smoke' } | ConvertTo-Json

Write-Host "`n== POST /ledger/entries (201) ==" -ForegroundColor Cyan
$r1 = Invoke-HttpStep -Method POST -Url "$Api/ledger/entries" -Headers @{ 'X-API-Key'=$ApiKey; 'Idempotency-Key'=$idem } -Body $body -Label 'ledger-create'
$r1.StatusCode; $loc = $r1.Headers.Location
if ($r1.StatusCode -ne 201) { throw "Esperado 201, obtido $($r1.StatusCode)" }
if($Verbose){ $r1.RawContent }

# Guardar consistência eventual: aguardar saldo pelo menos 1000
function Wait-UntilBalanceAtLeast {
  param([string]$ApiBase,[string]$Date,[int]$MinCents,[int]$TimeoutSec=30)
  $deadline = (Get-Date).AddSeconds($TimeoutSec)
  do {
    try {
      $res = Invoke-RestMethod -Method Get -Uri "$ApiBase/balances/daily?date=$Date" -TimeoutSec 5 -ErrorAction Stop
      $balance = if ($res.balanceCents -is [int]) { $res.balanceCents } else { [int]($res | Select-Object -ExpandProperty balanceCents) }
      if ($balance -ge $MinCents) { return $true }
    } catch { }
    Start-Sleep -Seconds 1
  } while ((Get-Date) -lt $deadline)
  return $false
}
if(-not (Wait-UntilBalanceAtLeast -ApiBase $Api -Date $day -MinCents 1000 -TimeoutSec 30)) { throw 'Daily balance did not reach expected value within timeout' }

Write-Host "`n== Replay mesma chave (200 + mesma Location) ==" -ForegroundColor Cyan
$r2 = Invoke-HttpStep -Method POST -Url "$Api/ledger/entries" -Headers @{ 'X-API-Key'=$ApiKey; 'Idempotency-Key'=$idem } -Body $body -Label 'ledger-replay'
$r2.StatusCode; if ($r2.StatusCode -ne 200) { throw "Esperado 200, obtido $($r2.StatusCode)" }
if ($r2.Headers.Location -ne $loc) { throw "Location diferente no replay" }
if($Verbose){ $r2.RawContent }

Write-Host "`n== GET /balances/daily ==" -ForegroundColor Cyan
$rDaily = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$day" -Label 'balances-daily'
$respDaily = $null; try { $respDaily = $rDaily.Content | ConvertFrom-Json } catch { $respDaily = $null }
if(-not $respDaily.balanceCents -or $respDaily.balanceCents -le 0){ throw "Saldo diário inválido: $($respDaily | ConvertTo-Json -Compress)" }
if($Verbose){ $respDaily | ConvertTo-Json -Compress }

Write-Host "`n== Rebuild replace-only (D..D) ==" -ForegroundColor Cyan
$h = @{ 'X-API-Key'=$ApiKey; 'X-Request-Id'=[guid]::NewGuid().Guid }
$rJob = Invoke-HttpStep -Method POST -Url "$Api/consolidator/rebuild?from=$day&to=$day" -Headers $h -Label 'rebuild-start-today'
$job = $null; try { $job = $rJob.Content | ConvertFrom-Json } catch { $job = $null }
$jid = $job.jobId

$deadline = (Get-Date).AddSeconds(60)
do {
  $st = Invoke-RestMethod -Method Get "$Api/consolidator/rebuild/status/$jid"
  Write-Host "status =" $st.status
  if ($st.status -in @('COMPLETED','DONE')) { break }
  if ($st.status -eq 'FAILED') { throw "Rebuild FAILED" }
  Start-Sleep -Seconds 1
} while ((Get-Date) -lt $deadline)

Write-Host "`n== GET /balances/daily após rebuild (deve permanecer 1000) ==" -ForegroundColor Cyan
$rAfter = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$day" -Label 'balances-daily-after-rebuild'
$after = $null; try { $after = $rAfter.Content | ConvertFrom-Json } catch { $after = $null }
if($Verbose){ $after | ConvertTo-Json -Compress }
if($after.balanceCents -ne $respDaily.balanceCents){ throw "Saldo mudou após rebuild ($($respDaily.balanceCents) -> $($after.balanceCents))" }

# -------- Scenario A: BASIC DEBIT --------
Write-Host "`n== Scenario: BASIC DEBIT (same-day) ==" -ForegroundColor Cyan
$todayStart = $after.balanceCents
$amt = 700
$idemA = [guid]::NewGuid().Guid
$bodyA = @{ occurredOn=$day; type='DEBIT'; amountCents=$amt; description='debit-smoke' } | ConvertTo-Json
$ra1 = Invoke-HttpStep -Method POST -Url "$Api/ledger/entries" -Headers @{ 'X-API-Key'=$ApiKey; 'Idempotency-Key'=$idemA } -Body $bodyA -Label 'debit-create'
Write-Host "status=$($ra1.StatusCode) location=$($ra1.Headers.Location)"; if($ra1.StatusCode -ne 201){ throw 'Scenario A: expected 201' }
$ra2 = Invoke-HttpStep -Method POST -Url "$Api/ledger/entries" -Headers @{ 'X-API-Key'=$ApiKey; 'Idempotency-Key'=$idemA } -Body $bodyA -Label 'debit-replay'
Write-Host "replay status=$($ra2.StatusCode) location=$($ra2.Headers.Location)"; if($ra2.StatusCode -ne 200 -or $ra2.Headers.Location -ne $ra1.Headers.Location){ throw 'Scenario A: replay 200/location' }
$expA = $todayStart - $amt
if(-not (Wait-UntilBalanceAtLeast -ApiBase $Api -Date $day -MinCents $expA -TimeoutSec 30)){ throw 'Scenario A: expected balance not reached' }
$rTodayAfterA = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$day" -Label 'scenario-A-daily-after'
$todayAfterAObj = $null; try { $todayAfterAObj = $rTodayAfterA.Content | ConvertFrom-Json } catch { $todayAfterAObj = $null }
$todayAfterA = $todayAfterAObj.balanceCents
if($todayAfterA -ne $expA){ throw "Scenario A: expected $expA got $todayAfterA" }
Write-Host 'PASS scenario A'

# -------- Scenario B: MIXED same-day --------
Write-Host "`n== Scenario: MIXED same-day (+500,+400,-200) ==" -ForegroundColor Cyan
$baseB = $todayAfterA
$null = New-Item -ItemType Directory -Force -Path out | Out-Null
function Post-One([string]$t,[int]$v,[string]$d,[string]$tag){
  $ib=[guid]::NewGuid().Guid; $b=@{occurredOn=$day;type=$t;amountCents=$v;description=$d}|ConvertTo-Json
  $label = "scenario-"+$tag
  $resp = Invoke-HttpStep -Method POST -Url "$Api/ledger/entries" -Headers @{ 'X-API-Key'=$ApiKey; 'Idempotency-Key'=$ib } -Body $b -Label $label
  return $resp
}
Post-One 'CREDIT' 500 'mix1' 'B-1' | Out-Null; Post-One 'CREDIT' 400 'mix2' 'B-2' | Out-Null; Post-One 'DEBIT' 200 'mix3' 'B-3' | Out-Null
$expB = $baseB + 700
if(-not (Wait-UntilBalanceAtLeast -ApiBase $Api -Date $day -MinCents $expB -TimeoutSec 30)){ throw 'Scenario B: expected balance not reached' }
$rCurB = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$day" -Label 'scenario-B-daily-after'
$curBObj = $null; try { $curBObj = $rCurB.Content | ConvertFrom-Json } catch { $curBObj = $null }
$curB = $curBObj.balanceCents
if($curB -ne $expB){ throw "Scenario B: expected $expB got $curB" }
Write-Host 'PASS scenario B'

# -------- Scenario C: MULTI-DAY --------
Write-Host "`n== Scenario: MULTI-DAY (yesterday + today) ==" -ForegroundColor Cyan
$yday = (Get-Date).AddDays(-1).ToString('yyyy-MM-dd')
$rY0 = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$yday" -Label 'scenario-C-yday-before'
$y0Obj = $null; try { $y0Obj = $rY0.Content | ConvertFrom-Json } catch { $y0Obj = $null }
$y0 = $y0Obj.balanceCents
$rT0 = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$day" -Label 'scenario-C-today-before'
$t0Obj = $null; try { $t0Obj = $rT0.Content | ConvertFrom-Json } catch { $t0Obj = $null }
$t0 = $t0Obj.balanceCents
Post-One 'CREDIT' 300 'ycredit'
$idemTd = [guid]::NewGuid().Guid; $bd=@{occurredOn=$day;type='DEBIT';amountCents=100;description='tdebit'}|ConvertTo-Json
Invoke-HttpStep -Method POST -Url "$Api/ledger/entries" -Headers @{ 'X-API-Key'=$ApiKey; 'Idempotency-Key'=$idemTd } -Body $bd -Label 'multi-day-today-debit' | Out-Null
if(-not (Wait-UntilBalanceAtLeast -ApiBase $Api -Date $yday -MinCents ($y0+300) -TimeoutSec 30)){ throw 'Scenario C: yesterday not reached' }
if(-not (Wait-UntilBalanceAtLeast -ApiBase $Api -Date $day -MinCents ($t0-100) -TimeoutSec 30)){ throw 'Scenario C: today not reached' }
$rYNow = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$yday" -Label 'scenario-C-yday-after'
$yNowObj = $null; try { $yNowObj = $rYNow.Content | ConvertFrom-Json } catch { $yNowObj = $null }
$yNow = $yNowObj.balanceCents
$rTNow = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$day" -Label 'scenario-C-today-after'
$tNowObj = $null; try { $tNowObj = $rTNow.Content | ConvertFrom-Json } catch { $tNowObj = $null }
$tNow = $tNowObj.balanceCents
if($yNow -ne ($y0+300) -or $tNow -ne ($t0-100)){ throw "Scenario C: expected Y=$($y0+300) T=$($t0-100) got Y=$yNow T=$tNow" }
Write-Host 'PASS scenario C'

# -------- Scenario D: REBUILD invariance --------
Write-Host "`n== Scenario: REBUILD invariance (yesterday..today) ==" -ForegroundColor Cyan
$preY=$yNow; $preT=$tNow
$rjob2 = Invoke-HttpStep -Method POST -Url "$Api/consolidator/rebuild?from=$yday&to=$day" -Headers @{ 'X-API-Key'=$ApiKey; 'X-Request-Id'=[guid]::NewGuid().Guid } -Label 'rebuild-start-yday-today'
$job2 = $null; try { $job2 = $rjob2.Content | ConvertFrom-Json } catch { $job2 = $null }
$jid2 = $job2.jobId
if(-not $jid2){ throw 'Scenario D: no jobId' }
for($i=0;$i -lt 60;$i++){ $st2=(Invoke-RestMethod "$Api/consolidator/rebuild/status/$jid2").status; if($st2 -in @('COMPLETED','DONE')){ break }; Start-Sleep 1 }
$rYAfter = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$yday" -Label 'scenario-D-yday-after'
$yAfterObj = $null; try { $yAfterObj = $rYAfter.Content | ConvertFrom-Json } catch { $yAfterObj = $null }
$yAfter = $yAfterObj.balanceCents
$rTAfter = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$day" -Label 'scenario-D-today-after'
$tAfterObj = $null; try { $tAfterObj = $rTAfter.Content | ConvertFrom-Json } catch { $tAfterObj = $null }
$tAfter = $tAfterObj.balanceCents
if($yAfter -ne $preY -or $tAfter -ne $preT){ throw 'Scenario D: balances changed after rebuild' }
Write-Host 'PASS scenario D'

# -------- Scenario E: Security 403 --------
Write-Host "`n== Scenario: SECURITY 403 (missing API key) ==" -ForegroundColor Cyan
$r403 = Invoke-HttpStep -Method POST -Url "$Api/ledger/entries" -Body $body -Label 'security-403'
if([int]$r403.StatusCode -ne 403){ throw "Scenario E: expected 403 got $($r403.StatusCode)" }
Write-Host 'PASS scenario E'

# -------- Scenario F: Validation 400 --------
Write-Host "`n== Scenario: VALIDATION 400 (bad payload) ==" -ForegroundColor Cyan
$bad = @{ occurredOn=$day; type='X'; amountCents=100 } | ConvertTo-Json
$r400 = Invoke-HttpStep -Method POST -Url "$Api/ledger/entries" -Headers @{ 'X-API-Key'=$ApiKey; 'Idempotency-Key'=[guid]::NewGuid().Guid } -Body $bad -Label 'validation-400'
if([int]$r400.StatusCode -ne 400){ throw "Scenario F: expected 400 got $($r400.StatusCode)" }
Write-Host 'PASS scenario F'

# -------- Scenario G: Multiple DEBITs same-day --------
Write-Host "`n== Scenario: MULTIPLE DEBITS (same-day) ==" -ForegroundColor Cyan
$rBaseG = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$day" -Label 'scenario-G-daily-before'
$baseGObj = $null; try { $baseGObj = $rBaseG.Content | ConvertFrom-Json } catch { $baseGObj = $null }
$baseG = $baseGObj.balanceCents
Post-One 'DEBIT' 300 'gdebit1' 'G-1' | Out-Null
Post-One 'DEBIT' 200 'gdebit2' 'G-2' | Out-Null
$expG = $baseG - 500
if(-not (Wait-UntilBalanceAtLeast -ApiBase $Api -Date $day -MinCents $expG -TimeoutSec 30)){ throw 'Scenario G: expected balance not reached' }
$rCurG = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$day" -Label 'scenario-G-daily-after'
$curGObj = $null; try { $curGObj = $rCurG.Content | ConvertFrom-Json } catch { $curGObj = $null }
$curG = $curGObj.balanceCents
if($curG -ne $expG){ throw "Scenario G: expected $expG got $curG" }
Write-Host 'PASS scenario G'

# -------- Scenario H: Mixed reordered (+500,-200,+400) --------
Write-Host "`n== Scenario: MIX REORDERED (+500,-200,+400) ==" -ForegroundColor Cyan
$rBaseH = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$day" -Label 'scenario-H-daily-before'
$baseHObj = $null; try { $baseHObj = $rBaseH.Content | ConvertFrom-Json } catch { $baseHObj = $null }
$baseH = $baseHObj.balanceCents
Post-One 'CREDIT' 500 'hcredit1' 'H-1' | Out-Null
Post-One 'DEBIT' 200 'hdebit' 'H-2' | Out-Null
Post-One 'CREDIT' 400 'hcredit2' 'H-3' | Out-Null
$expH = $baseH + 700
if(-not (Wait-UntilBalanceAtLeast -ApiBase $Api -Date $day -MinCents $expH -TimeoutSec 30)){ throw 'Scenario H: expected balance not reached' }
$rCurH = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$day" -Label 'scenario-H-daily-after'
$curHObj = $null; try { $curHObj = $rCurH.Content | ConvertFrom-Json } catch { $curHObj = $null }
$curH = $curHObj.balanceCents
if($curH -ne $expH){ throw "Scenario H: expected $expH got $curH" }
Write-Host 'PASS scenario H'

# -------- Scenario I: Multi-day rebuild invariance --------
Write-Host "`n== Scenario: REBUILD invariance (multi-day) ==" -ForegroundColor Cyan
$rPreYi = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$yday" -Label 'scenario-I-yday-before'
$preYiObj = $null; try { $preYiObj = $rPreYi.Content | ConvertFrom-Json } catch { $preYiObj = $null }
$preYi = $preYiObj.balanceCents
$rPreTi = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$day" -Label 'scenario-I-today-before'
$preTiObj = $null; try { $preTiObj = $rPreTi.Content | ConvertFrom-Json } catch { $preTiObj = $null }
$preTi = $preTiObj.balanceCents
$rji = Invoke-HttpStep -Method POST -Url "$Api/consolidator/rebuild?from=$yday&to=$day" -Headers @{ 'X-API-Key'=$ApiKey; 'X-Request-Id'=[guid]::NewGuid().Guid } -Label 'rebuild-start-yday-to-today'
$ji = $null; try { $ji = $rji.Content | ConvertFrom-Json } catch { $ji = $null }
$jidI = $ji.jobId
for($i=0;$i -lt 60;$i++){ $sti=(Invoke-RestMethod "$Api/consolidator/rebuild/status/$jidI").status; if($sti -in @('COMPLETED','DONE')){ break }; Start-Sleep 1 }
$rYAfterI = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$yday" -Label 'scenario-I-yday-after'
$yAfterIObj = $null; try { $yAfterIObj = $rYAfterI.Content | ConvertFrom-Json } catch { $yAfterIObj = $null }
$yAfterI = $yAfterIObj.balanceCents
$rTAfterI = Invoke-HttpStep -Method GET -Url "$Api/balances/daily?date=$day" -Label 'scenario-I-today-after'
$tAfterIObj = $null; try { $tAfterIObj = $rTAfterI.Content | ConvertFrom-Json } catch { $tAfterIObj = $null }
$tAfterI = $tAfterIObj.balanceCents
if($yAfterI -ne $preYi -or $tAfterI -ne $preTi){ throw 'Scenario I: balances changed after rebuild' }
Write-Host 'PASS scenario I'

if($Load){
  Write-Host "`n== Micro-load (50 rps, 15s, loss<=5%) ==" -ForegroundColor Cyan
  pwsh -File ops/load.ps1 -Rps 50 -Duration '15s' -MaxLoss 0.05 -Date $day
}

Write-Host "`nOK: testes concluídos" -ForegroundColor Green
Write-Host "`n== SUMMARY ==" -ForegroundColor Green
$Global:Steps |
  Select-Object ts,label,method,url,status,location |
  Format-Table -AutoSize

# Persist summary for CI / local inspection
$Global:Steps | ConvertTo-Json -Depth 15 | Out-File (Join-Path $Global:OutDir 'summary.json') -Encoding utf8
$Global:Steps | Export-Csv -NoTypeInformation (Join-Path $Global:OutDir 'summary.csv')

# Mostra resumo e entra no menu interativo — sem teardown
Print-RunSummary "Done"

while ($true) {
  Write-Host ""
  Write-Host "What would you like to do next?" -ForegroundColor Cyan
  Write-Host "[1] Deposit"
  Write-Host "[2] Withdraw"
  Write-Host "[3] View balance"
  Write-Host "[Q] Quit (Ctrl+C)  script will not auto-teardown"
  $choice = Read-Host "Choose an option"

  switch ($choice.Trim().ToUpperInvariant()) {
    '1' {
      $amount = Read-Host "Enter deposit amount in BRL (e.g., 100.50)"
      if ($amount -match '^[0-9]+([\.,][0-9]{1,2})?$') {
        $amount = $amount -replace ',', '.'
        $cents = [int][math]::Round([decimal]$amount * 100)
        $resp = Submit-Entry -Type CREDIT -AmountCents $cents -OnDate (Get-Date -Format 'yyyy-MM-dd') -Description "interactive deposit" -Label "deposit-create"
        if ([int]$resp.StatusCode -in 200,201) { $RunTotals.DepositedCents += $cents }
        Show-DailyBalance -Date (Get-Date -Format 'yyyy-MM-dd') -Label "balance-after-deposit" | Out-Null
        Print-RunSummary "Deposit recorded"
      } else {
        Write-Warning "Invalid amount."
      }
    }
    '2' {
      $amount = Read-Host "Enter withdrawal amount in BRL (e.g., 20.00)"
      if ($amount -match '^[0-9]+([\.,][0-9]{1,2})?$') {
        $amount = $amount -replace ',', '.'
        $cents = [int][math]::Round([decimal]$amount * 100)
        $resp = Submit-Entry -Type DEBIT -AmountCents $cents -OnDate (Get-Date -Format 'yyyy-MM-dd') -Description "interactive withdraw" -Label "withdraw-create"
        if ([int]$resp.StatusCode -in 200,201) { $RunTotals.WithdrawnCents += $cents }
        Show-DailyBalance -Date (Get-Date -Format 'yyyy-MM-dd') -Label "balance-after-withdraw" | Out-Null
        Print-RunSummary "Withdrawal recorded"
      } else {
        Write-Warning "Invalid amount."
      }
    }
    '3' {
      $date = Read-Host "Balance for which date? (YYYY-MM-DD, leave empty for today)"
      if (-not $date) { $date = (Get-Date -Format 'yyyy-MM-dd') }
      Show-DailyBalance -Date $date -Label "balance-check" | Out-Null
      Print-RunSummary "Balance checked"
    }
    'Q' {
      Write-Host "Ok. Press Ctrl+C to terminate if you really want to exit. Continuing menu…" -ForegroundColor Yellow
      # Intencionalmente não encerramos nem derrubamos containers
    }
    Default {
      Write-Warning "Unknown option."
    }
  }
}
