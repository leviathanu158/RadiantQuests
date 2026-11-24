# Radiant Quests quick feature test: start the test save, wait for the mod, open Squin's clerk, run only the given test-channel commands,
# print their answers and the mod's errors, then quit. For checking one new feature; scenario.ps1 is the full run.
#   .\quick.ps1 "report" "lairs"
#   .\quick.ps1 -KeepOpen "report"
#   .\quick.ps1 -NoStart "report"     (a game already started by this script or scenario.ps1)
[CmdletBinding(PositionalBinding = $false)]
param([string]$Save = "rqtest", [switch]$NoStart, [switch]$KeepOpen, [double]$WaitFirst = 0, [Parameter(ValueFromRemainingArguments = $true)][string[]]$Commands)
$ErrorActionPreference = "Continue"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$D = "C:\Kenshi_Meitou\ctl"
$launcher = Get-Content (Join-Path $env:LOCALAPPDATA "Meitou\launcher.json") -Raw | ConvertFrom-Json
$version = Get-ChildItem (Join-Path $launcher.InstallFolder "versions") -Directory | Sort-Object Name -Descending | Select-Object -First 1
$log = Join-Path $version.FullName "log.txt"
function Agent([string]$cmd, [double]$timeout = 15) { (& "$here\agent.ps1" -Timeout $timeout $cmd.Split(' ')) -join '' }
function Rq([string[]]$lines) { & "$here\rq.ps1" -Timeout 15 @lines }
function Session { $c = Get-Content $log; $i = ($c | Select-String -Pattern '^==== ' | Select-Object -Last 1).LineNumber; $c[($i - 1)..($c.Count - 1)] }
function Run([double]$seconds) { Agent "resume" | Out-Null; Start-Sleep -Milliseconds ([int]($seconds * 1000)); Agent "pause" | Out-Null }

$sessionsBefore = if (-not (Test-Path $log)) { 0 } else { @(Select-String -Path $log -Pattern '^==== ' -ErrorAction SilentlyContinue).Count }
if (-not $NoStart) {
    if (Get-Process MeitouClient -ErrorAction SilentlyContinue) { Write-Host "A Meitou game is running: close it first. Nothing started."; exit 2 }
    Remove-Item "$D\cmd", "$D\out", "$D\rq.cmd", "$D\rq.out" -ErrorAction SilentlyContinue
    $env:KENSHI_PATH = $launcher.KenshiPath
    $env:RADIANTQUESTS_TEST_DIR = $D
    Start-Process -FilePath (Join-Path $version.FullName "MeitouClient.exe") -WorkingDirectory (Join-Path $env:LOCALAPPDATA "Meitou") `
        -ArgumentList @("--windowed", "--load", "`"$(Join-Path $env:LOCALAPPDATA "Meitou\save\$Save")`"", "--control-dir", "`"$D`"") | Out-Null
    Write-Host "started $($version.Name) with save $Save"
}
$t0 = Get-Date
do {
    Start-Sleep 5
    $fresh = $NoStart -or @(Select-String -Path $log -Pattern '^==== ').Count -gt $sessionsBefore
    $s = if ($fresh) { Session } else { @() }
} while (-not ($s -match 'radiantquests: world ready|RadiantQuests: not loaded') -and ((Get-Date) - $t0).TotalSeconds -lt 600)
$s -match 'radiantquests: (catalog|ready|world ready|paused)' | ForEach-Object { $_ -replace '^.*radiantquests: ', 'mod: ' }
if (-not ($s -match 'radiantquests: world ready')) { Write-Host "MOD DID NOT START"; $s -match 'radiantquests|RadiantQuests' | Select-Object -Last 10; exit 1 }
# The world finishes loading (agent "loaded" event) well after the mod's "world ready" line.
$t0 = Get-Date
while (-not ((Test-Path "$D\out") -and (Select-String -Path "$D\out" -Pattern '"event":"loaded"' -Quiet)) -and ((Get-Date) - $t0).TotalSeconds -lt 600) { Start-Sleep 3 }
Agent "pause" | Out-Null

$t0 = Get-Date
# Any loaded clerk will do (the save may be anywhere: pick-save.ps1 chooses it by mod list); Squin's when it is there.
do { Run 3; $clerks = (Rq @("clerks")) -join '' } while ($clerks -notmatch '\d+: ' -and ((Get-Date) - $t0).TotalSeconds -lt 120)
$idx = if ($clerks -match '(\d+)[:=\s]+[^|]*Squin') { $Matches[1] } else { "0" }
if ($WaitFirst -gt 0) { Run $WaitFirst }
Rq @("open $idx")
if ($Commands) { Rq $Commands }

"--- mod errors and turned-off areas this session:"
(Session) -match 'radiantquests.*(failed|turned OFF|skipped|error)' | Select-Object -Last 10
if (-not $KeepOpen) { Agent "quit" | Out-Null; $t0 = Get-Date; while ((Get-Process MeitouClient -ErrorAction SilentlyContinue) -and ((Get-Date) - $t0).TotalSeconds -lt 60) { Start-Sleep 2 } }
