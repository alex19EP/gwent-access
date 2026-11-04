# Translations of the mod's own speech

The game's own text (menus, cards, keywords) always comes in the game's language. These files translate only
what the mod itself says: screen names, key help (F1), battle narration, summaries, "selected", "1 of 5"…

Each file maps the English line to its translation:

    "You draw {0} cards: {1}.": "Вы берёте карты ({0}): {1}.",

- The file name is the game's language code: `de-de`, `es-es`, `es-mx`, `fr-fr`, `it-it`, `ja-jp`, `ko-kr`,
  `pl-pl`, `pt-br`, `ru-ru`, `zh-cn`. A language without its own file uses another of the same language
  (`es-mx` reads `es-es.json`). Missing file or line: English.
- Keep every `{0}`, `{1}` placeholder: it is filled in when the line is spoken (a number, a card name, a list).
  Move it wherever your language needs it.
- A key with a `|` is a count: `"{0} card|{0} cards"`. Give your language's forms separated by `|`, in CLDR
  order: one|few|many for Russian, Ukrainian and Polish; one|few|other for Czech; one|other for German, French,
  Spanish, Italian, Portuguese; a single form for Japanese, Korean, Chinese.
- Entries whose name starts with `_` are notes, not lines.
- The mod speaks the game's language unless `Speech.Language` in `BepInEx\config\gwentaccess.cfg`
  (`thronebreakeraccess.cfg`) names a file (`de-de`) or `en`.

## Adding or updating a language

1. Run `python scripts/extract-strings.py` (or `just strings`). It rewrites `en.json` from the source code and
   prints, per language file, how many lines are missing, stale (no longer in the mod) or have different
   placeholders. Add `-v` to list them, `--where` to see where each line is used.
2. Copy `en.json` to `<code>.json` (or add the missing lines to an existing file) and translate the values.
3. `just deploy` copies the folder to `BepInEx\plugins\<Mod>\lang`; `just reload` too. A Debug build logs each
   line it couldn't find (`[loc] no de-de for: …`) once.

The files other than `en.json` were machine-translated (see each file's `_note`): corrections by native
speakers are welcome.
