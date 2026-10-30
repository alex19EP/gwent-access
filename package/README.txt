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
5. When you hear "Gwent Access loaded", you're ready.

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
To turn it back on, run Install.bat again.


KEYS EVERYWHERE
F3 / F2          Next / previous item on the screen. Moves the game's selection there.
Shift+F3 / F2    Jump to the next / previous section (top bar, main area, buttons...).
F4               Click the current item.
Enter            Choose (the game's own Accept). Escape: back / cancel.
Arrow keys       The game's own movement. Left/Right also change a setting's value.
F1               Full details of the selected card: abilities, categories, faction,
                 rarity, what each keyword means, and flavor text.
F6               What keys work on this screen.
F7               Repeat the last thing spoken.
Shift+F7         Repeat the last popup, tutorial or screen message. Press again
                 (within 5 seconds) to hear older ones, up to 15 back.
F8               Read the current screen.
F9               Check that the mod is running.
F5               Switch the game between controller and mouse mode (leave it on
                 controller mode; that is what the mod is built around).
Ctrl+Q           Press the screen's Exit or Back button. In the deck editor: save and exit.

MENUS
- Popups are read with their choices and keys. In popups, Enter is always Accept and
  Escape is always Cancel, no matter which choice is highlighted.
- Settings: Shift+F3 jumps between Categories, Options and Buttons. Left/Right arrow
  changes a value. Remember to press Apply Settings.
- Daily rewards popup: press F3 until "Click anywhere to continue", then F4.
- Shop: pick an item (a keg or pack), then its price option (like "x1 for 100 Ore"),
  then press Ctrl+B. The mod asks you to press Ctrl+B again to confirm. It only buys
  with Ore, and refuses anything that costs real money.


PLAYING A MATCH
Gwent is best of three rounds. Each turn you play ONE card, then end your turn.
When a player passes, they are out for the rest of that round; the other player can
keep playing. The round ends when both have passed, and the higher score wins it.

Ctrl+Right / Left   Pick a card from your hand (and select it on the board).
Enter               Pick up the selected card. Then arrows choose a row, Enter places it.
Escape              While holding a card: put it back.
Ctrl+E              End your turn (needed after you play a card).
Ctrl+P, twice       Pass. The first press tells you the score so you can think again.
Ctrl+M              At the start of a round: swap the selected card in your hand.
Ctrl+S              Score, round, rounds won, whose turn, passes, cards in hand.
Ctrl+H              Read your hand.
Ctrl+1 / Ctrl+2     Your melee / ranged row.
Ctrl+3 / Ctrl+4     Opponent's melee / ranged row.
Ctrl+Up / Down      Step through the opponent's cards on the board.
Ctrl+L              Both leaders.
Ctrl+G              Your deck and graveyard counts.

The mod announces: your turn and the opponent's turn, every card the opponent plays,
passes, new rounds, what you played and where, when a card needs a target, turn timer
warnings (20 seconds and 8 seconds left), and the end of the match.

Forfeit protection: in a match, Escape opens the "forfeit?" question and Enter accepts
it, which is easy to do by accident. The mod blocks the first forfeit and tells you.
If you really want to forfeit, do it again within 15 seconds.

"Card placement not ideal" warning: Enter places the card anyway; Escape ONCE cancels.


DECK BUILDING
Main menu: F3 to DECK BUILDER, F4.

On your deck list:
Ctrl+U   Duplicate the selected deck (starter decks can't be edited; edit a copy).
Ctrl+N   Rename the selected deck. Type the new name and press Enter.
         Names can use letters, numbers, spaces, dashes and underscores.
Enter    Open the selected deck.
Ctrl+Q   Leave the deck builder.

While editing a deck:
Shift+F3     Jump between Your deck, Collection, Filters and Other buttons.
Ctrl+A       Add the selected collection card to your deck.
Ctrl+R       Remove the selected card from your deck.
Ctrl+D       Deck stats: cards (25 minimum), units (13 minimum), provisions used and limit.
Ctrl+H       List the whole deck.
Ctrl+F       Jump to the card search box. Type part of a card name.
Ctrl+N       Rename this deck.
Ctrl+Q       Save and exit. If the game won't let you leave, press Ctrl+Q again
             within 8 seconds to leave without saving. In a starter deck it just leaves.

Provisions are the deck-building budget: 150 plus your leader ability's bonus.
Every card costs provisions; stronger cards cost more.


TROUBLESHOOTING
- No speech at all: make sure NVDA is running before you start Gwent. Press F9.
  If nothing, the mod didn't load; check BepInEx\LogOutput.log in the Gwent folder.
- Speech stopped mid-game: press F7 to repeat, F8 to read the screen.
- Stuck on a screen: Ctrl+Q, then Escape. Alt+F4 closes the game if all else fails.
- Reporting a problem: send BepInEx\LogOutput.log from your Gwent folder, right after
  the problem happens (it is replaced each time you start the game).


CREDITS AND LICENSES
Gwent Access: written by Claude (Anthropic) together with smoovgunner, a blind player
who did all of the testing by ear. Source code is included in the GwentAccess-source zip.
Uses BepInEx 6 (LGPL 2.1, https://github.com/BepInEx/BepInEx), HarmonyX and
Il2CppInterop, and Prism for speech (MPL 2.0, https://github.com/ethindp/prism).
Gwent is a trademark of CD PROJEKT RED. This is an unofficial fan mod.
