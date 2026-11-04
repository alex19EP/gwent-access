# Gwent Access & Thronebreaker Access -- build, deploy and dev loop
#
#   just              list the recipes
#   just all          test + build + deploy + verify (the full run)
#   just deploy       build and copy host + module into the game (game closed), then verify
#   just reload       rebuild the module and swap it into the RUNNING game (Debug, dev server on)
#   just run          start the game
#
# One repo, one mod per game on Gwent's engine. Pick the game with game=gwent (default) or game=tb:
#   just game=tb deploy
#
# Each mod is two assemblies (the WrathAccess host/module split): the host <Mod>.dll (src/Host, src/Dev and the
# game's GameInfo.cs, games/<Game>/Host) is loaded by BepInEx and locked while the game runs; changing it needs
# `just restart`. Everything else is the module (src/Access.Core, src/Shared, games/<Game>/Module), byte-loaded
# from Module\ and hot-swapped by `just reload`.
#
# The game folders come from GWENT_DIR / THRONEBREAKER_DIR (environment, or a git-ignored .env at the repo root,
# e.g. GWENT_DIR=D:\SteamLibrary\steamapps\common\GWENT The Witcher Card Game), else the default Steam folder.
# Debug builds carry the dev server; pick Release with: just config=Release all
#
# Needs: .NET SDK 6+, PowerShell 7 (pwsh), and ilspycmd for `decompile`
#   dotnet tool install --global ilspycmd

set windows-shell := ['pwsh.exe', '-NoProfile', '-NoLogo', '-Command']
set dotenv-load := true

repo    := justfile_directory()
game    := 'gwent'
config  := 'Debug'

# Per game: the mod's name, its folder in the repo, the game's folder and process, the dev server's port.
modname := if game == 'gwent' { 'GwentAccess' } else if game == 'tb' { 'ThronebreakerAccess' } else { error('game must be gwent or tb') }
folder  := repo / 'games' / if game == 'tb' { 'Thronebreaker' } else { 'Gwent' }
gamedir := if game == 'tb' { env_var_or_default('THRONEBREAKER_DIR', 'C:\Program Files (x86)\Steam\steamapps\common\Thronebreaker The Witcher Tales') } else { env_var_or_default('GWENT_DIR', 'C:\Program Files (x86)\Steam\steamapps\common\GWENT The Witcher Card Game') }
process := if game == 'tb' { 'Thronebreaker' } else { 'Gwent' }
devurl  := if game == 'tb' { 'http://localhost:8774' } else { 'http://localhost:8773' }

plugins := gamedir / 'BepInEx' / 'plugins' / modname
out     := folder / 'Host' / 'bin' / config / 'net6.0'
modout  := folder / 'Module' / 'bin' / config / 'net6.0'
modproj := folder / 'Module' / modname + '.Module.csproj'

# Show the available recipes
default:
    @just --list --unsorted

# The whole pipeline: kernel tests, build, deploy, verify
all: test deploy

# --- build ------------------------------------------------------------------

# Compile the host and the module
build:
    dotnet build '{{modproj}}' -c {{config}} -p:GameDir='{{gamedir}}'

# Build prism.dll from the third_party/prism submodule into build\native. `just build` runs this already (a no-op
# unless the submodule moved), so it is only for forcing a rebuild. Needs CMake 3.24+ and VS Build Tools (C++).
#   just prism -Force            # rebuild regardless
#   just prism -Clean            # drop the CMake tree and the built DLL
prism *args:
    pwsh -NoProfile -File '{{repo}}\scripts\build-prism.ps1' {{args}}

# Find the mod's translatable lines (Loc.T/P/Mark) into assets/lang/en.json and report what each language lacks
strings *args:
    python '{{repo}}\scripts\extract-strings.py' {{args}}

# Run the graph kernel unit tests (no game needed)
test:
    dotnet test '{{repo}}\tests\Access.Core.Tests\Access.Core.Tests.csproj'

# --- deployment -------------------------------------------------------------

# (The game holds the host dll open while it runs, hence the not-running guard.)
# Build, copy into BepInEx\plugins\<Mod>, verify byte for byte (close the game first)
deploy: not-running
    dotnet build '{{modproj}}' -c {{config}} -p:GameDir='{{gamedir}}' -p:Deploy=true
    @just game={{game}} config={{config}} verify

# Compare the build output with the game's plugin folder -- the only proof the deploy landed
verify:
    @$bad = 0; \
     $mod = Get-ChildItem '{{modout}}' -Filter '{{modname}}.Module_*.dll' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1; \
     if (-not $mod) { Write-Host 'module not built' -ForegroundColor Red; exit 1 }; \
     $pairs = @( @((Join-Path '{{out}}' '{{modname}}.dll'), '{{modname}}.dll'), @((Join-Path '{{out}}' '{{modname}}.pdb'), '{{modname}}.pdb'), @((Join-Path '{{out}}' 'prism.dll'), 'prism.dll'), \
                 @($mod.FullName, 'Module\{{modname}}.Module.dll'), @([IO.Path]::ChangeExtension($mod.FullName, '.pdb'), 'Module\{{modname}}.Module.pdb') ); \
     foreach ($p in $pairs) { \
         $a = $p[0]; $f = $p[1]; $b = Join-Path '{{plugins}}' $f; \
         if (-not (Test-Path $b)) { Write-Host ('MISSING  ' + $f) -ForegroundColor Red; $bad++; continue }; \
         if ((Get-FileHash $a).Hash -ne (Get-FileHash $b).Hash) { Write-Host ('DIFFERS  ' + $f) -ForegroundColor Red; $bad++ } \
         else { Write-Host ('ok       ' + $f) -ForegroundColor Green } }; \
     Write-Host ('module build: ' + $mod.BaseName); \
     if ('{{config}}' -eq 'Debug' -and -not (Test-Path (Join-Path '{{plugins}}' 'devserver.enable'))) { \
         Write-Host 'note: no devserver.enable in the plugin folder, so the dev server stays off' -ForegroundColor Yellow }; \
     exit $bad

# Turn the dev server on for Debug builds (it only starts when this marker file exists)
devserver-on:
    @New-Item -ItemType File -Force (Join-Path '{{plugins}}' 'devserver.enable') | Out-Null; Write-Host 'dev server enabled (takes effect on next game start)'

[private]
not-running:
    @if (Get-Process {{process}} -ErrorAction SilentlyContinue) { \
         Write-Host '{{process}} is running. Close it before deploying (just game={{game}} kill).' -ForegroundColor Red; exit 1 }

# --- test loop --------------------------------------------------------------

# Start the game: Gwent without the REDlauncher window, Thronebreaker through Steam (Steam must be running)
run:
    @if ('{{game}}' -eq 'tb') { Start-Process 'steam://rungameid/973760' } \
     else { Start-Process -FilePath (Join-Path '{{gamedir}}' 'Gwent-NoLauncher.bat') -WorkingDirectory '{{gamedir}}' }

# Force-close the game and wait until the process is gone, so a deploy doesn't hit a locked DLL
kill:
    @$p = Get-Process {{process}} -ErrorAction SilentlyContinue; \
     if (-not $p) { Write-Host 'the game is not running'; exit 0 }; \
     $p | Stop-Process -Force; \
     $p | Wait-Process -Timeout 60 -ErrorAction SilentlyContinue; \
     if (Get-Process {{process}} -ErrorAction SilentlyContinue) { Write-Host 'the game is still running after 60 s' -ForegroundColor Red; exit 1 }; \
     Write-Host 'game closed' -ForegroundColor Green

# Close the game, deploy, start it again
restart: kill deploy run

# Tail the BepInEx log (every spoken line and hotkey is in it)
log lines='40':
    @Get-Content (Join-Path '{{gamedir}}' 'BepInEx\LogOutput.log') -Tail {{lines}} -Encoding utf8

# --- live game (Debug build with the dev server on) ------------------------

# Rebuild the module, copy it into the running game and swap it in (host changes still need `just restart`)
reload:
    dotnet build '{{modproj}}' -c {{config}} -p:GameDir='{{gamedir}}' -p:DeployModule=true
    @$a = Join-Path '{{out}}' '{{modname}}.dll'; $b = Join-Path '{{plugins}}' '{{modname}}.dll'; \
     if ((Test-Path $b) -and (Get-FileHash $a).Hash -ne (Get-FileHash $b).Hash) { \
         Write-Host 'warning: the host changed too; the game still runs the old one (just restart)' -ForegroundColor Yellow }; \
     curl.exe -s -m 90 -X POST '{{devurl}}/reload'; if ($LASTEXITCODE -ne 0) { Write-Host 'dev server not answering' -ForegroundColor Red; exit 1 }

# Build the module and copy it into the running game WITHOUT loading it; press F11 in the game (Debug builds)
stage:
    dotnet build '{{modproj}}' -c {{config}} -p:GameDir='{{gamedir}}' -p:DeployModule=true
    @Write-Host 'staged: press F11 in the game to load it' -ForegroundColor Green

# Which module generation and build the game is running
module:
    @curl.exe -s -m 5 '{{devurl}}/module'

# Is the dev server answering?
health:
    @curl.exe -s -m 5 '{{devurl}}/health'; if ($LASTEXITCODE -ne 0) { Write-Host 'dev server not answering (game not running, Release build, or no devserver.enable)' -ForegroundColor Yellow; exit 1 }

# What the mod spoke since cursor N (the last line gives the next cursor)
speech since='0':
    @curl.exe -s -m 10 '{{devurl}}/speech?since={{since}}'

# Run a C# script file on the game thread, e.g. just eval probe.csx
eval file:
    @curl.exe -s -m 60 --data-binary '@{{file}}' '{{devurl}}/eval'

# Forget the variables and usings of earlier evals
eval-reset:
    @curl.exe -s -m 10 -X POST '{{devurl}}/eval/reset'

# --- reference --------------------------------------------------------------

# Decompile a BepInEx interop assembly (git-ignored output): Gwent into decomp/<Name>/, Thronebreaker into
# decomp/tb/<Name>/, e.g. just game=tb decompile Assembly-CSharp
decompile name:
    ilspycmd (Join-Path '{{gamedir}}' 'BepInEx\interop\{{name}}.dll') -o '{{repo}}\decomp\{{ if game == "tb" { "tb\\" } else { "" } }}{{name}}' -p

# Show the paths this justfile resolved, and whether they exist
paths:
    @Write-Host ('game     {{gamedir}}   ' + $(if (Test-Path (Join-Path '{{gamedir}}' '{{process}}.exe')) { '[found]' } else { '[MISSING]' })); \
     Write-Host ('plugins  {{plugins}}   ' + $(if (Test-Path '{{plugins}}') { '[found]' } else { '[MISSING]' })); \
     Write-Host ('build    {{out}}   ' + $(if (Test-Path (Join-Path '{{out}}' '{{modname}}.dll')) { '[built]' } else { '[not built]' }))
