# Radiant Quests: which save best matches the CURRENT mod list (his rule, 2026-11-24: test on the save closest to the mods he runs now).
# Scores every save in %LOCALAPPDATA%\Meitou\save: the data mods (Kenshi data\mods.cfg) named in its records ("12345-rebirth.mod"), and the
# code mods in its meitou-mods\environment.json, against what is installed and ticked now. Prints the saves best first; -Name prints only the
# best save's folder name (for quick.ps1 -Save).
param([switch]$Name, [int]$Top = 6)
$ErrorActionPreference = "Stop"
$launcher = Get-Content (Join-Path $env:LOCALAPPDATA "Meitou\launcher.json") -Raw | ConvertFrom-Json
$cfg = Join-Path $launcher.KenshiPath "data\mods.cfg"
$wanted = @(Get-Content $cfg | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() })
$wantedSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$wanted, [StringComparer]::OrdinalIgnoreCase)
$codeNow = @($wanted | ForEach-Object { $_ -replace '\.mod$', '' })
# Every installed mod's file name (Kenshi\mods and the Workshop folder beside steamapps\common).
$installed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$workshop = Join-Path (Split-Path (Split-Path $launcher.KenshiPath)) "workshop\content\233860"
foreach ($root in @((Join-Path $launcher.KenshiPath "mods"), $workshop)) {
    if (Test-Path $root) { Get-ChildItem $root -Recurse -Depth 1 -Filter *.mod -File -ErrorAction SilentlyContinue | ForEach-Object { [void]$installed.Add($_.Name) } }
}
$rows = foreach ($save in Get-ChildItem (Join-Path $env:LOCALAPPDATA "Meitou\save") -Directory) {
    $file = Join-Path $save.FullName "quick.save"
    if (-not (Test-Path $file)) { continue }
    $text = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($file))
    $used = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($m in [regex]::Matches($text, '\d+-([^\x00-\x1f"]{1,80}?\.mod)')) { [void]$used.Add($m.Groups[1].Value) }
    # Only names of real installed mods count: Kenshi's base data carries record ids from its own old files (rebirth.mod, armor_otto.mod,
    # "__world reactions NE.mod"...), which are no mods.
    foreach ($b in @($used)) { if (-not $installed.Contains($b)) { [void]$used.Remove($b) } }
    $missing = @($used | Where-Object { -not $wantedSet.Contains($_) })
    $present = @($used | Where-Object { $wantedSet.Contains($_) })
    $code = @()
    $env = Join-Path $save.FullName "meitou-mods\environment.json"
    if (Test-Path $env) { $code = @((Get-Content $env -Raw | ConvertFrom-Json).mods | ForEach-Object { $_.name }) }
    $codeMissing = @($code | Where-Object { $codeNow -notcontains $_ })
    # Best: none of its data mods missing now, most of the current ones in it, recent.
    [pscustomobject]@{
        Save = $save.Name; Missing = $missing.Count; Matching = $present.Count; DataMods = $used.Count
        CodeMissing = $codeMissing.Count; Changed = $save.LastWriteTime
        Score = - 10 * $missing.Count - $codeMissing.Count + $present.Count
    }
}
$sorted = @($rows | Sort-Object Score, @{ Expression = { $_.Save -notmatch '^(autosave|quicksave)' } }, Changed -Descending)
if ($Name) { $sorted[0].Save; exit }
"current data mods: $($wanted.Count) (mods.cfg)"
$sorted | Select-Object -First $Top | Format-Table Save, Score, Missing, Matching, DataMods, CodeMissing, Changed -AutoSize | Out-String -Width 200
