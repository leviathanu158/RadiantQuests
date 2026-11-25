# Radiant Quests tests: switch Manual Combat off for a test run and back on afterwards (his request, 2026-11-24: with it on, the character the
# agent channel orders into a fight may not listen). Off removes the "ManualCombat.mod" line from Kenshi's data\mods.cfg (a copy is kept as
# test\mods.cfg.bak); On puts the saved copy back exactly. Any script that turned it off must turn it on again (quick.ps1 -NoManualCombat does,
# in a finally block); a copy left over from a crashed run is put back by the next On (quick.ps1 always calls it first).
#   .\manual-combat.ps1 -Off
#   .\manual-combat.ps1 -On
param([switch]$Off, [switch]$On)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$launcher = Get-Content (Join-Path $env:LOCALAPPDATA "Meitou\launcher.json") -Raw | ConvertFrom-Json
$cfg = Join-Path $launcher.KenshiPath "data\mods.cfg"
$bak = Join-Path $here "mods.cfg.bak"
if ($On) {
    if (Test-Path $bak) {
        Copy-Item $bak $cfg -Force
        Remove-Item $bak
        Write-Host "Manual Combat: back on (mods.cfg restored)"
    }
    exit 0
}
if ($Off) {
    if (-not (Test-Path $bak)) { Copy-Item $cfg $bak }
    $text = [System.IO.File]::ReadAllText($cfg)
    $lines = $text -split "`r`n"
    $kept = $lines | Where-Object { $_ -ne "ManualCombat.mod" }
    # Written back without a byte order mark and with the file's own CRLF line ends (Kenshi reads it raw).
    [System.IO.File]::WriteAllText($cfg, ($kept -join "`r`n"), (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Manual Combat: off for this run ($($lines.Count - @($kept).Count) line removed)"
}
