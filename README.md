# Gwent Access

Gwent Access is a screen reader mod for Gwent: The Witcher Card Game (Steam, Windows). It makes the game playable with NVDA.

The mod reads the menus, cards, popups and the battle board. It adds keyboard commands for playing matches, building decks, drafting and using the shop. Claude (Anthropic) wrote it with smoovgunner, a blind player who did all the testing by ear.

## Download and install

Download the package (BepInEx included) from this repository's Releases page or the [AudioGames.net forum](https://forum.audiogames.net/). Unzip it, run `Install.bat`, and read `README.txt` for setup and every key.

You need sighted help once, to log in to GOG through Gwent's launcher. After that, the Gwent Accessible desktop shortcut skips the launcher.

What changed in each version is in [CHANGELOG.md](CHANGELOG.md).

## What's in this repository

- `src/`: C# source shared by every game on Gwent's engine. `Access.Core` is the graph kernel, `Host` and `Dev` are the BepInEx plugin host, and `Shared` is the module code the games share.
- `games/Gwent/`: Gwent Access's projects and screens.
- `games/Thronebreaker/`: Thronebreaker Access, an early start on the same base for Thronebreaker: The Witcher Tales.
- `package/`: the installer scripts, the launcher-skip script and the player readme that ship in the release zip.

## Building

You need:
- .NET SDK 6 or newer.
- Gwent with BepInEx 6.0.0-be.790 (IL2CPP), started once so BepInEx creates `BepInEx\interop`.
- For speech: CMake 3.24 or newer and Visual Studio Build Tools with the C++ workload. The [Prism](https://github.com/ethindp/prism) speech library is a git submodule. Fetch it with `git submodule update --init third_party/prism`, after `git config core.longpaths true`. The build compiles it into `prism.dll` (`scripts/build-prism.ps1`) and rebuilds it only when the submodule moves. A Debug build can skip it with `-p:SkipPrismBuild=true`, but then it has no speech.

```
dotnet build games/Gwent/Module/GwentAccess.Module.csproj -c Release -p:Deploy=true
```

If Gwent isn't in the default Steam folder, add `-p:GameDir="D:\path\to\GWENT The Witcher Card Game"`.

The mod is two assemblies. `GwentAccess.dll` is the small BepInEx plugin (`src/Host`, `src/Dev` and `games/Gwent/Host/GameInfo.cs`). It loads everything else from `Module\GwentAccess.Module.dll`. `-p:Deploy=true` copies both, and `prism.dll`, into `BepInEx\plugins\GwentAccess\`. Close the game first.

With [just](https://github.com/casey/just), `just all` runs the tests and deploys, and `just reload` swaps a rebuilt module into the running game (Debug builds with the dev server on). `just game=tb <recipe>` does the same for Thronebreaker. Run `just` to list the other recipes.

## How it works

- `src/Host/Plugin.cs` and `src/Host/ModuleLoader.cs`: the BepInEx entry point. It loads the module from bytes and swaps it on reload.
- `games/Gwent/Module/ModuleMain.cs`: the module entry. It applies each Harmony patch separately, so one failure can't disable the mod, and it undoes everything on reload.
- `src/Shared/Speech.cs` and `src/Host/Prism.cs`: speech through the screen reader, using Prism. The host owns the native context, so speech survives module reloads.
- `src/Shared/Ui/`: the graph screens. Each screen declares what it shows, and the navigator moves the cursor and speaks.
- `games/Gwent/Module/Ui/`: Gwent's own screens (main menu, deck builder, shop, Journey, Autumn Cycle, Draft and the rest). `PanelScreen.cs` reads any panel that has no screen of its own, top to bottom.
- `src/Shared/BattleSpeech.cs`, `Narrator.cs` and `Plays.cs`: match commentary, what the game is waiting for, and reading the board.
- `games/Gwent/Module/Hotkeys.cs`: the keys that work everywhere (F1 help, F5 controller mode, F7 repeat, F8 on and off, F9 where am I). A background thread reads them, and the work runs on the game thread through `MainThread.cs`.
- `src/Shared/CardReader.cs`: turns cards into words, line by line.

The log, `BepInEx\LogOutput.log`, records every spoken line and key, which makes bug reports easy to trace.

## Releasing

Add a section for the new version to the top of `CHANGELOG.md`, set the same version in `games/Gwent/Host/GameInfo.cs` and `package/README.txt`, then push a tag such as `v0.28.0`. The Build workflow builds the zip and publishes a release, using that version's changelog section as the notes. Every push to `main` also updates the rolling `dev` pre-release.

## Contributing

Suggestions and pull requests are welcome. Report a problem through the bug report form on the Issues page. It asks for the build, the screen, the keys you pressed and the log.

In a pull request, say what you tested and on which screen, and include a `LogOutput.log` excerpt for a bug fix. Run `just test` before you push.

## License

MIT. See [LICENSE](LICENSE). Gwent is a trademark of CD PROJEKT RED. This is an unofficial fan mod. BepInEx (LGPL 2.1) and Prism (MPL 2.0) are separate projects under their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
