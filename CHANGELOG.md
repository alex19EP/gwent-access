# Changelog

Each release lists what changed for players first, then for people building the mod. The release job on GitHub
uses the section for the tag it builds as the release notes, so keep each version under its own `## ` heading.

## 0.28.0

### New screens

- Autumn Cycle (the season cycle) has its own screen. You hear the cycle's level and dates, then every reward
  level ("Level 2, 40 Ore, next"), then the quests by difficulty, each with its Change Quest button, then the
  cycle's contract. The level track scrolls to follow you.
- Journey has its own screen. You hear your level and crowns, then all 100 steps, one row per step with the
  Standard reward and then the Premium one. Special steps add their Fast Travel buttons with prices. Enter on a
  reward previews it, and the preview is read.
- Draft has its own screen. Each step's three choices come first, read the way their hover shows them, then the
  draft's steps, then the buttons.
- The deck builder follows Accessible Arena's layout. The collection and your deck are card rows, and a deck's
  stats and the filters each have their own section. Right arrow opens a deck's options. Shift+Enter previews a
  card. Ctrl+Enter adds another copy. Page Up and Page Down move a grid row, and Ctrl+Page Up and Page Down jump
  by provision cost.
- The NEW DECK window reads the factions, says which one is chosen, and lists that faction's leader abilities
  under Basic and Advanced. Enter on an ability creates the deck.
- Every other panel (Leaderboards, What's new!, loading tips) is read top to bottom by a fallback screen.
  Items the game draws on one line form a row.

### Changes

- Backspace is Back on every screen. It presses the screen's own Back, Exit, Close or Cancel. Where there is
  nothing to go back to, it says so.
- Each section of the main menu is a Tab stop, and Up and Down move inside it. The menu starts at the top.
- Cards read the same way everywhere, as in Hearthstone Access. Left and Right move through the cards, and
  Up and Down read the focused card line by line.
- F1 help names the screen it opened over and lists that screen's keys.
- F8 turns the mod off and on. F9 says where you are.
- A button bar lists each button once. The shop no longer reads "Exit" and then "Back" for the same button.
- Profile opens on its open tab instead of EXIT.
- The Reward Book's two tabs with the base set's icon are told apart ("Base set" and "Base set, 2").
- The Friends window closes with Backspace or Escape. The friend search field says "Enter to type".

### For developers

- The older hotkey layer (the F2 and F3 object navigator, the selection narrator, the legacy battle keys) is
  gone. Every panel is a graph screen.
- `CrimsonNodes.BarButtons` reads a button bar. It keeps only the buttons the bar registers, so icon twins drop
  out.
- `RewardText.Of` names a reward in the game's words.
- F11 loads a newer module build in Debug builds. `just stage` copies one without loading it.
- Tests: 60 kernel tests, including an item that speaks its own position inside a row.

## 0.27.0

The version on `main` before these changes.
