# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A screen-reader (NVDA) mod for Gwent: The Witcher Card Game, built as a BepInEx 6 (be.790) **IL2CPP** plugin. This branch is rewriting the original hotkey-pile UI onto a game-agnostic **graph navigation kernel** shared with WH40KRTAccess (spec: `E:\Games\modding\WH40KRTAccess\docs\graph-a11y-spec.md`; code comments cite it as "spec §N"), with battle UX modeled on Hearthstone Access.

## Commands

Commands go through the `justfile` (needs `just`, pwsh 7, .NET SDK 6+). The game folder comes from `GWENT_DIR` (env or the git-ignored `.env`), else the default Steam path. Default config is Debug; use `just config=Release <recipe>` for Release.

- `just test` — graph kernel unit tests (no game needed). Single test: `dotnet test tests/Access.Core.Tests/Access.Core.Tests.csproj --filter "FullyQualifiedName~KeyGraphTests.SomeTest"`
- `just build` — compile host + module (the module project references the host, so one build does both)
- `just deploy` — build and copy into `BepInEx\plugins\GwentAccess\` and verify byte-for-byte; refuses while the game runs (`just kill` first). `just restart` = kill + deploy + run.
- `just reload` — rebuild only the module and hot-swap it into the **running** game (Debug build, dev server on). Host changes still need `just restart`.
- `just run`, `just log [lines]`, `just health`, `just module`, `just speech [since]`
- `just eval probe.csx` — run a C# script on the game thread (POST `/eval`); `just eval-reset` clears its state. Put probe scripts in the scratchpad, not the repo.
- `just decompile Assembly-CSharp` — ilspycmd the BepInEx interop assembly into `decomp/` (git-ignored; CDPR code, never commit). Read `decomp/` to find game APIs; use Ghidra on GameAssembly.dll only when a method body is needed.

Without just: `dotnet build module/GwentAccess.Module.csproj -c Release -p:Deploy=true [-p:GwentDir="..."]`.

The dev server (port 8773) exists only in Debug builds and only starts when `devserver.enable` is in the plugin folder (`just devserver-on`). Eval/main-thread work only runs once a `CrimsonInputLayer` is active (main menu), not during boot. Eval scripts cannot see the module's `internal` classes; reach them via reflection on `ModuleLoader.CurrentAssembly` or find game objects directly.

## Architecture

### Three compilation units
- **Host** (`src/GwentAccess.csproj` → `GwentAccess.dll`): only `src/Host` and `src/Dev`. Loaded by BepInEx and locked while the game runs. `Plugin` is the entry point, `ModuleLoader` byte-loads the module and swaps it, `MainThread` marshals work onto the game thread, `IModModule` is the module contract.
- **Module** (`module/GwentAccess.Module.csproj`): every other `.cs` under `src/`. Its **AssemblyName is timestamped per build on purpose** (generations coexist; the name tells them apart) — don't "fix" it. Deployed under the stable name `Module\GwentAccess.Module.dll`. `ModuleMain` is its entry: `Load` applies each `[HarmonyPatch]` class separately with a per-generation Harmony id and registers the screens; `Dispose` must undo everything (restore game keys first, then unpatch) so the next generation can take over. Don't use `ClassInjector`-registered types in the module (they can't be unloaded/reloaded).
- **Kernel** (`src/Access.Core/Graph`): compiled into the module, and separately by `tests/Access.Core.Tests` with **no** game/Unity/BepInEx references. It must stay game-agnostic (it is kept in sync with WH40KRTAccess); if it references game types the test project stops building. `GameReferences.props` is deliberately not a `Directory.Build.props` for this reason.

### Graph UI (`src/Ui`, namespace `GwentAccess.Ui` — not `Screens`, a legacy static class has that name)
- `Screen`: `IsActive()` re-derived from live game state each poll, a `Layer`, and an immediate-mode `Build(GraphBuilder)` called every render. Screens hold no tree or focus.
- `ScreenManager`: poll-and-diff stack each frame (no event subscriptions — the game destroys/rebuilds panels freely); top screen by Layer gets the navigator.
- `GraphNavigator`: one cursor per live screen, reconciled by `ControlId` after each rebuild; the differ is the **only** place a landing is announced. Writes the cursor back into the game's own selection (the mod drives the game's hover like a gamepad player would). IL2CPP wrappers are fresh per access, so identity matching must use structural path keys, not object references.
- `Keys` reads the Rewired keyboard device (synthesized repeat). `GameKeyBlock` arbitrates input by disabling the Rewired system player's keyboard ActionElementMaps for claimed keys — Rewired is the game's only input path. Escape stays the game's Back. Screens can leave keys to the game via `LeavesToGame`.
- Screens: `MainMenuScreen`, `SettingsScreen`, `BattleScreen`, `MulliganScreen`; `CardKeys` holds shared card-reading keys.
- **Press, hold and hover go through the game's mouse path, so their sounds play.** UI sound handlers (e.g. `RewardSystemTreeElementSoundHandler`) listen to a button's press state, its `HoldButtonController` progress and hover — which the mouse drives and the keyboard submit / `InvokeOnLeftClick` skip (the reward book's keyboard claim is instant and silent). Where a control has hold or press sounds, send it the game's own cursor events (`HandleCursorButtonEvent` with `CursorButtonEventArgs` ButtonDown on key press, ButtonUp on key release — see `RewardBookScreen.Press`). For hover, raise the control's own `OnHover` event on landing (`c.OnHover.Invoke(c)`, as the layer's pointer raycast does); selecting it, setting `ButtonState = Hovered` or calling `HandleControlHover()` don't reach the sound handlers. Verify by ear that the sounds a sighted mouse player hears also play.

### Legacy layer (being replaced)
`Hotkeys.cs` (background keyboard thread), `MenuSpeech.cs` (hooks `CrimsonInputLayer.set_SelectedControl`), `Navigator.cs` (F2/F3 object nav), `BattleSpeech`/`HandNav`, `DeckBuilder`, etc. still run. They must stand down when `GraphNavigator.Owns` (or `ScreenManager.AnyActive()` before this frame's poll) — one keypress, one model.

### Battle
- `Plays.cs` reads what the game is waiting on from its open requests (`RequestManager` per player: play-card, card targets, location targets, choice) — read-only.
- Moves go through the game's own gameplay flow (selection handlers, ghost-card placement, its A/B handlers, the pass widget), **not** the internal `Ask*` API, so animations, sounds and warning popups play.
- `CardReader.cs` is the generic card reader (battle, shop, packs, deck builder); `CardText.cs` is the older formatter.
- Parity rule: narrate only what a sighted player can see; give blind players no extra information.
- For battle testing use Practice vs AI: it runs locally with authority and no turn timer.

## Conventions

- Code comments are explanatory XML-doc style and cite spec sections; match that density.
- Speech reuses the game's own translation strings (`LocalizationManager.Instance.Translations`, looked up by key at runtime) wherever one exists, enriched only with what a sighted player can see (e.g. the scores beside the board). Mod-authored English is the fallback for things the game has no string for.
- Logs (`BepInEx\LogOutput.log`) record every spoken line and hotkey — check `just log` / `just speech` to verify behavior.
- `justfile` is not committed (excluded by the user's global gitignore); `.env` is local.
