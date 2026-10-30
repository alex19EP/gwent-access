<#
.SYNOPSIS
    Build (and optionally publish) the Gwent.GameRefs NuGet package.

.DESCRIPTION
    Runs JetBrains Refasmer over BepInEx\core and BepInEx\interop of a local Gwent install (BepInEx 6 be.790,
    started once so the interop assemblies exist) to produce metadata-only reference assemblies (method bodies
    stripped), and packs them into a NuGet package consumed by CI
    (.github/workflows/build.yml), so the mod builds without a game install. Nothing game-derived is committed
    to the repo; rerun this after a game patch or a BepInEx upgrade (the interop assemblies change with both).

    Refasmer keeps ALL members (--all), including non-public ones, so code reaching private game members still
    compiles.

.PARAMETER Version
    Package version. Defaults to the date (yyyy.M.d): GitHub Packages versions are immutable and Gwent has no
    version file to read. CI takes the highest version.

.PARAMETER GwentDir
    The Gwent folder. Defaults to GWENT_DIR (which `just` loads from .env), else the default Steam path.

.PARAMETER Push
    After packing, push the .nupkg to GitHub Packages under alex19EP. Requires -ApiKey.

.PARAMETER ApiKey
    A GitHub PAT with the write:packages scope. Only needed with -Push. Defaults to GH_PACKAGES_TOKEN.

.EXAMPLE
    pwsh scripts/build-gamerefs.ps1                  # build only
    pwsh scripts/build-gamerefs.ps1 -Push            # build and publish (GH_PACKAGES_TOKEN set)
#>
[CmdletBinding()]
param(
    [string]$Version = (Get-Date -Format 'yyyy.M.d'),
    [string]$GwentDir = $(if ($env:GWENT_DIR) { $env:GWENT_DIR } else { 'C:\Program Files (x86)\Steam\steamapps\common\GWENT The Witcher Card Game' }),
    [switch]$Push,
    [string]$ApiKey = $env:GH_PACKAGES_TOKEN
)
$ErrorActionPreference = 'Stop'

$repo    = Split-Path -Parent $PSScriptRoot
$pkgDir  = Join-Path $repo 'build/gamerefs'
$coreOut = Join-Path $pkgDir 'BepInEx/core'
$iopOut  = Join-Path $pkgDir 'BepInEx/interop'
$outDir  = Join-Path $pkgDir 'out'

$core    = Join-Path $GwentDir 'BepInEx/core'
$interop = Join-Path $GwentDir 'BepInEx/interop'
foreach ($p in @($core, $interop)) {
    if (-not (Test-Path $p)) { throw "Not found: $p (BepInEx installed and started once?)" }
}

if (-not (Get-Command refasmer -ErrorAction SilentlyContinue)) {
    Write-Host 'Installing JetBrains.Refasmer.CliTool...'
    dotnet tool install --global JetBrains.Refasmer.CliTool | Out-Null
    $env:PATH = "$env:PATH;$env:USERPROFILE\.dotnet\tools"
}

foreach ($d in @($coreOut, $iopOut, $outDir)) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    New-Item -ItemType Directory -Path $d -Force | Out-Null
}

# Refasmer opens its inputs without read sharing, so a running game (which holds BepInEx\core and the interop
# assemblies open) makes it skip them; copies are readable either way.
$src = Join-Path ([IO.Path]::GetTempPath()) "gwent-gamerefs-src"
if (Test-Path $src) { Remove-Item $src -Recurse -Force }
New-Item -ItemType Directory -Path "$src/core", "$src/interop" -Force | Out-Null
Copy-Item "$core/*.dll" "$src/core"
Copy-Item "$interop/*.dll" "$src/interop"

# -g expands the glob internally (avoids the Windows command-line length limit on the interop folder).
Write-Host 'Refasming BepInEx\core ...'
refasmer -g --all -c -O $coreOut "$src/core/*.dll"
if ($LASTEXITCODE) { throw "refasmer failed on BepInEx\core ($LASTEXITCODE)" }
Write-Host 'Refasming BepInEx\interop ...'
refasmer -g --all -c -O $iopOut "$src/interop/*.dll"
if ($LASTEXITCODE) { throw "refasmer failed on BepInEx\interop ($LASTEXITCODE)" }
Remove-Item $src -Recurse -Force

# -c keeps going past a failed assembly, so check the counts: a missing reference only shows up as a CI build error.
$counts = foreach ($pair in @(@("$core/*.dll", $coreOut), @("$interop/*.dll", $iopOut))) {
    $in = (Get-ChildItem $pair[0]).Count; $out = (Get-ChildItem "$($pair[1])/*.dll").Count
    if ($out -lt $in) { Write-Warning "Only $out of $in assemblies refasmed into $($pair[1])" }
    $out
}
Write-Host ('Refasmed {0} core + {1} interop assemblies' -f $counts[0], $counts[1])

Write-Host "Packing Gwent.GameRefs $Version ..."
dotnet pack (Join-Path $pkgDir 'Gwent.GameRefs.csproj') -o $outDir "-p:Version=$Version" -v:m
if ($LASTEXITCODE) { throw "dotnet pack failed ($LASTEXITCODE)" }

$nupkg = Get-ChildItem "$outDir/*.nupkg" | Select-Object -First 1
Write-Host ("Built {0} ({1:N1} MB)" -f $nupkg.Name, ($nupkg.Length/1MB)) -ForegroundColor Green

if ($Push) {
    if (-not $ApiKey) { throw '-Push needs a token: pass -ApiKey, or set GH_PACKAGES_TOKEN (a write:packages PAT) in .env' }
    Write-Host 'Pushing to GitHub Packages (alex19EP)...'
    dotnet nuget push $nupkg.FullName --source 'https://nuget.pkg.github.com/alex19EP/index.json' --api-key $ApiKey
    if ($LASTEXITCODE) { throw "nuget push failed ($LASTEXITCODE)" }
    Write-Host 'Pushed. Make the package public (or grant the repo access) so the CI GITHUB_TOKEN can read it.' -ForegroundColor Yellow
}
