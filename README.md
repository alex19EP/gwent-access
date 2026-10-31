# Gwent Access

A screen reader mod for **Gwent: The Witcher Card Game** (Steam, Windows) that makes the game playable with **NVDA**.

It reads the menus, cards, popups and the battle board, and adds keyboard commands for playing matches, building decks and using the shop. It was built from scratch by Claude (Anthropic) together with **smoovgunner**, a blind player who did all of the testing by ear.

## Download and install

Players: grab the ready-made package (BepInEx included) from the release posted on the [AudioGames.net forum](https://forum.audiogames.net/) or this repository's Releases page. Unzip it, run `Install.bat`, and read `README.txt` for setup and every hotkey.

You will need sighted help once to log in to GOG through Gwent's launcher. After that, the **Gwent Accessible** desktop shortcut skips the launcher.

## What's in this repository

- `src/`: C# source shared by every game on Gwent's engine: the graph kernel (`Access.Core`), the BepInEx plugin host (`Host`, `Dev`) and the shared module code (`Shared`).
- `games/Gwent/`: Gwent Access's own projects and screens. `games/Thronebreaker/`: Thronebreaker Access, an early start on the same base for **Thronebreaker: The Witcher Tales**.
- `package/`: the installer scripts, the launcher-skip script and the player readme that ship in the release zip.

## Building

Requirements:
- .NET SDK 6 or newer.
- Gwent installed with **BepInEx 6.0.0-be.790 (IL2CPP)**, started once so BepInEx generates `BepInEx\interop`.
- For speech: CMake 3.24+ and Visual Studio Build Tools with the C++ workload. The [Prism](https://github.com/ethindp/prism) speech library is a git submodule (`git submodule update --init third_party/prism`, with `git config core.longpaths true`) that the build compiles into `prism.dll` (`scripts/build-prism.ps1`; it only rebuilds when the submodule moves). A Debug build can skip it with `-p:SkipPrismBuild=true`, but has no speech then.

```
dotnet build games/Gwent/Module/GwentAccess.Module.csproj -c Release -p:Deploy=true
```

If Gwent isn't in the default Steam folder, add `-p:GameDir="D:\path\to\GWENT The Witcher Card Game"`.

The mod is two assemblies. `GwentAccess.dll` (`src/Host`, `src/Dev`, `games/Gwent/Host/GameInfo.cs`) is the small BepInEx plugin; it loads everything else from `Module\GwentAccess.Module.dll`. `-p:Deploy=true` copies both, and `prism.dll`, into `BepInEx\plugins\GwentAccess\` (close the game first).

With [just](https://github.com/casey/just), `just all` runs the tests and deploys, and `just reload` swaps a rebuilt module into the running game (Debug builds with the dev server on). `just game=tb <recipe>` does the same for Thronebreaker. `just` lists the rest.

## How it works (short tour)

| File | What it does |
|---|---|
| `src/Host/Plugin.cs`, `src/Host/ModuleLoader.cs` | BepInEx entry point; loads the module from bytes and swaps it on reload. |
| `games/*/Module/ModuleMain.cs` | Module entry; applies each Harmony patch separately so one failure can't disable the mod, and undoes everything on reload. |
| `src/Shared/Speech.cs`, `src/Host/Prism.cs` | Speaks through the screen reader via Prism; the host owns the native context so it survives module reloads. |
| `src/Shared/Ui/` | The graph screens: each screen declares its controls, the navigator moves the cursor and speaks. |
| `src/Shared/BattleSpeech.cs`, `Narrator.cs`, `Plays.cs` | Match commentary, what the game is waiting on, board reading. |
| `games/Gwent/Module/Legacy/Hotkeys.cs` | Gwent's older layer: reads the keyboard on a background thread; work runs on the game thread through `MainThread.cs`. |
| `games/Gwent/Module/Legacy/MenuSpeech.cs` | Hooks `CrimsonInputLayer.set_SelectedControl` and describes whatever the game selects. |
| `games/Gwent/Module/Legacy/Navigator.cs` | F2/F3 object navigation, sections (Shift+F2/F3), clicking, settings and deck builder lists, shop buying. |
| `games/Gwent/Module/Legacy/HandNav.cs` | Hand/board reading, mulligan, pass, end turn, turn timer, forfeit guard. |
| `games/Gwent/Module/DeckBuilder.cs` | Add/remove cards, deck stats, rename, duplicate, search, save and exit. |
| `src/Shared/CardText.cs`, `games/Gwent/Module/RewardBookText.cs` | Turning cards and reward nodes into words. |

Logs go to `BepInEx\LogOutput.log` and record every spoken line and hotkey, which makes bug reports easy to trace.

## Contributing

Suggestions and pull requests are welcome. Please describe what you tested and on which screen, and include a `LogOutput.log` excerpt for bug fixes. Changes are reviewed before they're merged.

## License

MIT. See [LICENSE](LICENSE). Gwent is a trademark of CD PROJEKT RED; this is an unofficial fan mod. BepInEx (LGPL 2.1) and Prism (MPL 2.0) are separate projects under their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
