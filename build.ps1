# Radiant Quests for Meitou: build the mod and install it into Kenshi's mods folder.
# Run build.bat (double-click). Optional: -MeitouDir <a Meitou version folder>  -KenshiDir <your Kenshi folder>  -NoInstall
param(
    [string]$MeitouDir = "",
    [string]$KenshiDir = "",
    [switch]$NoInstall
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$wantVersion = "0.23.0"
$modName = "RadiantQuests"

function Say($text, $color = "Gray") { Write-Host $text -ForegroundColor $color }

# 1. .NET SDK
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$sdks = if ($dotnet) { & dotnet --list-sdks 2>$null } else { @() }
if (-not $sdks) {
    Say "The .NET SDK is not installed." Red
    Say "Install the .NET 11 SDK (x64) from https://dotnet.microsoft.com/download/dotnet/11.0 , then run build.bat again." Yellow
    exit 1
}
Say ("Found .NET SDK: " + (($sdks | Select-Object -Last 1) -split ' ')[0])

# 2. Meitou and Kenshi folders, from the Meitou launcher's settings (%LocalAppData%\Meitou\launcher.json)
$launcherJson = Join-Path $env:LOCALAPPDATA "Meitou\launcher.json"
$config = $null
if (Test-Path $launcherJson) {
    try { $config = Get-Content $launcherJson -Raw | ConvertFrom-Json } catch { Say "Could not read $launcherJson" Yellow }
}
function Get-Prop($obj, [string[]]$names) {
    if (-not $obj) { return $null }
    foreach ($n in $names) { $p = $obj.PSObject.Properties | Where-Object { $_.Name -ieq $n } | Select-Object -First 1; if ($p -and $p.Value) { return [string]$p.Value } }
    return $null
}

if (-not $MeitouDir) {
    $installRoot = Get-Prop $config @("InstallFolder")
    if (-not $installRoot) { $installRoot = Join-Path $env:LOCALAPPDATA "Meitou" }
    $versions = Join-Path $installRoot "versions"
    if (-not (Test-Path $versions)) {
        Say "No installed Meitou versions found in $versions" Red
        Say "Run again with:  build.bat -MeitouDir ""C:\path\to\your\Meitou version folder""  (the folder holding MeitouClient.dll)" Yellow
        exit 1
    }
    $candidates = Get-ChildItem $versions -Recurse -Filter MeitouClient.dll -Depth 3 | ForEach-Object { $_.Directory }
    $match = $candidates | Where-Object { $_.FullName -like "*$wantVersion*" } | Select-Object -First 1
    if (-not $match) {
        $match = $candidates | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($match) { Say "Meitou $wantVersion not found; building against the newest installed version instead: $($match.FullName)" Yellow }
    }
    if (-not $match) { Say "No MeitouClient.dll under $versions" Red; exit 1 }
    $MeitouDir = $match.FullName
}
if (-not (Test-Path (Join-Path $MeitouDir "MeitouClient.dll"))) { Say "MeitouClient.dll not found in $MeitouDir" Red; exit 1 }
Say "Meitou: $MeitouDir"

# 3. Build
& dotnet build (Join-Path $here "$modName.csproj") -c Release -nologo -v quiet "-p:MeitouDir=$MeitouDir"
if ($LASTEXITCODE -ne 0) {
    Say "" ; Say "BUILD FAILED. Copy everything above (or a screenshot) into the chat so it can be fixed." Red
    exit 1
}
$dll = Get-ChildItem (Join-Path $here "bin\Release") -Recurse -Filter "$modName.dll" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

# 4. Stage the mod folder: the dll, meitou.json and an empty Kenshi data mod (RadiantQuests.mod) that the mod list shows
$stage = Join-Path $here "out\$modName"
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $dll.FullName $stage -Force
Copy-Item (Join-Path $here "meitou.json") $stage -Force
Copy-Item (Join-Path $here "overrides.json") $stage -Force

# The Kenshi data file: data\RadiantQuests.mod (the Guild Parcel item, made by test\make-data.ps1); an empty one if it is missing.
$dataFile = Join-Path $here "data\$modName.mod"
if (Test-Path $dataFile) { Copy-Item $dataFile (Join-Path $stage "$modName.mod") -Force }
else {
    function Write-FcsString([System.IO.BinaryWriter]$w, [string]$s) { $b = [System.Text.Encoding]::ASCII.GetBytes($s); $w.Write([int]$b.Length); $w.Write($b) }
    $fs = [System.IO.File]::Create((Join-Path $stage "$modName.mod"))
    $w = New-Object System.IO.BinaryWriter($fs)
    try {
        $w.Write([int]16); $w.Write([int]1)                      # file type, version
        Write-FcsString $w "RadiantQuests"                      # author
        Write-FcsString $w "Radiant Quests for Meitou (code mod): repeatable guild quests, rep and ranks. Holds no records."
        Write-FcsString $w ""; Write-FcsString $w ""             # dependencies, references
        $w.Write([int]0); $w.Write([int]0)                       # flags, record count
    } finally { $w.Close() }
    Say "data\$modName.mod missing: installed an empty data file (no courier quests)" Yellow
}
Say "Built: $stage" Green

if ($NoInstall) { exit 0 }

# The interface pictures (the hand-in circles), next to the dll.
Get-ChildItem (Join-Path $here "data\ui") -Filter *.png -ErrorAction SilentlyContinue | ForEach-Object { Copy-Item $_.FullName $stage -Force }

# 5. Install into Kenshi\mods\RadiantQuests
if (-not $KenshiDir) { $KenshiDir = Get-Prop $config @("KenshiPath") }
if (-not $KenshiDir -or -not (Test-Path $KenshiDir)) {
    Say "Kenshi folder not found. Copy the folder $stage into Kenshi\mods yourself, or run: build.bat -KenshiDir ""C:\path\to\Kenshi""" Yellow
    exit 0
}
$target = Join-Path $KenshiDir "mods\$modName"
New-Item -ItemType Directory -Force $target | Out-Null
# Everything but overrides.json, which a player may have edited: that one only when the install has none yet.
Get-ChildItem $stage -File | Where-Object { $_.Name -ne "overrides.json" -or -not (Test-Path (Join-Path $target "overrides.json")) } |
    ForEach-Object { Copy-Item $_.FullName $target -Force }
Say "Installed to $target" Green
Say "Now open the Meitou launcher, tick RadiantQuests in the mod list, and press Play." Green
