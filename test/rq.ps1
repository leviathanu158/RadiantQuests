# The mod's own test channel: .\rq.ps1 "cmd1" "cmd2" ...  (prints one "<cmd> => <result>" line per command)
[CmdletBinding(PositionalBinding = $false)]
param([double]$Timeout = 10, [Parameter(ValueFromRemainingArguments = $true)][string[]]$Lines)
$D = "C:\Kenshi_Meitou\ctl"
$before = if (Test-Path "$D\rq.out") { @(Get-Content "$D\rq.out").Count } else { 0 }
$t0 = Get-Date
while (Test-Path "$D\rq.cmd") { if (((Get-Date) - $t0).TotalSeconds -gt $Timeout) { "TIMEOUT slot"; exit 2 }; Start-Sleep -Milliseconds 100 }
[System.IO.File]::WriteAllText("$D\rq.tmp", ($Lines -join "`n") + "`n"); Move-Item "$D\rq.tmp" "$D\rq.cmd" -Force
$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt $Timeout) {
    if (Test-Path "$D\rq.out") { $all = @(Get-Content "$D\rq.out"); if ($all.Count -ge $before + $Lines.Count) { $all[$before..($all.Count - 1)]; exit 0 } }
    Start-Sleep -Milliseconds 150
}
"TIMEOUT"; exit 2
