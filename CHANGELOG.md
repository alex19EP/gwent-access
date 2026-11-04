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

### Battle

- Cards read as in Hearthstone Access. Landing on a card says the short line: name, power, statuses,
  provisions. Down then reads the abilities first, then categories, kind and base power, flavor and artist.
  The card preview uses the same order.
- A leader's landing is short ("Your leader, Emhyr var Emreis, Double Cross, order ready, 1 charge"). The
  player's name and title are its last Down line.
- Home and End go to the first and last card of the row you are in, not to the leader.
- Rows read their effects with turns left and say "full" at nine units ("Opponent's melee row, score 12,
  Frost, 2 turns left"). Number keys read the board from a choice too.
- A says the deck and graveyard counts for both players. Shift+T reads either player's turn timer and the
  mulligan timer, with warnings in the mulligan.
- Orders say their cooldown and counters ("order recharging, 2 turns", "counter 3"). A card glowing because
  its condition holds says so ("Dominance met"). Landing on a valid target says "valid target".
- Choices: the offered cards are read (created cards were read as "hidden card"). Tab hides the choices to look
  at the board ("Choices hidden.") and brings them back ("Choices shown.", then the card you were on). The
  title reads "Choose a card to play for Dazhbog Runestone".
- Special cards that need a confirmation say "Play X? Enter plays it, Backspace puts it back."
- New effect lines: Syndicate coins, reveals and Ambush flips, charges, orders ready again, Zeal. Destroyed
  cards read "Opponent's X destroyed by Y". Frost and other row effects name themselves as the damage source.
- A round's draws are one line ("You draw 3 cards: …. Opponent draws 3 cards."), before the mulligan.
  Later rounds' mulligans say only the round and swaps.
- Fixed: purify read "[MISSING]card_token", a stray "Opponent: hidden card", an opponent's Disloyal card read
  twice, a false "Not available right now", the first hand card read twice at the opening mulligan, and the
  leader read before "Place X" after a choice against a person.
- Emotes: M opens the emote wheel; Enter sends, the last item mutes the opponent. Your emote is read
  ("You: Well played.").
- The Versus banner reads both players once: name, title, rank. The results screen names the rank, the GG
  notification names its reward ("5 Scraps"), and Send Good Game says "sent".

### Menus

- New screens: matchmaking (T reads the time, Backspace cancels), the Journey and Autumn Cycle side windows,
  the Journey story with its pages, What's new, Leaderboards (Page Up and Page Down), gifting, the shop's product
  preview, the profile's avatar, border, title and showcase pickers, the Autumn Cycle reroll payment, the Draft
  lobby, wheel and summary, and the season transition. Card previews opened outside the collection are read.
- Slide-in notifications are spoken (contract completed, gifts, maintenance).
- PLAY opens on the mode last chosen, and choosing a mode reads its description.
- Profile contracts give their dates as the card draws them. The banner's name and avatar are one item.
- Reward Book nodes are named by their reward ("30 Ore, can claim, cost 1 Blood Drops, hold Enter to claim").
- Popups and panels name currency icons ("COST: -200 Scraps").
- Text fields: typing echoes, Backspace deletes (it used to close the deck rename popup), and Space types a
  space. The rename popup lists its name field.
- Deck builder: Backspace on the deck list exits; adding a card says the copies once; "1 card".
- The mod's own speech follows the game's language. Machine translations for Russian, German, French, Spanish,
  Polish, Brazilian Portuguese and Italian; corrections from native speakers are welcome (assets/lang).
- The controller is detected from the game's connect and disconnect events instead of a check every second.

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
