<#
.SYNOPSIS
    Assemble the player zip: BepInEx, the mod, the Prism speech library and the installer scripts.

.DESCRIPTION
    The zip is laid out like the Gwent folder, so package/Install.bat can robocopy it in place:
        BepInEx 6 be.790 IL2CPP (doorstop proxy renamed winhttp.dll -> version.dll, see package/README.txt)
        BepInEx\plugins\GwentAccess\GwentAccess.dll                 the host
        BepInEx\plugins\GwentAccess\Module\GwentAccess.Module.dll   the module (stable file name)
        BepInEx\plugins\GwentAccess\prism.dll                       speech (built from third_party/prism)
        BepInEx\plugins\GwentAccess\NAudio.dll                      the sound mixer; the module does not load without it
        BepInEx\plugins\GwentAccess\audio\                          the sound stems (assets/audio)
        BepInEx\plugins\GwentAccess\lang\                           the mod's speech in other languages (assets/lang)
        BepInEx\plugins\GwentAccess\THIRD-PARTY-NOTICES.md          licenses of what the zip carries
        BepInEx\plugins\GwentAccess\licenses\                       their full texts (third_party/licenses)
        BepInEx\plugins\GwentAccess\build.txt                       what was built, for bug reports
        BepInEx\config\BepInEx.cfg                                  UnityLogListening off, or the game crashes at boot
        Install.bat, Update.bat, Uninstall.bat, MakeShortcut.bat, Gwent-NoLauncher.bat, README.txt   (from package/)

    Build first (dotnet build games/Gwent/Module/GwentAccess.Module.csproj -c Release); this only packs the
    output. The host build drops prism.dll next to GwentAccess.dll (build/Prism.targets).

    Gwent only so far: package/ holds Gwent's installer scripts. Thronebreaker Access has no player zip yet.

.PARAMETER Configuration
    The build configuration to pack. Release for players: Debug carries the dev server and Roslyn.

.PARAMETER BepInExZip
    A local BepInEx be.790 IL2CPP win-x64 zip. Downloaded from builds.bepinex.dev when omitted.

.PARAMETER Label
    Names the zip (GwentAccess-<Label>.zip) and goes into build.txt, e.g. dev or 0.27.0.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$BepInExZip = '',
    [string]$Label = 'local'
)
$ErrorActionPreference = 'Stop'

# The BepInEx build the mod is developed and tested against (LogOutput.log: "BepInEx 6.0.0-be.790",
# "Built from commit 4e98359..."). The interop assemblies in Gwent.GameRefs come from this build too.
$BepInExUrl = 'https://builds.bepinex.dev/projects/bepinex_be/790/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.790%2B4e98359.zip'

$repo   = Split-Path -Parent $PSScriptRoot
$dist   = Join-Path $repo 'dist'
$stage  = Join-Path $dist 'stage'
$hostOut = Join-Path $repo "games/Gwent/Host/bin/$Configuration/net6.0"
$modOut  = Join-Path $repo "games/Gwent/Module/bin/$Configuration/net6.0"

$hostDll = Join-Path $hostOut 'GwentAccess.dll'
if (-not (Test-Path $hostDll)) { throw "Host not built: $hostDll" }
# The module's assembly name is timestamped per build; the newest one is this build's.
$modDll = Get-ChildItem $modOut -Filter 'GwentAccess.Module_*.dll' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $modDll) { throw "Module not built under $modOut" }
$prismDll = Join-Path $hostOut 'prism.dll'
if (-not (Test-Path $prismDll)) { throw "prism.dll not built: $prismDll (a zip without it is a mute mod)" }
# The module's NuGet references are not copied to its output (CopyLocalLockFileAssemblies is off), so NAudio comes from
# the package folder restore actually used. Same version and path as the deploy step in build/Module.props.
$assetsFile = Join-Path $repo 'games/Gwent/Module/obj/project.assets.json'
if (-not (Test-Path $assetsFile)) { throw "Module not restored: $assetsFile" }
$nugetRoot = (Get-Content $assetsFile -Raw | ConvertFrom-Json).packageFolders.PSObject.Properties.Name | Select-Object -First 1
$naudioDll = Join-Path $nugetRoot 'naudio/1.10.0/lib/netcoreapp3.0/NAudio.dll'
if (-not (Test-Path $naudioDll)) { throw "NAudio.dll not found: $naudioDll (a zip without it can't load the module)" }
$audioDir = Join-Path $repo 'assets/audio'
if (-not (Get-ChildItem $audioDir -Recurse -Filter '*.wav' -ErrorAction SilentlyContinue)) { throw "No sounds under $audioDir" }
$langDir = Join-Path $repo 'assets/lang'
if (-not (Get-ChildItem $langDir -Filter '*.json' -ErrorAction SilentlyContinue)) { throw "No translations under $langDir" }

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

if (-not $BepInExZip) {
    $BepInExZip = Join-Path $dist 'BepInEx.zip'
    if (-not (Test-Path $BepInExZip)) {
        Write-Host "Downloading $BepInExUrl"
        Invoke-WebRequest -Uri $BepInExUrl -OutFile $BepInExZip
    }
}
Expand-Archive -Path $BepInExZip -DestinationPath $stage -Force
# Gwent's login breaks with a winhttp.dll in its folder, so the mod ships doorstop as version.dll and
# Install.bat renames any winhttp.dll it finds (which would otherwise be this one).
$winhttp = Join-Path $stage 'winhttp.dll'
if (Test-Path $winhttp) { Move-Item $winhttp (Join-Path $stage 'version.dll') -Force }
if (-not (Test-Path (Join-Path $stage 'version.dll'))) { throw 'The BepInEx zip has no doorstop proxy (winhttp.dll or version.dll)' }
if (-not (Test-Path (Join-Path $stage 'BepInEx/core/BepInEx.Unity.IL2CPP.dll'))) { throw 'The BepInEx zip is not the IL2CPP build' }

$plugin = Join-Path $stage 'BepInEx/plugins/GwentAccess'
New-Item -ItemType Directory -Path (Join-Path $plugin 'Module') -Force | Out-Null
Copy-Item $hostDll $plugin
Copy-Item $modDll.FullName (Join-Path $plugin 'Module/GwentAccess.Module.dll')
Copy-Item $prismDll $plugin
Copy-Item $naudioDll $plugin
Copy-Item $audioDir (Join-Path $plugin 'audio') -Recurse
New-Item -ItemType Directory -Path (Join-Path $plugin 'lang') -Force | Out-Null
Copy-Item (Join-Path $langDir '*.json') (Join-Path $plugin 'lang')
Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $plugin
Copy-Item (Join-Path $repo 'third_party/licenses') (Join-Path $plugin 'licenses') -Recurse
Copy-Item (Join-Path $repo 'package/*') $stage

# BepInEx's Unity log hook converts a delegate through Il2CppInterop's ClassInjector, which dies with an access
# violation on Gwent's GameAssembly.dll: with the default (true) the game crashes before any plugin loads. The zip
# has to carry the setting, since a fresh install would otherwise generate the default. BepInEx fills in the rest
# of the file on the next start. Install.bat and Update.bat copy this over a tester's existing config on purpose,
# which also repairs an install that already crashed.
$config = Join-Path $stage 'BepInEx/config'
New-Item -ItemType Directory -Path $config -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $config 'BepInEx.cfg'), "[Logging]`r`nUnityLogListening = false`r`n")

$commit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (git -C $repo rev-parse HEAD).Trim() }
@(
    "Gwent Access $Label ($Configuration)"
    "commit $commit"
    "module $($modDll.BaseName)"
    "built $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm')) UTC"
) | Set-Content -Path (Join-Path $plugin 'build.txt') -Encoding utf8

$zip = Join-Path $dist "GwentAccess-$Label.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host ("Packed {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length/1MB)) -ForegroundColor Green
