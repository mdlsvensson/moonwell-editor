# Moonwell build of its Sample map: an archive written by War3Net

Copied unchanged on 2026-10-26 from wc3-cs `moonwell/tests/Moonwell.Build.Tests/Fixtures/Sample/build/Sample.w3x`
(Moonwell's build test fixture, built on 2026-10-25 from moonwell `v0.2.5-115-g7b97ed8`; the build folder is not in
Moonwell's git). Moonwell packs it with War3Net: an MPQ archive with the header at 0 (no `HM3W` header in front), block
size 3, a hash table of 256, and 21 files: 20 named in `(listfile)` (among them `war3map.w3u`, `war3mapSkin.w3u`,
`war3map.wts`, `war3map.lua` and `Moonwell\hello.txt`) and `(listfile)` itself; no `(attributes)` or `(signature)`.
See `docs/spikes/archives.md` for what War3Net does with it.

SHA-256:

```
42be935106a2c6b431299cca1af074d9e4ad0cb195372576291e12311e3575c1  Sample.w3x
```
