# ============== Update-FancontrolFixtures.ps1 — R2: producer-genererede kontrakt-fixtures ==============
# Hoester de LEVENDE Fancontrol-kontrakter ind som test-fixtures, saa Optimizer.WinUI.Tests binder
# PRODUCENTEN og ikke en haandkopieret streng: omdoeber engine'en et felt (cool->coolant), goer en
# fixture-regenerering testene roede i stedet for at de forbliver groenne mod en foraeldet kopi.
#
# Brug:  powershell -File tools\Update-FancontrolFixtures.ps1   (fra L:\Projects)
# Kraever: Fancontrol-systemet KOERER og er SUNDT (gate herunder afviser ellers) - fixtures skal
# fange den raske form, ellers udvandes testenes asserts (fx sentinel pass=true).
param(
  [string]$FancontrolRoot = 'L:\Users\Fancontrol',
  [string]$FixtureDir = "$PSScriptRoot\..\Optimizer.WinUI.Tests\Fixtures\fancontrol"
)
$ErrorActionPreference = 'Stop'
$state = "$FancontrolRoot\state"

# ---- sundheds-gate: capture kun en rask maskine ----
$brain = [System.IO.File]::ReadAllText("$state\brain_state.json") | ConvertFrom-Json
$age = ((Get-Date) - [datetime]$brain.ts).TotalSeconds
if ($age -gt 30) { throw "brain_state er $([int]$age) s gammel (FanBrain doed?) - fixtures skal fanges paa en levende maskine" }
if ($null -eq $brain.cool) { throw 'brain_state.cool er null (Corsair-tap nede?) - vent paa en komplet tick foer capture' }
$sent = [System.IO.File]::ReadAllText("$state\sentinel_state.json") | ConvertFrom-Json
if (-not $sent.pass) { throw "sentinel pass=false ($($sent.issues -join '; ')) - fix systemet foer fixtures fanges" }
$fgw = [System.IO.File]::ReadAllText("$state\fgwatch_state.json") | ConvertFrom-Json
if ($fgw.stopped) { throw 'fgwatch er stoppet - start daemonen foer capture' }

New-Item -ItemType Directory -Force $FixtureDir | Out-Null

# ---- de tre state-kontrakter (verbatim) ----
foreach ($f in 'brain_state.json','fgwatch_state.json','sentinel_state.json') {
  [System.IO.File]::Copy("$state\$f", "$FixtureDir\$f", $true)
  "fixture: $f"
}

# ---- en brain-telemetrilinje (dagsarkivet indeholder OGSAA sentinel-linjer - tag en med "mode") ----
$tele = "$state\telemetry\{0}.jsonl" -f (Get-Date).ToString('yyyy-MM-dd')
$line = Get-Content $tele -Tail 50 | Where-Object { $_ -match '"mode":' } | Select-Object -Last 1
if (-not $line) { throw "ingen brain-linje fundet i $tele" }
[System.IO.File]::WriteAllText("$FixtureDir\telemetry_line.jsonl", $line)
'fixture: telemetry_line.jsonl'

# ---- R1-resultatkontrakten, begge former, fanget fra den AEGTE ctl.ps1 ----
$ctl = "$FancontrolRoot\engine\ctl.ps1"
$okLine   = (& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ctl -Command status | Select-Object -Last 1)
$failLine = (& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ctl -Command run-task -Args FixtureProbe | Select-Object -Last 1)
if ($okLine -notmatch '^\{"ok":true')  { throw "uventet ctl-ok-form: $okLine" }
if ($failLine -notmatch '^\{"ok":false') { throw "uventet ctl-fail-form: $failLine" }
[System.IO.File]::WriteAllText("$FixtureDir\ctl_result_ok.json", $okLine)
[System.IO.File]::WriteAllText("$FixtureDir\ctl_result_fail.json", $failLine)
'fixture: ctl_result_ok.json + ctl_result_fail.json'

"FAERDIG: $((Get-ChildItem $FixtureDir).Count) fixtures i $FixtureDir (koer 'dotnet test' for at binde dem)"
