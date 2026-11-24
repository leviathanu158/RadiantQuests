# Radiant Quests scenario test. Starts the test save, runs every check in order, prints PASS/FAIL lines and writes them to test\results.txt,
# with screenshots in test\shots. Needs: RadiantQuests ticked, Manual Combat unticked, the "rqtest" save (characters in Squin's bar).
#   .\scenario.ps1               start the game, run, quit the game
#   .\scenario.ps1 -NoStart      run against a game already started with this script's settings (control dir, test env var)
#   .\scenario.ps1 -KeepOpen     leave the game running afterwards
param([string]$Save = "rqtest", [switch]$NoStart, [switch]$KeepOpen)
$ErrorActionPreference = "Continue"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$D = "C:\Kenshi_Meitou\ctl"
$launcher = Get-Content (Join-Path $env:LOCALAPPDATA "Meitou\launcher.json") -Raw | ConvertFrom-Json
$version = Get-ChildItem (Join-Path $launcher.InstallFolder "versions") -Directory | Sort-Object Name -Descending | Select-Object -First 1
$log = Join-Path $version.FullName "log.txt"
$shots = Join-Path $here "shots"; New-Item -ItemType Directory -Force $shots | Out-Null
$results = [System.Collections.Generic.List[string]]::new()

function Agent([string]$cmd, [double]$timeout = 15) { (& "$here\agent.ps1" -Timeout $timeout $cmd.Split(' ')) -join '' }
function Rq([string[]]$lines) { & "$here\rq.ps1" -Timeout 15 @lines }
function Result([string]$line) { ($line -split ' => ', 2)[1] }
function Check([string]$name, [bool]$ok, [string]$detail = "") {
    $line = "{0}  {1}{2}" -f ($(if ($ok) { "PASS" } else { "FAIL" })), $name, $(if ($detail) { "  ($detail)" } else { "" })
    $results.Add($line); Write-Host $line -ForegroundColor $(if ($ok) { "Green" } else { "Red" })
}
function Session { $c = Get-Content $log; $i = ($c | Select-String -Pattern '^==== ' | Select-Object -Last 1).LineNumber; $c[($i - 1)..($c.Count - 1)] }
function Report { Result (Rq @("report")) }
# The report's General board (before "; faction board:") and Faction board (after it).
function GeneralPart([string]$r) { ($r -split '; faction board: ', 2)[0] }
function FactionPart([string]$r) { $parts = $r -split '; faction board: ', 2; if ($parts.Count -gt 1) { $parts[1] } else { "" } }
function Rep { if ((Report) -match 'lifetime (\d+) balance (\d+)') { [int]$Matches[1] } else { -1 } }
function Money { if ((Agent "observe --brief") -match '"money":(\d+)') { [long]$Matches[1] } else { -1 } }
# The agent channel splits words at spaces, so screenshots go to the control folder first and are copied into test\shots.
function Shot([string]$name) { $tmp = Join-Path $D "rq-$name.png"; Agent "screenshot $tmp" | Out-Null; Start-Sleep 1; if (Test-Path $tmp) { Move-Item $tmp (Join-Path $shots "$name.png") -Force } }
function Run([double]$seconds) { Agent "resume" | Out-Null; Start-Sleep -Milliseconds ([int]($seconds * 1000)); Agent "pause" | Out-Null }

# 1. Start
$sessionsBefore = @(Select-String -Path $log -Pattern '^==== ' -ErrorAction SilentlyContinue).Count
if (-not $NoStart) {
    # Never a second copy: a game still running (yours, or a test one still closing) must be closed first.
    if (Get-Process MeitouClient -ErrorAction SilentlyContinue) { Write-Host "A Meitou game is running: close it first. Nothing started." -ForegroundColor Red; exit 2 }
    Remove-Item "$D\cmd", "$D\out", "$D\rq.cmd", "$D\rq.out" -ErrorAction SilentlyContinue
    $env:KENSHI_PATH = $launcher.KenshiPath
    $env:RADIANTQUESTS_TEST_DIR = $D
    Start-Process -FilePath (Join-Path $version.FullName "MeitouClient.exe") -WorkingDirectory (Join-Path $env:LOCALAPPDATA "Meitou") `
        -ArgumentList @("--windowed", "--load", "`"$(Join-Path $env:LOCALAPPDATA "Meitou\save\$Save")`"", "--control-dir", "`"$D`"") | Out-Null
    Write-Host "started $($version.Name) with save $Save; loading..."
}
$t0 = Get-Date
do {
    Start-Sleep 5
    $fresh = $NoStart -or @(Select-String -Path $log -Pattern '^==== ').Count -gt $sessionsBefore
    $s = if ($fresh) { Session } else { @() }
} while (-not ($s -match 'radiantquests: world ready|RadiantQuests: not loaded') -and ((Get-Date) - $t0).TotalSeconds -lt 600)
Check "mod loads" ([bool]($s -match 'radiantquests: world ready')) (($s -match 'radiantquests: catalog: \d+') -join '' -replace 'mod\s+radiantquests: ', '')
if (-not ($s -match 'radiantquests: world ready')) { $results | Set-Content (Join-Path $here "results.txt"); exit 1 }
Agent "pause" | Out-Null
Check "paused-commands hook" ([bool]($s -match 'paused commands: on'))

# 2. Clerk and board
$t0 = Get-Date
do { Run 3; $clerks = Result (Rq @("clerks")) } while ($clerks -notmatch 'Squin' -and ((Get-Date) - $t0).TotalSeconds -lt 120)
Check "clerk found in Squin" ($clerks -match 'Squin') $clerks
$idx = if ($clerks -match '(\d+): [^|]*Squin') { $Matches[1] } else { "0" }
# Rank D first (500 rep), so the D quests the real-fight check needs (Dust Bandits) are not locked.
Rq @("rep 600") | Out-Null
Start-Sleep 1
Rq @("open $idx") | Out-Null
Start-Sleep 1
$board = GeneralPart (Report)
$offers = [regex]::Matches($board, '\[(\d+)\] (\w+) ([^|]+?) (\d+)c (\d+)r( taken)?( locked)?')
$atE = @($offers | Where-Object { $_.Groups[2].Value -eq 'E' -and $_.Groups[3].Value -notlike 'Wipe out*' })
$dd = @($offers | Where-Object { $_.Groups[2].Value -eq 'D' -and $_.Groups[3].Value -notlike 'Wipe out*' })
Check "board made while paused, E and D each have 2-5 quests" ($atE.Count -ge 2 -and $atE.Count -le 5 -and $dd.Count -ge 2 -and $dd.Count -le 5) "$($offers.Count) offers, $($atE.Count) at E, $($dd.Count) at D"
# His rule (2026-11-23): the same quest never twice - a target once per rank and quest kind (E and D of the same target is fine), each real
# group or camp once (different groups of the same enemy are fine).
$keys = @($offers | Where-Object { $_.Groups[3].Value -notlike 'Wipe out*' -and $_.Groups[3].Value -notlike 'Clear *' } |
    ForEach-Object { $_.Groups[2].Value + ' ' + ($_.Groups[3].Value -replace ' \d+ ', ' ') })
Check "no quest twice on the board (target per rank and kind)" (($keys | Select-Object -Unique).Count -eq $keys.Count) ($keys -join ', ')
Shot "guild-window"

# 3. Families
$fam = Result (Rq @("families"))
Check "variant factions grouped (Dust Raiders -> Dust Bandits)" ($fam -match 'Dust Raiders -> Dust Bandits') $fam

# 4. Accept a kill quest and a hunt quest (while paused)
$kill = $offers | Where-Object { $_.Groups[3].Value -like 'Defeat*Dust Bandits*' -and -not $_.Groups[6].Success -and -not $_.Groups[7].Success } | Select-Object -First 1
if (-not $kill) { $kill = $offers | Where-Object { $_.Groups[3].Value -like 'Defeat*' -and -not $_.Groups[6].Success -and -not $_.Groups[7].Success } | Select-Object -First 1 }
$hunt = $offers | Where-Object { $_.Groups[3].Value -like 'Hunt*' -and -not $_.Groups[6].Success -and -not $_.Groups[7].Success } | Select-Object -First 1
$accept = @(); if ($kill) { $accept += "accept $($kill.Groups[1].Value)" }; if ($hunt) { $accept += "accept $($hunt.Groups[1].Value)" }
Rq $accept | Out-Null
Start-Sleep 1
$quests = Result (Rq @("quests"))
$ids = @([regex]::Matches($quests, '#(\d+)') | ForEach-Object { $_.Groups[1].Value })
Check "accepted a kill and a hunt quest while paused" ($ids.Count -eq $accept.Count -and $ids.Count -ge 1) $quests

# 5. Target groups: found or spawned, within range, marked
Run 6
$targets = Result (Rq @("targets"))
$withGroups = [regex]::Matches($targets, ': (\d+) groups') | Where-Object { [int]$_.Groups[1].Value -gt 0 }
Check "every quest has target groups" (@($withGroups).Count -eq $ids.Count) $targets
$spawned = Session | Select-String 'radiantquests: quest #\d+: (none in range, spawned|none in range and)' | ForEach-Object { $_.Line -replace 'mod\s+radiantquests: ', '' }
Check "spawn when none in range (info)" $true $(if ($spawned) { $spawned -join '; ' } else { "groups already in range, nothing spawned" })
Rq @("close") | Out-Null
Agent "ui-click --id hud/ShortcutMapButton" | Out-Null
Start-Sleep 2
Shot "map"
Agent "ui-click --id hud/ShortcutMapButton" | Out-Null

# 5b. A real defeat: Su knocks out a spawned Dust Bandit away from town (guards stay out of it); the knockout counts for a Dust Bandits quest.
# Spawn a member of the accepted defeat quest's target (any faction), so this check runs whatever today's board offers.
$killId = if ($kill) { if ((Result (Rq @("quests"))) -match '#(\d+) Defeat') { $Matches[1] } else { "" } } else { "" }
$record = if ($killId) { Result (Rq @("member #$killId")) } else { "none" }
# Manual Combat takes over Su (she won't fight on the test's orders): the real fight needs it unticked.
$manualCombat = [bool](Get-Content (Join-Path $launcher.KenshiPath "data\mods.cfg") | Select-String -SimpleMatch 'ManualCombat.mod')
$dust = $record -and $record -ne "none" -and $record -notmatch 'no data' -and -not $manualCombat
if ($manualCombat) { Check "a knockout by your side counts as a defeat (skipped: Manual Combat is ticked, Su won't fight on orders)" $true }
if ($dust) {
    Rq @("close") | Out-Null
    $back = if ((Agent "observe --brief") -match '"pos":\[(-?[\d.]+),(-?[\d.]+)\]') { [double]$Matches[1], [double]$Matches[2] } else { 0, 0 }
    Agent "tp -66500 20500" 20 | Out-Null
    Run 3
    $spawn = Agent "spawn-at -66440 20500 $record --aggro" 20
    $bandit = if ($spawn -match '"id":"(c:[\d.]+)"') { $Matches[1] } else { "" }
    Agent "select c:0.0" | Out-Null
    if ($bandit) { Agent "order attack $bandit" | Out-Null }
    $t0 = Get-Date; $counted = $null
    do {
        Run 5
        $counted = Session | Select-String 'radiantquests: message: Defeat \d+ [^:]+: (\d+/\d+|done)' | Select-Object -Last 1
    } while (-not $counted -and ((Get-Date) - $t0).TotalSeconds -lt 120)
    Check "a knockout by your side counts as a defeat" ([bool]$counted) $(if ($counted) { $counted.Line -replace 'mod\s+radiantquests: message: ', '' } else { "bandit $bandit; no defeat counted in 2 min" })
    Agent "tp $([int]$back[0]) $([int]$back[1])" 20 | Out-Null
    Run 3
    Rq @("open $idx") | Out-Null
} elseif (-not $manualCombat) {
    Check "a knockout by your side counts as a defeat (skipped: no spawnable member for today's defeat quest)" $true
}

# 5c. Group quest: a real group near town, tagged and followed; finishing it pays
$all = Report
$g = [regex]::Matches($all, '\[(\d+)\] (\w+) (Wipe out a group of (\d+) [^|]+?) (\d+)c (\d+)r(?! taken)(?! locked)') | Select-Object -First 1
$gf = $all -match '; faction board: .*' -and $g -and $all.IndexOf($g.Value) -gt $all.IndexOf('; faction board: ')
Check "group quests offered from real groups near town$(if (-not $g) { ' (info: none within the quest area today)' })" $true $(if ($g) { $g.Groups[3].Value } else { "" })
if ($g) {
    Rq @("$(if ($gf) { 'faccept' } else { 'accept' }) $($g.Groups[1].Value)") | Out-Null; Start-Sleep 1
    Run 6
    $gq = Result (Rq @("quests"))
    $gid = if ($gq -match '#(\d+) Wipe out') { $Matches[1] } else { "" }
    $gt = Result (Rq @("targets"))
    Check "group quest follows its own group on the map" ($gid -and $gt -match "#$gid [^|]*: 1 groups") $gt
    if ($gid) {
        Rq @("finish $gid") | Out-Null; Start-Sleep 1
        $rep0 = Rep
        Rq @("handin $gid") | Out-Null; Start-Sleep 1
        Check "group quest hands in and pays" ((Rep) -gt $rep0) "rep $rep0 -> $(Rep)"
    }
}

# 5d. Clear the lair: an existing camp/nest near town; finishing it pays
$all = Report
$lm = [regex]::Matches($all, '\[(\d+)\] (\w+) (Clear [^|]+?) (\d+)c (\d+)r(?! taken)(?! locked)') | Select-Object -First 1
Check "lair quests offered$(if (-not $lm) { ' (info: no lair within reach of this town today)' })" $true $(if ($lm) { $lm.Groups[3].Value } else { "" })
if ($lm) {
    $lf = $all.IndexOf('; faction board: ') -ge 0 -and $all.IndexOf($lm.Value) -gt $all.IndexOf('; faction board: ')
    Rq @("$(if ($lf) { 'faccept' } else { 'accept' }) $($lm.Groups[1].Value)") | Out-Null; Start-Sleep 1
    Run 6
    $lq = Result (Rq @("quests")); $lid = if ($lq -match '#(\d+) Clear') { $Matches[1] } else { "" }
    $lt = Result (Rq @("targets"))
    Check "lair quest is marked at its place" ($lid -and $lt -match "#$lid [^|]*: 1 groups") $lt
    if ($lid) {
        Rq @("finish $lid") | Out-Null; Start-Sleep 1
        $rep0 = Rep
        Rq @("handin $lid") | Out-Null; Start-Sleep 1
        Check "lair quest hands in and pays" ((Rep) -gt $rep0) "rep $rep0 -> $(Rep)"
    }
}

# 6. Finish, hand in (pays cats and rep)
Rq @("open $idx") | Out-Null
$money0 = Money; $rep0 = Rep
Rq @("finish $($ids[0])") | Out-Null; Start-Sleep 1
Check "quest done after its kills" ((Result (Rq @("quests"))) -match "#$($ids[0]) [^|]*DONE")
Rq @("handin $($ids[0])") | Out-Null; Start-Sleep 1
$money1 = Money; $rep1 = Rep
Check "hand-in pays cats and rep" ($money1 -gt $money0 -and $rep1 -gt $rep0) "money $money0 -> $money1, rep $rep0 -> $rep1"

# 7. Abandon costs rep
if ($ids.Count -ge 2) {
    $rep0 = Rep
    Rq @("abandon $($ids[1])") | Out-Null; Start-Sleep 1
    $rep1 = Rep
    Check "abandon removes the quest and costs rep" ($rep1 -le $rep0 -and (Result (Rq @("quests"))) -notmatch "#$($ids[1]) ") "rep $rep0 -> $rep1"
}

# 8. Running out of time
$board = GeneralPart (Report)
$next = [regex]::Matches($board, '\[(\d+)\] [ED] ([^|]+?) \d+c \d+r(?! taken)(?! locked)') | Select-Object -First 1
if ($next) {
    Rq @("accept $($next.Groups[1].Value)") | Out-Null; Start-Sleep 1
    $rep0 = Rep
    Rq @("skip 1000") | Out-Null
    Run 4
    $q = Result (Rq @("quests")); $rep1 = Rep
    Check "a quest out of time fails and costs rep" ($q -eq "no quests" -and $rep1 -le $rep0) "rep $rep0 -> $rep1; $q"
}

# 8b. Faction tab: a quest against the clerk's faction's enemies pays faction points and a little real relation
$frep = Report
$fboard = FactionPart $frep
# Offers on the Faction tab carry " +Nrel" (the relation they give: none at E and D, his rule).
$foffers = [regex]::Matches($fboard, '\[(\d+)\] (\w+) ([^|]+?) (\d+)c (\d+)r(?: \+(\d+)rel)?( taken)?( locked)?')
Check "faction board offers quests against the faction's enemies" ($foffers.Count -gt 0) $(if ($foffers.Count -gt 0) { $fboard } else { "report: $frep" })
$fo = $foffers | Where-Object { -not $_.Groups[7].Success -and -not $_.Groups[8].Success } | Select-Object -First 1
if ($fo) {
    $before = Result (Rq @("factions"))
    Rq @("faccept $($fo.Groups[1].Value)") | Out-Null; Start-Sleep 1
    $fq = Result (Rq @("quests"))
    $fid = if ($fq -match '#(\d+) ') { $Matches[1] } else { "" }
    Rq @("finish $fid") | Out-Null; Start-Sleep 1
    Rq @("handin $fid") | Out-Null; Start-Sleep 1
    $after = Result (Rq @("factions"))
    $faction = if ($clerks -match "$idx`: ([^/]+?) /") { $Matches[1].Trim() } else { "?" }
    $p0 = if ($before -match [regex]::Escape($faction) + ': rank \w+ (\d+) pts relation (-?[\d.]+)') { [int]$Matches[1], [double]$Matches[2] } else { 0, 0 }
    $p1 = if ($after -match [regex]::Escape($faction) + ': rank \w+ (\d+) pts relation (-?[\d.]+)') { [int]$Matches[1], [double]$Matches[2] } else { 0, 0 }
    Check "faction quest pays faction points" ($p1[0] -gt $p0[0]) "$faction points $($p0[0]) -> $($p1[0])"
    $rel = if ($fo.Groups[6].Success) { [int]$fo.Groups[6].Value } else { 0 }
    Check "faction quest changes relation as offered (E/D none, C+ 1-6)" ([math]::Abs(($p1[1] - $p0[1]) - $rel) -le 0.51) "$faction relation $($p0[1]) -> $($p1[1]), offer said +$rel ($($fo.Groups[2].Value) $($fo.Groups[3].Value))"
}
$released = Session | Select-String 'released its spawned group' | ForEach-Object { $_.Line -replace 'mod\s+radiantquests: ', '' }
Check "spawned groups released when their quest ends (info)" $true $(if ($released) { $released -join '; ' } else { "none released" })

# 9. Rank up
$rank0 = if ((Report) -match 'rank (\w+)') { $Matches[1] } else { "?" }
Rq @("rep 1200") | Out-Null; Start-Sleep 1
$rank1 = if ((Report) -match 'rank (\w+)') { $Matches[1] } else { "?" }
Check "rank goes up with lifetime rep" ($rank1 -ne $rank0) "$rank0 -> $rank1"

# 9a. Reroll: costs spendable rep and replaces the untaken offers
Rq @("open $idx") | Out-Null; Start-Sleep 1
$b0 = Report; $bal0 = if ($b0 -match 'balance (\d+)') { [int]$Matches[1] } else { -1 }
Rq @("reroll") | Out-Null; Start-Sleep 1
$b1 = Report; $bal1 = if ($b1 -match 'balance (\d+)') { [int]$Matches[1] } else { -1 }
Check "reroll costs rep and puts up new work" ($bal1 -lt $bal0 -and (GeneralPart $b1) -ne (GeneralPart $b0)) "balance $bal0 -> $bal1"

# 9b. A town whose bar another faction runs (The Hub: Trade Ninjas behind the bar) still has a clerk, working for the town's owner
Agent "tp The Hub" 20 | Out-Null
$t0 = Get-Date
do { Run 4; $hub = Result (Rq @("clerks")) } while ($hub -notmatch 'The Hub' -and ((Get-Date) - $t0).TotalSeconds -lt 120)
Check "a bar run by another faction still has a clerk (The Hub)" ($hub -match 'The Hub') $hub

# 10. Errors
$errors = Session | Select-String 'radiantquests: ' | Where-Object { $_.Line -notmatch 'radiantquests: (message|command|board|quest #|kill:)' -and $_.Line -cmatch 'failed|turned OFF|threw|Exception' } | ForEach-Object Line
Check "no mod errors in the log" (-not $errors) ($errors -join ' / ')

$results | Set-Content (Join-Path $here "results.txt")
Write-Host "screenshots: $shots"
if (-not $KeepOpen) {
    Agent "quit" | Out-Null
    # Wait until the game is really gone (it holds the mod's files while closing).
    $t0 = Get-Date
    while ((Get-Process MeitouClient -ErrorAction SilentlyContinue) -and ((Get-Date) - $t0).TotalSeconds -lt 90) { Start-Sleep 2 }
}
