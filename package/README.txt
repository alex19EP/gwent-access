GWENT ACCESS
A screen reader mod for Gwent: The Witcher Card Game (Steam, Windows)
Version 0.27

Gwent Access makes Gwent playable with the NVDA screen reader. It reads the menus,
cards, popups and the battle board, and adds keyboard commands for playing a match
and building decks. It was built and tested by a blind player, by ear, from scratch.


WHAT YOU NEED
- Windows 10 or 11, 64-bit.
- Gwent: The Witcher Card Game installed from Steam.
- NVDA running (tested with NVDA 2026.2). Speech goes through Prism, which also
  supports other screen readers such as JAWS; those are untested.
- A GOG account linked to Gwent. Gwent's launcher and login screens are NOT made
  accessible by this mod. You may need sighted help once to log in and accept the
  user agreement. After that, the Gwent Accessible shortcut skips the launcher.


INSTALLING
1. Close Gwent completely.
2. Unzip this package anywhere, for example your Downloads folder.
3. Run Install.bat. If Gwent is in the usual Steam folder, it copies everything for you:
   C:\Program Files (x86)\Steam\steamapps\common\GWENT The Witcher Card Game
   If Windows says access is denied, right-click Install.bat and choose
   "Run as administrator".
   If your Steam library is somewhere else, copy every file and folder from this
   package (except Install.bat, Uninstall.bat and README.txt) into your Gwent folder,
   the folder that contains Gwent.exe.
4. Start Gwent. The FIRST start after installing takes several minutes with no speech
   while the mod prepares itself. That is normal. Later starts are quick.
5. When you hear "Gwent Access ... loaded. Press F1 for help.", you're ready.

IMPORTANT: If your Gwent folder contains a file called winhttp.dll, rename it to
winhttp.dll.off. Some older mod setups use that file, and it breaks Gwent's login
("cannot connect to server"). This mod uses version.dll instead. Install.bat does
this rename for you.


SKIPPING THE LAUNCHER (recommended after your first login)
Gwent normally opens the REDlauncher window first, which is not accessible.
Install.bat puts a shortcut called "Gwent Accessible" on your desktop. It starts Gwent
directly and skips the launcher. Steam must be running first.
If the shortcut is missing, press Windows+R and run (with the quote marks):
"C:\Program Files (x86)\Steam\steamapps\common\GWENT The Witcher Card Game\Gwent-NoLauncher.bat"
The very first time, you may still need to log in through the launcher with sighted
help so your GOG account is connected. After that, use the shortcut.


UPDATING (testers)
Close Gwent, then run Update.bat from your Gwent folder (Install.bat copies it there).
It downloads the newest test build and installs it over yours, keeping your settings.
At the end it reads out which build you now have; include that when reporting a problem.


UNINSTALLING
Run Uninstall.bat. It turns the mod off by renaming version.dll to version.dll.off.
The mod's files stay in your Gwent folder. To turn it back on, run Install.bat again.


KEYS EVERYWHERE
F1               Help: the keys for the screen you're on, then the keys that work
                 everywhere. Up and Down to move through it, F1 or Escape to close.
Arrow keys       Move around the screen. Left/Right also change a setting's value.
Tab / Shift+Tab  Next / previous section.
Enter            Use the current item. Escape: the game's own back / menu.
Backspace        Back: the screen's Back, Exit or Close (Cancel in a popup). Where an item has
                 a Backspace action of its own (a deck's options, a card's preview, cancelling
                 in a match), that comes first. Says "Nothing to go back to here" otherwise.
Space            Details of the current item, or read it again.
F7               Repeat the last thing spoken.
Shift+F7         Repeat the last popup, tutorial or screen message. Press again
                 (within 5 seconds) to hear older ones, up to 15 back.
F9               Where am I: the current item again, with its screen and section.
F8               Accessibility on/off ("Accessibility off" / "on"): off, the mod is
                 silent and gives the game every key.
F5               Switch controller mode by hand. The mod already switches it on
                 when a controller is connected and off when there is none.

MENUS
- Popups read their message first. Enter on the message presses the popup's default
  button (Accept, OK...); arrow to another button and Enter presses that one instead.
  Escape is the game's own cancel.
- Main menu: Tab / Shift+Tab move between its sections: the game modes, the opponent
  (VERSUS AI or VERSUS PLAYER, once you are in PLAY), your deck, the menu bar (quests,
  options, quit...), your profile and currencies, and any notifications. Up/Down move through the items of the section you are in. In the
  game modes and your deck, Left/Right also change the mode or the deck. A letter
  jumps to the next item starting with it, and opens it when it is the only one
  (Quit is only ever reached, never opened, this way). You start on PLAY.
- Settings: Up/Down go through the categories. Enter opens one and takes you to its
  options; Apply, Restore Defaults, Exit and Back follow the options. Left/Right change
  a value. Escape or Backspace in the options goes back to the categories; Escape on
  the categories closes settings. Remember to press Apply Settings.
- Daily rewards: Left/Right go through the days, Up/Down read a card reward, Tab
  reaches the buttons.
- Shop: pick a category, then a product (a keg, a pack...). Its price options and its
  details follow it: title, time left, description, quantity and the Buy button.
  Enter on Buy goes through the game's own purchase, with its confirmation popup.
  Every price says its currency; real-money prices carry their symbol.
- Draft: each step's three choices come first, under the step's title ("Choose
  ability"): an ability's name and text, a package's name and its cards (Space reads
  every card in full), a card. Enter picks it. Tab reaches the draft's steps (done,
  with what you picked; current; to come), then the buttons. Once the deck is done,
  the screen lists your wins, Play (starts a match against a player) and your deck.
  EXIT leaves and keeps your draft; Abandon ends it.
- Other screens (Journey, Leaderboards, What's new!, the season cycle, loading tips):
  read top to bottom with Up/Down. Items the game draws on one line (a leaderboard
  entry's rank, name and rating; column headings) are a row: Left/Right move along it.
  A journey step says its number, reward and whether it is locked or claimed. Tab
  reaches the buttons at the bottom.

PLAYING A MATCH
Gwent is best of three rounds. Each turn you play ONE card, then end your turn.
When a player passes, they are out for the rest of that round; the other player can
keep playing. The round ends when both have passed, and the higher score wins it.

Moving around the board works by zones, as in Hearthstone Access: Left/Right move
through the cards of the row you are in, Up/Down read the focused card line by line
(name and power, categories, ability, keywords, flavor; "End of card" at the end), and
the keys below change the row. The same goes for every screen of cards: the mulligan,
card choices, your deck and graveyards, rewards and kegs.

C / Shift+C         Your hand / the opponent's hand size.
B / Shift+B         Your melee / ranged row.
G / Shift+G         The opponent's melee / ranged row.
V / F               Your leader / the opponent's leader.
1, 2, 3, 4          Jump to your melee, your ranged, the opponent's melee, the
                    opponent's ranged row. Shift+number reads that row aloud.
Enter               Use the card: pick it up from your hand (then arrows choose a spot
                    and Enter places it), use a ready order (your leader's ability),
                    choose a target.
Tab / Shift+Tab     Next / previous card you can use, target or row.
Backspace           Cancel (put a card back, stop targeting).
E (hold)            End your turn, or pass when you have played. Letting go early
                    cancels. It tells you the score as you start holding.
A                   Score, round and passes.
T / Shift+T         Whose turn it is (and who has passed) / time left in your turn.
D                   Your deck. Y: your graveyard. Shift+Y: the opponent's graveyard.
O                   Battle log: everything said this match, newest first.
Space               The whole card at once.
I                   What the card's keywords mean. K: card kind and base power.
F1                  Every key for the screen you're on.

Before the match the mod reads the Versus screen (both players' names and titles),
then who goes first.

The mod announces: your turn and the opponent's turn, every card the opponent plays,
passes, new rounds, what you played and where, when a card needs a target, turn timer
warnings (20 seconds and 8 seconds left), and the end of the match.

Forfeit protection: in a match, Escape opens the "forfeit?" question and Enter accepts
it, which is easy to do by accident. The mod blocks the first forfeit and tells you.
If you really want to forfeit, do it again within 15 seconds.

"Card placement not ideal" warning: Enter places the card anyway; Escape ONCE cancels.


DECK BUILDING
Main menu: Tab to the game modes or menu bar, then DECK BUILDER, Enter.

Tab / Shift+Tab move between the sections: your decks (or, while editing, the deck you
are editing), the card collection, the filters, the side filters and the buttons.

On your deck list: Enter opens a deck. Backspace shows its options (rename, change
ability, share, duplicate, delete), which follow it in the list ("options shown").
Starter decks can't be edited; duplicate one and edit the copy.

While editing a deck:
- The deck section comes first: the totals the game checks ("25 cards, minimum 25;
  22 units, minimum 13; provisions 130 of 165"), then the leader ability, the
  stratagem and each card ("Geralt of Rivia, power 3, 10 provisions", "x2" for two
  copies). Enter on a card takes it out ("Removed Geralt of Rivia", then the totals).
- In the collection, Enter adds the card to the deck you are editing ("Added Geralt of
  Rivia, 1 in deck", then the totals). Backspace opens the card's preview (the game's right click); Escape
  closes it. Page Up / Page Down move a row of the grid.
- Ctrl+F    Search cards. Type part of a card name.
- The buttons section has the game's Exit (it asks whether to save) and Exit without
  saving.

Provisions are the deck-building budget: 150 plus your leader ability's bonus.
Every card costs provisions; stronger cards cost more.


OPTIONS
The mod's options are in BepInEx\config\gwentaccess.cfg in the Gwent folder (open it
with Notepad; it is created the first time the game starts with the mod). Changes apply
while the game runs.
TimerWarnings    true / false: say "20 seconds left" and "8 seconds left" in your turn.
OpponentCardText true / false: read what the opponent's played card does after its name.

TROUBLESHOOTING
- No speech at all: make sure NVDA is running before you start Gwent. Press F8: if you hear
  "Accessibility on", it had been switched off.
  If nothing, the mod didn't load; check BepInEx\LogOutput.log in the Gwent folder.
- Speech stopped mid-game: press F7 to repeat, F9 for where you are.
- Stuck on a screen: Escape, or Tab to the screen's buttons. Alt+F4 closes the game if
  all else fails.
- Reporting a problem: send BepInEx\LogOutput.log from your Gwent folder, right after
  the problem happens (it is replaced each time you start the game).


CREDITS AND LICENSES
Gwent Access: written by Claude (Anthropic) together with smoovgunner, a blind player
who did all of the testing by ear. Source code is included in the GwentAccess-source zip.
Uses BepInEx 6 (LGPL 2.1, https://github.com/BepInEx/BepInEx), HarmonyX and
Il2CppInterop, Prism for speech (MPL 2.0, https://github.com/ethindp/prism), NAudio
(Ms-PL, https://github.com/naudio/NAudio) and sounds from WrathAccess (zlib). The full
notices are in BepInEx\plugins\GwentAccess\THIRD-PARTY-NOTICES.md.
Gwent is a trademark of CD PROJEKT RED. This is an unofficial fan mod.
