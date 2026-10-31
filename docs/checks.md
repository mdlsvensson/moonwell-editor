# Checks in World Editor and the game

Checks a person made by hand, newest first. Each names the game version and what was looked at.

## 2026-10-31: the card layout survives a save in World Editor

Game and World Editor 3.0.1.24371. Object Editor branch `trigger-tree-drag` at fa1d0db.

A fresh copy of Lost Temple got `Card Check` and a moved card in the window, whose save names
`war3map.triggerlayout.json` in `war3map.imp` with flag 13. Sven opened it in World Editor, which lists the file in its
Import Manager, then saved and closed it. The file came back with the same text and the import with the same flag. The
window shows the cards at their places, and a card moved after that saves. **Passed.**

## 2026-10-31: a trigger save from the window opens in World Editor, whose save drops the card layout

Game and World Editor 3.0.1.24371. Object Editor branch `trigger-tree-drag` at f4635c4.

A copy of Lost Temple got a trigger, `Card Check`, with a Time Of Day event and a moved card, in the window with real mouse
input, and was saved from there. Sven opened it in World Editor: it opens with no error, and `Card Check` is under
Initialization with its event. **Passed.**

Sven then saved it in World Editor. That save drops `war3map.triggerlayout.json`, which no `war3map.imp` entry names, so
card places and folds are lost and the cards stand in columns again. It keeps the ids of the category and the triggers.
Its script differs from the one the window wrote only in what is not triggers: `ChooseRandomItemExWithFilter` in the drop
tables, `SetPlayerRaceSkin` and `SetHDWaterParamsEx`.

Not seen in the game: this save. The one below was made by a small program, through the same code.

## 2026-10-31: the app's trigger save runs in the game

Game 3.0.1.24371. The same map as the World Editor check below. `War3Log.txt` shows the game opening it.

Sven played it. The game loads the spliced `war3map.j`, and about a second in shows the name of a placed unit no trigger had
named before (`Ice Troll Berserker`, from `gg_unit_nits_0047`, set after the line that makes it) and `Custom script ran`,
from a custom script item called from a Custom Script action. Every 5 seconds it shows `Moonwell trigger ran`, then `Else
block` the first time and `Then block, loop` twice each time after: a variable, an If with conditions and both blocks and
a multiple-action loop. **Passed.**

Single player only. Not seen in the game: a script from a save in the window itself, and a map larger than this one.

## 2026-10-31: the app's trigger save opens right in World Editor

Game and World Editor 3.0.1.24371. Object Editor branch `triggers`, War3Net `6.1.2-moonwell.4`.

A copy of Lost Temple (`Maps/FrozenThrone/(4)LostTemple.w3x` of the install) got a category with a variable, two triggers,
a comment and a custom script through the session's trigger edits and save, from a small program, not the window: the code
the window calls. Only `war3map.wtg`, `war3map.wct` and `war3map.j` were written.

Sven opened it in World Editor: it opens with no error, the new category holds all five items, `Moonwell Tick` reads as made
with its If block and loop, and `Melee Initialization` is as it was. **Passed.**

## 2026-10-26: the app's archive save opens right in World Editor (milestone 4)

Game and World Editor 3.0.1.24371. Object Editor branch `archives`, War3Net `6.1.2-moonwell.4` (MPQ code from
`War3Net.IO.Mpq` 6.0.3).

Sven saved Moonwell's template map in World Editor as an archive (`tests/fixtures/we-heavy-archive`, see its
`SOURCE.md`: no object files, no `HM3W` header). The app opened a copy,
`Documents\Warcraft III\Maps\ObjectEditorTest-AppArchive.w3x`, made these edits through its bridge and saved. The save
added `war3map.w3u`, `war3mapSkin.w3u` and `war3map.w3a`; every file of World Editor's archive came back with the same
bytes except `(listfile)` and `(attributes)`, which a save rewrites. The backup equals the original.

- Footman (`hfoo`): hit points 777, name "Archive Footman" (inline, in `war3mapSkin.w3u`).
- Holy Light (`AHhb`): amount healed/damaged 123 on level 2.

Sven opened the copy in World Editor: it opens with no error and both edits show, levels 1 and 3 unchanged.
**Passed.**

Not seen yet: the game loading an archive the app saved, and World Editor or the game reading a save that had to write
deleted entries where War3Net emptied hash slots.

## 2026-10-26: the app's save opens right in World Editor (milestone 2)

Game and World Editor 3.0.1.24371. Object Editor branch `browse-and-edit`, War3Net `6.1.2-moonwell.4`.

Sven saved a map in World Editor with edits on every tab (`tests/fixtures/we-heavy`, see its `SOURCE.md`). The app
opened a copy, `Documents\Warcraft III\Maps\ObjectEditorTest-AppSaved.w3m`, made these edits through its bridge and
saved. Only `war3map.w3u`, `war3mapSkin.w3u`, `war3map.w3d`, `war3map.w3a` and `war3map.wts` changed:

- Footman (`hfoo`): hit points 999 → 1234, gold cost 77.
- New unit `h001` from the Footman: name "App Footman" (inline), 555 hit points.
- Siege Engine (`hmtt`): its `TRIGSTR` name changed in `war3map.wts` to "Renamed By The App".
- Ability `A000`: amount healed 100, 200, 300 on levels 1-3.
- Doodad `D000`: blue tint 64 on variation 2.
- Upgrade `R000`: level 2 tooltip "APP LEVEL 2 TOOLTIP" (its `TRIGSTR` entry in `war3map.wts`).

Sven opened the copy in World Editor: every edit shows, and every object and change of his own save is as he made it.
**Passed.**

The check in the game was left out (Sven, 2026-10-26): World Editor reading the files, the byte-for-byte round trip of
World Editor's own files, and Moonwell maps written through the same War3Net loading in the game cover it.
