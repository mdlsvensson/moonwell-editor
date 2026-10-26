# Checks in World Editor and the game

Checks a person made by hand, newest first. Each names the game version and what was looked at.

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
