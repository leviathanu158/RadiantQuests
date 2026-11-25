# Radiant Quests: writes the mod's Kenshi data file data\RadiantQuests.mod (the Guild Parcel item) with test\MakeData. Run once after a
# change to MakeData; the file is committed and build.ps1 installs it.
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$launcher = Get-Content (Join-Path $env:LOCALAPPDATA "Meitou\launcher.json") -Raw | ConvertFrom-Json
$version = Get-ChildItem (Join-Path $launcher.InstallFolder "versions") -Directory | Sort-Object Name -Descending | Select-Object -First 1
$proj = Join-Path $here "MakeData\MakeData.csproj"
& dotnet build $proj -c Release -nologo -v quiet "-p:MeitouDir=$($version.FullName)" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "MakeData failed to build" -ForegroundColor Red; exit 1 }
$dll = Get-ChildItem (Join-Path $here "MakeData\bin\Release") -Recurse -Filter MakeData.dll | Select-Object -First 1
Copy-Item (Join-Path $version.FullName "*.dll") $dll.DirectoryName -Force -ErrorAction SilentlyContinue
& dotnet $dll.FullName $launcher.KenshiPath (Join-Path $here "..\data\RadiantQuests.mod")
