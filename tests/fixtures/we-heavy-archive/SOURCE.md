# World Editor's own archive

Sven saved this map with World Editor (File > Save Map As, as a `.w3x` archive, not a folder) on 2026-10-26 at 22:26 as
`Documents\Warcraft III\Maps\ObjectEditorTest.w3x`, starting from Moonwell's template map (wc3-cs
`moonwell/template/MoonwellMap/map`). `ObjectEditorTest.w3x` is copied here unchanged (16634 bytes). This is actual
editor output.

It is **not** the map of `we-heavy`. It has no object edits and holds no object file at all (no `war3map.w3u` and so on,
no `war3mapSkin.*`): the plain folder save of the same template, below, has none either. Its `war3map.wts` is the
template's own, six entries (`STRING 1` "Moonwell Template" to `STRING 6` "Force 1"), not the longer one of `we-heavy`
(which goes up to `TRIGSTR_027`). What it is good for is the archive itself: the layout World Editor gives an archive, and
a save that adds the object files.

Compared with the plain folder save of the same template that Sven made earlier the same day
(`Documents\Warcraft III\Maps\ObjectEditorTest`, not committed), every file is equal byte for byte except two:
`war3map.w3i` (4 bytes differ, at offsets 8, 20, 24 and 25) and `war3mapUnits.doo` (8 bytes differ, at offsets 67 to 74,
`FF` here, `00` there). Everything the app reads, `war3map.wts` included, is equal.

The MPQ header is the first thing in the file: there is **no** 512-byte `HM3W` header before it (this World Editor writes
none to an archive saved this way), so the archive's header offset is 0. Block size 3 (the 4096-byte sectors War3Net's
default also uses). Every file is in the neutral locale and stored compressed (`CompressedMulti`); `(listfile)` and
`(attributes)` are also encrypted with the position-adjusted key. No `(signature)`.

| File | Size |
| --- | --- |
| `conversation.json` | 64 |
| `war3map.doo` | 24 |
| `war3map.lua` | 1564 |
| `war3map.mmp` | 24 |
| `war3map.shd` | 65536 |
| `war3map.w3c` | 8 |
| `war3map.w3e` | 33869 |
| `war3map.w3grp` | 12 |
| `war3map.w3i` | 375 |
| `war3map.w3l` | 22 |
| `war3map.w3r` | 8 |
| `war3map.wct` | 306 |
| `war3map.wpm` | 65552 |
| `war3map.wtg` | 125 |
| `war3map.wts` | 176 |
| `war3mapMap.blp` | 9463 |
| `war3mapUnits.doo` | 131 |
| `(listfile)` | 237 |
| `(attributes)` | 84 |

Sizes are the files' own, after decompression.
