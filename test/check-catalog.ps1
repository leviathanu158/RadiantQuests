# Radiant Quests: run the offline catalog check on the plain base game and on the installed mod list (no game started).
# Writes test\catalog-base.txt and test\catalog-full.txt and prints a short summary of each.
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$launcher = Get-Content (Join-Path $env:LOCALAPPDATA "Meitou\launcher.json") -Raw | ConvertFrom-Json
$version = Get-ChildItem (Join-Path $launcher.InstallFolder "versions") -Directory | Sort-Object Name -Descending | Select-Object -First 1
$proj = Join-Path $here "CatalogCheck\CatalogCheck.csproj"
& dotnet build $proj -c Release -nologo -v quiet "-p:MeitouDir=$($version.FullName)" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "catalog check failed to build" -ForegroundColor Red; exit 1 }
$dll = Get-ChildItem (Join-Path $here "CatalogCheck\bin\Release") -Recurse -Filter CatalogCheck.dll | Select-Object -First 1
Copy-Item (Join-Path $version.FullName "*.dll") $dll.DirectoryName -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $here "..\bin\Release\net11.0\RadiantQuests.dll") $dll.DirectoryName -Force
foreach ($which in "base", "full") {
    $out = Join-Path $here "catalog-$which.txt"
    & dotnet $dll.FullName $launcher.KenshiPath $which (Join-Path $here "..") 2>&1 | Out-File -Encoding utf8 $out
    Write-Host "== $which ($out)"
    Get-Content $out | Select-String '^== |^  (E|D|C|B|A|S|SS) *:' | ForEach-Object { $_.Line }
}
