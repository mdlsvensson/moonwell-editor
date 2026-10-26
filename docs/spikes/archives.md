# Spike: War3Net's MPQ code on real maps

2026-10-26, for milestone 4 (archives). A throwaway console program, not committed, asked one question: can
`War3Net.IO.Mpq` open real maps and write a new archive that replaces one file while every other file comes back the
same? The version tested is `War3Net.IO.Mpq` 6.0.3, the one `Moonwell.War3Net.Build.Core 6.1.2-moonwell.4` brings
in. All work was on copies; no original map was written.

**Recommendation: War3Net.** It passed on every map that is not protected, as long as the save follows the rules
under "What the app's save must do". StormLib is not needed.

## How it was tested

The way the app would save: `MpqArchive.Open(path, loadListFile: true)`, add the file names the app knows
(`AddFileNames`), `new MpqArchiveBuilder(archive)`, `AddFile` the new `war3map.w3u` with the flags the old one had,
write any bytes from before the MPQ header first, then `SaveTo`. Then open the new archive again and compare every
file with the original by its hash (so unnamed files count too). Two comparisons per file:

- **decompressed bytes**: what the game reads;
- **stored bytes**: the raw block (position and size from the block table), compressed and maybe encrypted. Equal
  stored bytes mean the file was copied, not compressed again.

`war3map.w3u` was replaced (a) with the same bytes and (b) with changed bytes (the old bytes plus 19 more); when a map
had none, (b) added one.

## Step 1: Sample.w3x and (4)losttemple.w3m

| | Moonwell's `Sample.w3x` | Blizzard's `(4)losttemple.w3m` |
|---|---|---|
| Size | 196 KB, 21 files | 248 KB, 31 files |
| Header | `MPQ\x1A` at 0, block size 3, hash table 256 | `MPQ\x1A` at 0, block size 3, hash table 32 |
| Unnamed files | none | none |
| `war3map.w3u` | 32 bytes, flags `80000200` | absent |
| `(attributes)` | absent | CRC32 only |
| `(signature)` | absent | absent |
| After the archive | nothing | 260 bytes starting `NGIS` (Blizzard's strong signature) |

Results, both maps, both modes:

- Every file other than `(listfile)`, `(attributes)` and `(signature)` decompresses the same, keeps its flags, and
  **keeps its stored bytes** (20 of 20 and 29 of 29). Only the replaced `war3map.w3u` is new. With the same bytes it
  came out identical even stored.
- With changed bytes, `war3map.w3u` holds exactly the new bytes, with its old flags. On losttemple it was added with
  War3Net's default flags (`80000200`, zlib in blocks) and named in `(listfile)`.
- The hash table keeps its size; the block size stays 3.
- `(listfile)` is written anew: the same names, but in the order the files were written (the replaced file first),
  and an added file is added. `(attributes)` is written anew with CRC32s only (Sample gains one). On losttemple the
  CRC32s War3Net wrote agree with Blizzard's for all 29 files; only the one for `(listfile)` itself differs.
- **The 260-byte `NGIS` signature after losttemple's archive is gone.** War3Net writes only the archive. An edited
  map cannot keep a valid Blizzard signature anyway, so the copy is an unsigned map. What the game shows for an
  unsigned, edited Blizzard map was not checked.

Save times (open, build, write; warm): Sample 5-8 ms, losttemple 6-10 ms. Opening the new file again and reading every
file: 4-7 ms.

## Step 2: hand-made archives

No World Editor archive save exists yet, so these were built from `Sample.w3x`.

**(a) A 512-byte `HM3W` header in front.** The bytes before the header come back identical, and every file keeps its
stored bytes. But saving straight into the stream that already holds those 512 bytes (what `MpqArchive.Create` is
written to support) writes **two wrong fields in the MPQ header**: the header-size field becomes `0xFFFFFE20` instead
of `0x20`, and the archive size is 512 too large. Cause: War3Net's `MpqHeader` constructor computes
`DataOffset = 32 - headerOffset` and adds the pre-archive bytes into the archive size. War3Net reads its own output
back fine, and Storm is known to be lenient with these fields, but World Editor and the game were not tried on it.
Two ways round it, both checked:

- save the archive into a stream of its own and write `HM3W bytes + archive` (MPQ offsets are relative to the MPQ
  header, so this is valid as it is); or
- save in place and then write the two fields again: `0x20` at header + 4, `file length - header offset` at
  header + 8. The result is byte for byte the same as the first way (the two files differ in those 4 bytes only).

**(b) One file left out of `(listfile)`.** With `war3map.lua` left out, the app's own name list finds it: it is
carried as a named file and gains its line in `(listfile)` again. With `Moonwell\hello.txt` left out (a name nobody
knows), War3Net carries it as an unnamed file at its old hash slot, stored bytes unchanged; `(listfile)` does not list
it (nobody knows its name), which is as it was. Every other file keeps its stored bytes.

*Corrected after the final review (2026-10-26):* "at its old hash slot" is not enough for the game to find it.
War3Net leaves deleted entries (block index `0xFFFFFFFE`), entries that point at no block and `(signature)` out of
the hash table it writes, and gives an unnamed file no collision count. The game's lookup (Storm) starts at the slot
the name hashes to and steps forward until the first **empty** slot (it steps over deleted ones). When one of the
left-out entries lay between an unnamed file's home slot and its slot, that slot is now empty and the game stops short
of the file, while War3Net (whose lookup searches the whole table) and this spike's check still find it. The app's
save therefore writes a deleted entry into every slot that held something in the original and that War3Net left
empty, and its check looks every file up the way the game does (see rules 8 and 7 below).

**(c) The hardest case**: `HM3W` header, and two unnamed files encrypted with a key that depends on their position
(flag `BlockOffsetAdjustedKey`): a 30-byte one and the 673 KB `war3map.lua`. They have to move, because the new
`war3map.w3u` is written first. War3Net decrypted them and encrypted them again for the new position: both decompress
the same; their stored bytes necessarily differ. This took the save to about 20 ms.

**Block size.** `MpqArchiveBuilder.SaveTo(stream)` does not take the original's block size; it writes War3Net's
default, 3. On an archive with block size 4 that made 7 of 22 files be compressed again (same content, different
stored bytes). Passing `MpqArchiveCreateOptions { BlockSize = <the original's> }` kept all 22 stored bytes.

**A locked map.** `MpqArchive.Open(path)` maps the file into memory. The builder disposes the files it writes, but not
the one it left out (the replaced `war3map.w3u`): that file's view keeps the map mapped after the archive is
disposed. `File.Replace` still worked, but copying over the map failed ("a file with a user-mapped section open")
until `builder.OriginalFiles` were disposed too.

**A big map.** `Sample.w3x` plus a 100 MB imported file (53 MB on disk): save 213-231 ms, reopen and read every file
152 ms.

## Extra: every archive in Sven's Maps folder

To see more real maps than two, the same save (with the rules below) ran on copies of all 360 archives under
`Documents\Warcraft III\Maps`: AMAI's copies of the melee maps and 148 downloaded custom maps. 63 s in all.

- **356 passed**: every file decompresses the same, nothing missing, no flag changed, and every file that is not
  encrypted by position kept its stored bytes. That includes 4 maps with an `HM3W` header, 3 with block sizes other
  than 3 (13 and 15), 7 with unnamed files (up to 3,521 of them), and one whose position-encrypted files moved (48
  files, all decompress the same). For the 7 maps with unnamed files, "passed" means found by War3Net's lookup, not
  by the game's: whether the game could still find every one was not checked (see the correction under step 2 (b)).
- 133 maps had bytes after the archive (132 Blizzard `NGIS` signatures, one 16 KB of junk). All are dropped by a save.
- **4 failed, all protected RPG maps** (thousands of unnamed files, odd block sizes):
  - `CWG RPG 1.1.0.w3x`: 624 hash entries point at a block another entry also points at (a protector's trick). War3Net
    keeps one name per block, so **624 names were lost** in the save. This is the one real loss found.
  - 3 maps have 1 to 8 files War3Net cannot decompress (unknown encryption key, or unreadable for another reason).
    Their stored bytes came across unchanged, so the game would read them as before, but War3Net cannot prove it by
    decompressing.

Biggest saves: 89 MB in 0.8 s, 110 MB in 1.0 s, 485 MB (CWG) in 3.6 s.

## What the app's save must do

1. Read the bytes before the MPQ header from the original and write them first. Then either save the archive into a
   stream of its own and append it, or save in place and write the header-size and archive-size fields again (the
   second needs no copy of the archive in memory, which matters for maps of hundreds of MB).
2. Pass the original's block size (read from the header, 2 bytes at header + 14; War3Net's header is internal) in
   `MpqArchiveCreateOptions`, with `AttributesFlags.Crc32` as the builder's own default uses. The hash table size is
   kept by the builder.
3. Replace a file with `AddFile(file, oldFlags)` only. Do not call `RemoveFile` for it first: the builder's remove
   list filters the new file out too.
4. Call `AddFileNames` with the names the app knows before building, so files missing from `(listfile)` keep or regain
   their names.
5. Dispose `builder.OriginalFiles` after the save, and the original archive, before replacing the map.
6. Refuse to open any map where several hash entries share one block: War3Net would drop the extra names, and a
   protected map like that has nothing useful to show. (The spike said "open as read only"; the app refuses it at
   open with the reason.)
7. The check after the save should compare, per hash entry (not only per name): the same number of entries; stored
   bytes for every unchanged file not encrypted by position (stronger than decompressing, and it covers the files
   War3Net cannot decompress); decompressed bytes for the ones encrypted by position. It finds each entry in the new
   archive the way the game does, reading both hash tables itself: a named file from the slot its name hashes to,
   forward to the first empty slot; a file with no known name from the start of the run of non-empty slots that led
   to it in the original (its home is somewhere in that run), at its old slot or after it. A file the game would not
   find refuses the save.
8. Write a deleted entry into every hash-table slot that held something in the original (a file, a deleted entry, an
   entry that points at no block, `(signature)`) and that War3Net left empty, so every lookup the game made in the
   original still reaches its file.

## What changes in a save, beyond the files the user changed

- `(listfile)`: written anew; order changes, names of files not in the archive are dropped, added files are added.
- `(attributes)`: written anew, CRC32 only (a map that had timestamps or MD5s loses them). Allowed by the spec.
- `(signature)` and any bytes after the archive (Blizzard's `NGIS` strong signature, or a tool's junk) are dropped.
  A signature cannot stay valid after an edit. Decided (ruling 4): a save drops whatever follows the archive.
- The hash table gains a deleted entry wherever War3Net would leave a slot empty that the original filled (rule 8).
- Position-encrypted files that move are encrypted again: same content, different stored bytes. Any MPQ library would
  have to do this unless the file keeps its position.
- The MPQ header's fields are War3Net's (with the fix in rule 1).

## Not checked

- That World Editor and the game open an archive War3Net wrote with an `HM3W` header in front. War3Net's archives
  without one are game-proven (every Moonwell map is one). This belongs in milestone 4's check with Sven's World
  Editor archive save.
- Files stored under a locale other than neutral, and archives where two files' hashes collide: none of the maps had
  them knowingly; the check after the save would catch a loss. The app opens an object or strings file under another
  locale than the neutral one as read only (a save writes the neutral one, so it would add a second copy).
