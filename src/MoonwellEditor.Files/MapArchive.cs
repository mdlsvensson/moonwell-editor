using War3Net.IO.Mpq;

namespace MoonwellEditor.Files;

/// <summary>
/// A map stored as an MPQ archive, a <c>.w3x</c> or <c>.w3m</c>, read with War3Net. Opening reads what the app needs
/// into memory (the bytes before the MPQ header, the names of the files, the object files and <c>war3map.wts</c>) and
/// closes the file, so World Editor is never kept from the map by the app.
/// </summary>
public sealed class MapArchive : IMapStore
{
    /// <summary>
    /// Every file name the app knows a map may have: the object and strings files, the map's other standard files and
    /// the archive's own. War3Net is told them all, so a file missing from the archive's <c>(listfile)</c> still has
    /// its name.
    /// </summary>
    internal static readonly IReadOnlyList<string> KnownNames =
    [
        .. MapStore.ObjectAndStringFiles,
        "war3map.w3i", "war3map.w3e", "war3map.wpm", "war3map.doo", "war3mapUnits.doo", "war3map.shd",
        "war3map.mmp", "war3map.w3r", "war3map.w3c", "war3map.w3s", "war3map.wtg", "war3map.wct", "war3map.j",
        "scripts\\war3map.j", "war3map.lua", "war3map.imp", "war3mapMap.blp", "war3mapMap.tga", "war3mapPreview.tga",
        "war3mapPath.tga", "war3mapMisc.txt", "war3mapSkin.txt", "war3mapExtra.txt", "war3map.w3o",
        "conversation.json", "(listfile)", "(attributes)", "(signature)",
    ];

    // The MPQ header's id, "MPQ\x1A", and its size; the header starts at a multiple of 512 bytes.
    private static readonly byte[] HeaderId = [(byte)'M', (byte)'P', (byte)'Q', 0x1A];
    private const int HeaderSize = 32;
    private const int HeaderAlign = 512;

    private Contents _contents;

    private MapArchive(string path, Contents contents)
    {
        Path = path;
        _contents = contents;
    }

    /// <summary>The archive file's full path.</summary>
    public string Path { get; }

    /// <summary>True: this is an archive.</summary>
    public bool IsArchive => true;

    /// <summary>
    /// The bytes before the MPQ header (World Editor's 512-byte <c>HM3W</c> header); empty when there are none.
    /// </summary>
    internal byte[] PreArchive => _contents.PreArchive;

    /// <summary>
    /// The names of the archive's files that have one (from its <c>(listfile)</c> or <see cref="KnownNames"/>).
    /// </summary>
    internal IReadOnlyList<string> Names => _contents.Names;

    /// <summary>For tests: runs on the new archive's file after it is written and before the check reads it.</summary>
    internal Action<string>? BeforeCheck { get; set; }

    /// <summary>
    /// <see cref="File.Replace(string, string, string?)"/>, which puts the new archive in the map's place (the old one
    /// kept under the backup name); tests make it fail half way.
    /// </summary>
    internal Action<string, string, string?> ReplaceFile { get; set; } = File.Replace;

    /// <summary>
    /// Opens a map archive and reads what the app needs from it, then closes it. Throws
    /// <see cref="MapArchiveException"/> (naming the file and the reason) when War3Net cannot read it, or when several
    /// of its hash entries share one file's data (a protector's trick: a save would lose the extra names).
    /// </summary>
    public static MapArchive Open(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        return new MapArchive(full, Load(full, MapStore.ObjectAndStringFiles));
    }

    /// <summary>
    /// A file's bytes as the archive on disk holds them, or null when it has no such file. The object and strings
    /// files come from memory while the archive's <see cref="Stamp"/> is the one they were read with; when it changed,
    /// the archive is read again. Any other file is read from the archive when asked for.
    /// </summary>
    public byte[]? ReadImport(string path)
    {
        var name = path.Trim().Replace('/', '\\').TrimStart('\\');
        return name.Length == 0 ? null : Read(name);
    }

    /// <inheritdoc />
    public byte[]? Read(string name)
    {
        if (Stamp() != _contents.Stamp)
        {
            _contents = Load(Path, MapStore.ObjectAndStringFiles);
        }

        if (_contents.Files.TryGetValue(name, out var bytes))
        {
            return bytes;
        }

        return Load(Path, [name]).Files[name];
    }

    /// <summary>
    /// Why the object or strings file <paramref name="name"/> may not be written, or null when it may: the archive
    /// holds it under a locale other than the neutral one (a save, which writes the neutral one, would add a second
    /// copy beside it). As read with the files (see <see cref="Read"/>).
    /// </summary>
    public string? WriteProblem(string name) => _contents.Problems.GetValueOrDefault(name);

    /// <summary>
    /// Saves files into the archive. A new archive is built from the one on disk with War3Net's
    /// <see cref="MpqArchiveBuilder"/>: each given file replaced (with the flags its old entry had; a new file with
    /// War3Net's default), every other file carried over as it is stored, the bytes before the MPQ header written
    /// first. It is written to <c>&lt;archive&gt;.saving</c> beside the map, opened again and checked against the old
    /// one, and only then replaces the map (the old one kept as <c>&lt;archive&gt;.replacing</c> until that is done).
    /// A failure leaves the map as it was, deletes the new file and throws <see cref="MapArchiveException"/> saying
    /// why; when the replace fails half way, the map's path is never left empty, and the message says whether the old
    /// map or the new one is there (<see cref="MapArchiveException.Saved"/> is true for the new one). A
    /// <c>&lt;archive&gt;.replacing</c> left by an earlier save cut short that cannot be deleted refuses the save
    /// before anything is written. A delete (null bytes) is refused: a save never removes a file from an archive. War3Net
    /// writes <c>(listfile)</c> and <c>(attributes)</c> anew and drops <c>(signature)</c> and any bytes after the
    /// archive (a signature cannot stay valid after an edit); a hash-table slot that held something in the old archive
    /// keeps a deleted entry where War3Net left it empty, so the game finds every file as before. With
    /// <paramref name="stamp"/>, a map whose <see cref="Stamp"/> is another one once the save holds it (World Editor
    /// saved it after the caller checked) is not saved (<see cref="MapArchiveException.ChangedOnDisk"/>).
    /// </summary>
    public void Write(IReadOnlyList<(string Name, byte[]? Bytes)> files, string? stamp = null)
    {
        var written = new List<(string Name, byte[] Bytes)>();
        foreach (var (name, bytes) in files)
        {
            if (bytes is null)
            {
                throw MapArchiveException.NotSaved(Path, Texts.Get("app.archive.wouldRemove", ("name", name)));
            }

            written.Add((name, bytes));
        }

        var saving = Path + ".saving";
        var replacing = Path + ".replacing";

        // A backup left by an earlier save cut short is stale: the map itself was just read. One that cannot be
        // deleted would make the replace fail, so the save is refused before anything is written.
        if (File.Exists(replacing))
        {
            try
            {
                File.Delete(replacing);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw MapArchiveException.NotSaved(
                    Path, Texts.Get("app.archive.staleBackup", ("file", replacing), ("error", e.Message)), e);
            }
        }

        try
        {
            Save(written, saving, stamp);
        }
        catch (Exception e)
        {
            Delete(saving);
            if (e is MapArchiveException)
            {
                throw;
            }

            throw IsLocked(e) ? MapArchiveException.Locked(Path, e) : MapArchiveException.NotSaved(Path, e.Message, e);
        }

        // Held while it was read, the map could still have its time set; and it is not held between the check and
        // the replace. Its stamp is taken once more, just before.
        if (stamp is not null && StampOf(Path) != stamp)
        {
            Delete(saving);
            throw MapArchiveException.Changed(Path);
        }

        Delete(replacing);
        try
        {
            ReplaceFile(saving, Path, replacing);
        }
        catch (Exception e)
        {
            throw AfterFailedReplace(e, saving, replacing);
        }

        Delete(replacing);
    }

    // File.Replace can fail half way (Win32 error 1177: the map moved to the backup name, the new archive not moved
    // in; or, as without a backup, the map gone and the new archive still under its own name). The map's path is
    // never left empty: the old map is put back from the backup, or else the new (checked) archive is moved in. Only
    // then are the leftovers deleted. The exception says which of the two the map now is.
    private MapArchiveException AfterFailedReplace(Exception e, string saving, string replacing)
    {
        bool saved;
        if (File.Exists(Path))
        {
            // The new archive gone from its own name means it is the map now.
            saved = !File.Exists(saving);
        }
        else
        {
            try
            {
                if (File.Exists(replacing))
                {
                    File.Move(replacing, Path);
                    saved = false;
                }
                else if (File.Exists(saving))
                {
                    File.Move(saving, Path);
                    saved = true;
                }
                else
                {
                    return MapArchiveException.WithMessage(Path, e, Texts.Get(
                        "app.archive.missing",
                        ("path", Path), ("error", e.Message), ("replacing", replacing), ("saving", saving)));
                }
            }
            catch (Exception moving) when (moving is IOException or UnauthorizedAccessException)
            {
                return MapArchiveException.WithMessage(Path, e, Texts.Get(
                    "app.archive.missingPutBack",
                    ("path", Path), ("error", e.Message), ("moving", moving.Message), ("replacing", replacing),
                    ("saving", saving)));
            }
        }

        Delete(saving);
        Delete(replacing);
        if (saved)
        {
            return MapArchiveException.SavedWithError(
                Path, e, Texts.Get("app.archive.savedWithError", ("path", Path), ("error", e.Message)));
        }

        return IsLocked(e) ? MapArchiveException.Locked(Path, e) : MapArchiveException.NotSaved(Path, e.Message, e);
    }

    /// <summary>
    /// The archive file's write time (UTC, in ticks) and its length, as <c>ticks:length</c>; <c>missing</c> when the
    /// file is gone. A different stamp means the archive changed on disk.
    /// </summary>
    public string Stamp() => StampOf(Path);

    private static string StampOf(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? $"{file.LastWriteTimeUtc.Ticks}:{file.Length}" : "missing";
    }

    // Deletes a file when it can; a failed save reports why it failed, not that its new file could not be deleted.
    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Writes the new archive to saving and checks it against the archive at Path (see Write).
    private void Save(List<(string Name, byte[] Bytes)> written, string saving, string? stamp)
    {
        // Read only, and nobody may write the map while it is read; from here on its stamp cannot change.
        using var source = OpenRead(Path, FileShare.Read);
        if (stamp is not null && StampOf(Path) != stamp)
        {
            throw MapArchiveException.Changed(Path);
        }

        using var original = MpqArchive.Open(source, loadListFile: true);
        MpqArchiveBuilder? builder = null;
        try
        {
            original.AddFileNames(KnownNames);
            original.AddFileNames(written.Select(file => file.Name));
            var pre = ReadPreArchive(source);
            var blockSize = ReadUInt16(source, pre.Length + 14);

            builder = new MpqArchiveBuilder(original);
            foreach (var (name, bytes) in written)
            {
                // AddFile alone, with no RemoveFile first: the builder's remove list would filter out the new file too.
                var file = MpqFile.New(new MemoryStream(bytes), name);
                if (OldFlags(original, name) is { } flags)
                {
                    builder.AddFile(file, flags);
                }
                else
                {
                    builder.AddFile(file);
                }
            }

            using (var target = new FileStream(saving, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                target.Write(pre);

                // The original's block size (SaveTo would write War3Net's default, and compress files again); the
                // builder keeps the hash table's size.
                var options = new MpqArchiveCreateOptions
                {
                    BlockSize = blockSize,
                    AttributesFlags = AttributesFlags.Crc32,
                };
                builder.SaveTo(target, options, leaveOpen: true);

                // Behind bytes before the header, War3Net writes two of the header's fields wrong: the header's size
                // (32 minus the bytes before it) and the archive's size (those bytes counted in). Both are set again.
                WriteUInt32(target, pre.Length + 4, HeaderSize);
                WriteUInt32(target, pre.Length + 8, (uint)(target.Length - pre.Length));
                KeepLookups(source, target, pre.Length);
            }

            BeforeCheck?.Invoke(saving);
            Check(original, source, pre, written, saving);
        }
        finally
        {
            // The builder disposes the files it wrote, not the ones it left out (each replaced file's old entry).
            if (builder is not null)
            {
                foreach (var file in builder.OriginalFiles)
                {
                    file.Dispose();
                }
            }
        }
    }

    // The game looks a file up from the hash-table slot its name hashes to, forward to the first empty slot (it steps
    // over deleted entries). War3Net leaves the original's deleted entries, its entries that point at no block and
    // its (signature) out of the table it writes, and puts a file with no known name back at its old slot, so a slot
    // one of those filled on the way to such a file would now stop the game short of it. Every slot that held
    // something in the original and is empty in the new table gets a deleted entry: the game steps over it as it did
    // before. Tables of different sizes are left; the check refuses one when it matters.
    private static void KeepLookups(Stream original, Stream target, long header)
    {
        var old = StormHashTable.Read(original, header);
        var now = StormHashTable.Read(target, header);
        if (old.Length != now.Length)
        {
            return;
        }

        var changed = false;
        for (var slot = 0; slot < old.Length; slot++)
        {
            if (!old[slot].IsEmpty && now[slot].IsEmpty)
            {
                now[slot] = StormHashTable.Slot.Deleted;
                changed = true;
            }
        }

        if (changed)
        {
            StormHashTable.Write(target, header, now);
        }
    }

    // The flags of a file's entry in the archive (the neutral locale's, as a new file is written), or null when the
    // archive has no such file.
    private static MpqFileFlags? OldFlags(MpqArchive archive, string name)
    {
        var hashed = MpqHash.GetHashedFileName(name);
        foreach (var hash in archive.EnumerateHashes())
        {
            if (hash.Name == hashed && hash.Locale == MpqLocale.Neutral && hash.BlockIndex < archive.Count)
            {
                return archive[(int)hash.BlockIndex].Flags & ~MpqFileFlags.Garbage;
            }
        }

        return null;
    }

    // Opens the new archive at saving and compares it with the original, slot by slot in the original's hash table
    // (so files with no known name count too), read as the file holds it. Every file of the original is found in the
    // new archive the way the game looks files up (see StormHashTable: a named file from the slot its name hashes to,
    // one with no known name from wherever the game may have started for it) with the same flags and the same stored
    // bytes (decompressed bytes for a file encrypted by its position, which moves); each written file holds its new
    // bytes; the bytes before the MPQ header are the same; and there are no other entries. (listfile), (attributes)
    // and (signature) are left out: War3Net writes them anew. Throws MapArchiveException on the first difference.
    private void Check(
        MpqArchive original,
        Stream originalStream,
        byte[] pre,
        List<(string Name, byte[] Bytes)> written,
        string saving)
    {
        using var stream = OpenRead(saving, FileShare.Read);
        using var saved = MpqArchive.Open(stream, loadListFile: true);
        saved.AddFileNames(KnownNames);
        saved.AddFileNames(written.Select(file => file.Name));
        for (var i = 0; i < original.Count; i++)
        {
            if (original[i].FileName is { } name)
            {
                saved.AddFileName(name);
            }
        }

        if (!ReadPreArchive(stream).AsSpan().SequenceEqual(pre))
        {
            throw CheckFailed(Texts.Get("app.archive.checkBefore"));
        }

        var oldTable = StormHashTable.Read(originalStream, pre.Length);
        var newTable = StormHashTable.Read(stream, pre.Length);
        var specials = new[] { "(listfile)", "(attributes)", "(signature)" }
            .Select(MpqHash.GetHashedFileName)
            .ToHashSet();
        var writtenBytes = new Dictionary<ulong, (string Name, byte[] Bytes)>();
        foreach (var file in written)
        {
            writtenBytes[MpqHash.GetHashedFileName(file.Name)] = file;
        }

        var expectedCount = 0;
        for (var slot = 0; slot < oldTable.Length; slot++)
        {
            // A hash entry that points at no block is no file; War3Net leaves it out.
            var entry = oldTable[slot];
            if (!entry.IsLive || entry.Block >= original.Count || specials.Contains(entry.Name))
            {
                continue;
            }

            expectedCount++;
            if (entry.Locale == 0 && writtenBytes.ContainsKey(entry.Name))
            {
                continue;   // checked below, with the written files
            }

            var old = original[(int)entry.Block];
            var label = old.FileName ?? Texts.Get("app.archive.unnamed", ("hash", entry.Name.ToString("X16")));
            int found;
            if (old.FileName is { } name)
            {
                found = StormHashTable.Find(newTable, name, entry.Locale, saved.Count);
            }
            else if (newTable.Length != oldTable.Length)
            {
                throw CheckFailed(Texts.Get(
                    "app.archive.checkSlots", ("now", newTable.Length), ("old", oldTable.Length), ("file", label)));
            }
            else
            {
                found = StormHashTable.FindUnnamed(oldTable, newTable, slot, saved.Count);
            }

            if (found < 0)
            {
                throw CheckFailed(Texts.Get("app.archive.checkNotFound", ("file", label)));
            }

            var now = saved[(int)newTable[found].Block];
            if ((old.Flags & ~MpqFileFlags.Garbage) != (now.Flags & ~MpqFileFlags.Garbage))
            {
                throw CheckFailed(Texts.Get("app.archive.checkFlags", ("file", label)));
            }

            bool same;
            if (old.Flags.HasFlag(MpqFileFlags.Encrypted | MpqFileFlags.BlockOffsetAdjustedKey))
            {
                // One War3Net cannot decrypt (its key unknown) on both sides was kept where it was: compare it as
                // stored.
                var before = Decompressed(original, old);
                var after = Decompressed(saved, now);
                same = before is null && after is null
                    ? SameStored(originalStream, old, stream, now)
                    : before is not null && after is not null && before.AsSpan().SequenceEqual(after);
            }
            else
            {
                same = SameStored(originalStream, old, stream, now);
            }

            if (!same)
            {
                throw CheckFailed(Texts.Get("app.archive.checkNotSame", ("file", label)));
            }
        }

        foreach (var (name, bytes) in writtenBytes.Values)
        {
            if (!OldFlags(original, name).HasValue)
            {
                expectedCount++;
            }

            var found = StormHashTable.Find(newTable, name, 0, saved.Count);
            if (found < 0)
            {
                throw CheckFailed(Texts.Get("app.archive.checkNotFound", ("file", name)));
            }

            if (Decompressed(saved, saved[(int)newTable[found].Block]) is not { } after
                || !after.AsSpan().SequenceEqual(bytes))
            {
                throw CheckFailed(Texts.Get("app.archive.checkBytes", ("file", name)));
            }
        }

        var savedCount = newTable.Count(entry => entry.IsLive && !specials.Contains(entry.Name));
        if (savedCount != expectedCount)
        {
            throw CheckFailed(Texts.Get("app.archive.checkCount", ("count", savedCount), ("expected", expectedCount)));
        }
    }

    private MapArchiveException CheckFailed(string reason) =>
        MapArchiveException.NotSaved(Path, Texts.Get("app.archive.checkFailed", ("reason", reason)));

    // A file's bytes, decompressed; null when War3Net cannot read them.
    private static byte[]? Decompressed(MpqArchive archive, MpqEntry entry)
    {
        try
        {
            using var stream = archive.OpenFile(entry);
            if (!stream.CanRead)
            {
                return null;
            }

            var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    // Whether two entries' stored bytes (their blocks as the files hold them) are the same, read a piece at a time.
    private static bool SameStored(Stream oldStream, MpqEntry old, Stream newStream, MpqEntry now)
    {
        if (old.CompressedSize != now.CompressedSize)
        {
            return false;
        }

        var oldPiece = new byte[64 * 1024];
        var newPiece = new byte[oldPiece.Length];
        long left = old.CompressedSize;
        long done = 0;
        while (left > 0)
        {
            var size = (int)Math.Min(left, oldPiece.Length);
            oldStream.Position = old.FilePosition + done;
            oldStream.ReadExactly(oldPiece, 0, size);
            newStream.Position = now.FilePosition + done;
            newStream.ReadExactly(newPiece, 0, size);
            if (!oldPiece.AsSpan(0, size).SequenceEqual(newPiece.AsSpan(0, size)))
            {
                return false;
            }

            done += size;
            left -= size;
        }

        return true;
    }

    // Whether an exception says another program holds the file: a sharing or lock violation, a mapped view, or
    // File.Replace unable to remove the map (which leaves both files as they were).
    private static bool IsLocked(Exception e)
    {
        const int SharingViolation = 32, LockViolation = 33, CannotRemoveReplaced = 1175, UserMappedFile = 1224;
        return e is IOException
            && (e.HResult & 0xFFFF) is SharingViolation or LockViolation or CannotRemoveReplaced or UserMappedFile;
    }

    // War3Net memory-maps an archive it is given as a FileStream, and a view it leaves behind (after a failed open, or
    // a file it cannot read) keeps the map from other programs. Through a BufferedStream it reads the file plainly.
    private static BufferedStream OpenRead(string path, FileShare share) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, share), 64 * 1024);

    private static ushort ReadUInt16(Stream stream, long position)
    {
        var bytes = new byte[2];
        stream.Position = position;
        stream.ReadExactly(bytes);
        return BitConverter.ToUInt16(bytes);
    }

    private static void WriteUInt32(Stream stream, long position, uint value)
    {
        stream.Position = position;
        stream.Write(BitConverter.GetBytes(value));
    }

    // Reads the archive at path: its stamp (taken first, so a change made while reading is seen later), the bytes
    // before the MPQ header, the names of its files, and the bytes of the files named (null for one it does not have).
    private static Contents Load(string path, IReadOnlyList<string> names)
    {
        var stamp = StampOf(path);
        using var stream = OpenRead(path, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            using var archive = MpqArchive.Open(stream, loadListFile: true);
            archive.AddFileNames(KnownNames);

            var shared = archive.EnumerateHashes().GroupBy(hash => hash.BlockIndex).Count(block => block.Count() > 1);
            if (shared > 0)
            {
                throw new MapArchiveException(path, Texts.Get("app.archive.shared", ("count", shared)));
            }

            var files = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                files[name] = ReadFile(archive, name);
            }

            var listed = new List<string>();
            for (var i = 0; i < archive.Count; i++)
            {
                if (archive[i].FileName is { } name)
                {
                    listed.Add(name);
                }
            }

            return new Contents(stamp, ReadPreArchive(stream), listed, files, Localized(archive));
        }
        catch (Exception e) when (e is not MapArchiveException)
        {
            throw new MapArchiveException(path, e.Message, e);
        }
    }

    // The object and strings files the archive holds under a locale other than the neutral one, each with why it may
    // not be written: a save writes a file under the neutral locale, so it would add a second copy beside that one.
    private static Dictionary<string, string> Localized(MpqArchive archive)
    {
        var problems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in MapStore.ObjectAndStringFiles)
        {
            var hashed = MpqHash.GetHashedFileName(name);
            var locales = archive.EnumerateHashes()
                .Where(hash => hash.Name == hashed && hash.Locale != MpqLocale.Neutral)
                .Where(hash => hash.BlockIndex < archive.Count)
                .Select(hash => $"0x{(uint)hash.Locale:X4}")
                .ToList();
            if (locales.Count > 0)
            {
                problems[name] = Texts.Get(
                    "app.archive.locale",
                    ("name", name), ("locales", string.Join(Texts.Get("app.archive.and"), locales)));
            }
        }

        return problems;
    }

    // A file's bytes, decompressed, or null when the archive has no file of that name. A file War3Net cannot read
    // throws an exception that names it.
    private static byte[]? ReadFile(MpqArchive archive, string name)
    {
        try
        {
            if (!archive.TryOpenFile(name, out var stream))
            {
                return null;
            }

            using (stream)
            {
                if (!stream.CanRead)
                {
                    throw new InvalidDataException(Texts.Get("app.archive.cannotDecompress"));
                }

                var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new InvalidDataException(Texts.Get("app.archive.cannotReadFile", ("name", name), ("error", e.Message)), e);
        }
    }

    // The bytes before the MPQ header, found the way War3Net finds it: the first multiple of 512 that holds its id.
    private static byte[] ReadPreArchive(Stream stream)
    {
        var id = new byte[HeaderId.Length];
        for (long offset = 0; offset + HeaderSize <= stream.Length; offset += HeaderAlign)
        {
            stream.Position = offset;
            stream.ReadExactly(id);
            if (id.AsSpan().SequenceEqual(HeaderId))
            {
                var bytes = new byte[offset];
                stream.Position = 0;
                stream.ReadExactly(bytes);
                return bytes;
            }
        }

        throw new InvalidDataException(Texts.Get("app.archive.noHeader"));
    }

    /// <summary>What <see cref="Load"/> read from the archive.</summary>
    /// <param name="Stamp">The archive's <see cref="MapArchive.Stamp"/> before reading.</param>
    /// <param name="PreArchive">The bytes before the MPQ header.</param>
    /// <param name="Names">The names of the files that have one.</param>
    /// <param name="Files">
    /// The files read, by name (letter case ignored); null for one the archive does not have.
    /// </param>
    /// <param name="Problems">Why an object or strings file may not be written, by name (letter case ignored).</param>
    private sealed record Contents(
        string Stamp,
        byte[] PreArchive,
        IReadOnlyList<string> Names,
        Dictionary<string, byte[]?> Files,
        Dictionary<string, string> Problems);
}

/// <summary>
/// A map archive the app cannot open (War3Net cannot read it, a damaged or protected map, or it is a protected map a
/// save would damage), or a save of one that failed and left the map as it was; or, with <see cref="Saved"/>, a save
/// that put the new archive in place but reported an error on the way. The message says why.
/// </summary>
public sealed class MapArchiveException : Exception
{
    /// <summary>The archive at <paramref name="path"/> cannot be opened, for <paramref name="reason"/>.</summary>
    public MapArchiveException(string path, string reason, Exception? inner = null)
        : this(path, inner, Texts.Get("app.archive.cannotOpen", ("path", path), ("reason", reason)))
    {
    }

    private MapArchiveException(
        string path, Exception? inner, string message, bool saved = false, bool changedOnDisk = false)
        : base(message, inner)
    {
        Path = path;
        Saved = saved;
        ChangedOnDisk = changedOnDisk;
    }

    /// <summary>The archive file's path.</summary>
    public string Path { get; }

    /// <summary>
    /// True only for a save whose new archive passed the check and ended up at the map's path although replacing the
    /// old one reported an error: the map is saved. False for every other failure (the map is as it was).
    /// </summary>
    public bool Saved { get; }

    /// <summary>
    /// True for a save refused because the map's stamp was not the one the caller checked once the save held the map
    /// (another program wrote it meanwhile); the map is as that program left it.
    /// </summary>
    public bool ChangedOnDisk { get; }

    /// <summary>A save of the archive at <paramref name="path"/> failed, for <paramref name="reason"/>.</summary>
    internal static MapArchiveException NotSaved(string path, string reason, Exception? inner = null) =>
        new(path, inner, Texts.Get("app.archive.notSaved", ("path", path), ("reason", reason)));

    /// <summary>A save refused because the map changed on disk (<see cref="ChangedOnDisk"/> is true).</summary>
    internal static MapArchiveException Changed(string path) =>
        new(path, null, Texts.Get("app.archive.changed", ("path", path)), changedOnDisk: true);

    /// <summary>A save that ended otherwise, with a message that says how.</summary>
    internal static MapArchiveException WithMessage(string path, Exception inner, string message) =>
        new(path, inner, message);

    /// <summary>
    /// A save whose checked new archive is in the map's place, although replacing the old one reported an error
    /// (<see cref="Saved"/> is true).
    /// </summary>
    internal static MapArchiveException SavedWithError(string path, Exception inner, string message) =>
        new(path, inner, message, saved: true);

    /// <summary>A save failed because another program holds the archive.</summary>
    internal static MapArchiveException Locked(string path, Exception inner) =>
        new(path, inner, Texts.Get("app.archive.locked"));
}
