# Meitou's agent channel: .\agent.ps1 [-Timeout s] <command words...>  (prints the JSON answer; exit 0 when ok)
[CmdletBinding(PositionalBinding = $false)]
param([double]$Timeout = 10, [Parameter(ValueFromRemainingArguments = $true)][string[]]$Words)
$D = "C:\Kenshi_Meitou\ctl"
$cmdText = ($Words -join ' ')
$id = [string]([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())
$t0 = Get-Date
while (Test-Path "$D\cmd") { if (((Get-Date) - $t0).TotalSeconds -gt $Timeout) { "TIMEOUT waiting for cmd slot"; exit 2 }; Start-Sleep -Milliseconds 100 }
$tmp = "$D\.t$id"
[System.IO.File]::WriteAllText($tmp, "$id $cmdText`n")
Move-Item $tmp "$D\cmd"
$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt $Timeout) {
    if (Test-Path "$D\out") {
        $l = Select-String -Path "$D\out" -Pattern "^\{`"id`":$id," | Select-Object -First 1
        if ($l) { $l.Line; if ($l.Line -like '*"ok":true*') { exit 0 } else { exit 1 } }
    }
    Start-Sleep -Milliseconds 100
}
"TIMEOUT ($cmdText)"; exit 2
