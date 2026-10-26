using War3Net.IO.Mpq;
using Xunit.Abstractions;

namespace MoonwellEditor.Files.Tests;

/// <summary>
/// Reading map archives (<c>.w3x</c>, <c>.w3m</c>), and the map store that opens folders and archives.
/// </summary>
public sealed class MapArchiveTests : IDisposable
{
    /// <summary>The environment variable that names the game data folder (war3.w3mod).</summary>
    private const string GameDataVariable = "MOONWELL_EDITOR_GAME_DATA";

    private readonly ITestOutputHelper _output;

    // A temporary folder for this test's copies; the fixtures themselves are never written.
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "MoonwellEditor.Files.Tests", Guid.NewGuid().ToString("N"));

    public MapArchiveTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, true);
    }

    /// <summary>Moonwell's Sample.w3x, next to the test assembly.</summary>
    private static string Sample => Path.Combine(Fixtures.Root, "moonwell-sample", "Sample.w3x");

    /// <summary>The archive World Editor saved of the unedited template map (see its SOURCE.md).</summary>
    private static string WorldEditorArchive =>
        Path.Combine(Fixtures.Root, "we-heavy-archive", "ObjectEditorTest.w3x");

    [Fact]
    public void Reads_the_files_of_an_archive()
    {
        var path = Copy(Sample, "Sample.w3x");
        var map = MapArchive.Open(path);

        Assert.Equal(path, map.Path);
        Assert.True(map.IsArchive);
        Assert.Equal(War3NetReads(path, "war3map.w3u"), map.Read("war3map.w3u"));
        Assert.Equal(32, map.Read("war3map.w3u")!.Length);
        Assert.Equal(War3NetReads(path, "war3map.wts"), map.Read("war3map.wts"));
        Assert.Equal(War3NetReads(path, "war3mapSkin.w3u"), map.Read("war3mapSkin.w3u"));
        Assert.Null(map.Read("war3map.w3o"));
        Assert.Null(map.Read("war3mapSkin.w3a"));

        // A file the app does not read at open is read when asked for.
        Assert.Equal(War3NetReads(path, "Moonwell\\hello.txt"), map.Read("Moonwell\\hello.txt"));
        Assert.Null(map.Read("no such file.txt"));

        // What was read at open: no bytes before the MPQ header, and the names of the files.
        Assert.Empty(map.PreArchive);
        Assert.Contains("war3map.lua", map.Names);
        Assert.Contains("Moonwell\\hello.txt", map.Names);

        // Behind a 512-byte header, as World Editor saves (the archive's offsets count from the MPQ header).
        var header = new byte[512];
        "HM3W"u8.CopyTo(header);
        var behind = Path.Combine(_folder, "behind.w3x");
        File.WriteAllBytes(behind, [.. header, .. File.ReadAllBytes(path)]);
        var behindMap = MapArchive.Open(behind);
        Assert.Equal(header, behindMap.PreArchive);
        Assert.Equal(map.Read("war3map.w3u"), behindMap.Read("war3map.w3u"));
    }

    [Fact]
    public void Store_opens_folders_and_archives()
    {
        var folder = MapStore.Open(Path.Combine(Fixtures.Root, "we-3.0"));
        Assert.IsType<MapFolder>(folder);
        Assert.False(folder.IsArchive);
        Assert.Equal(Fixtures.Bytes("we-3.0/war3map.w3u"), folder.Read("war3map.w3u"));

        var archive = MapStore.Open(Sample);
        Assert.IsType<MapArchive>(archive);
        Assert.True(archive.IsArchive);
        Assert.Equal(War3NetReads(Sample, "war3map.w3u"), archive.Read("war3map.w3u"));

        Assert.Throws<FileNotFoundException>(() => MapStore.Open(Path.Combine(_folder, "missing.w3x")));

        // A file is opened as a map only when it ends .w3x or .w3m (letter case ignored).
        Assert.IsType<MapArchive>(MapStore.Open(Copy(Sample, "UPPER.W3X")));
        Assert.IsType<MapArchive>(MapStore.Open(Copy(Sample, "melee.w3m")));
        foreach (var name in new[] { "Sample.w3x.bak", "Sample.zip", "Sample" })
        {
            var other = Copy(Sample, name);
            var problem = Assert.Throws<InvalidDataException>(() => MapStore.Open(other));
            Assert.Equal($"Not a map: {other} is a file, but not a .w3x or .w3m map archive.", problem.Message);
        }
    }

    [Fact]
    public void Unreadable_archive_is_a_problem()
    {
        var bytes = File.ReadAllBytes(Sample);
        var half = Path.Combine(_folder, "half.w3x");
        File.WriteAllBytes(half, bytes[..(bytes.Length / 2)]);
        var noise = Path.Combine(_folder, "noise.w3x");
        var random = new byte[1000];
        new Random(1).NextBytes(random);
        File.WriteAllBytes(noise, random);

        foreach (var path in new[] { half, noise })
        {
            var problem = Assert.Throws<MapArchiveException>(() => MapArchive.Open(path));
            Assert.Contains(path, problem.Message);
            Assert.Equal(path, problem.Path);
            Assert.Throws<MapArchiveException>(() => MapStore.Open(path));
            AssertFree(path);
        }
    }

    [Fact]
    public void The_file_is_free()
    {
        // After opening and reading: another program may take the file for itself, and even cut it short.
        var opened = Copy(Sample, "opened.w3x");
        var map = MapArchive.Open(opened);
        Assert.NotNull(map.Read("Moonwell\\hello.txt"));
        AssertFree(opened);

        // After an open that failed on a file War3Net cannot read; the message names that file.
        var damaged = Copy(Sample, "damaged.w3x");
        Spoil(damaged, "war3map.wts", middle: true);
        var problem = Assert.Throws<MapArchiveException>(() => MapArchive.Open(damaged));
        Assert.Contains("war3map.wts", problem.Message);
        AssertFree(damaged);

        // After a save.
        var saved = Copy(Sample, "saved.w3x");
        MapArchive.Open(saved).Write([("war3map.w3u", [1, 2, 3])]);
        AssertFree(saved);
    }

    [Fact]
    public void Save_changes_only_the_written_files()
    {
        var path = Copy(Sample, "Sample.w3x");
        var before = Entries(path);
        var map = MapArchive.Open(path);
        byte[] w3u = [.. map.Read("war3map.w3u")!, .. "changed"u8];
        map.Write([("war3map.w3u", w3u)]);

        var after = Entries(path);
        AssertCarried(before, after, "war3map.w3u");
        var written = after.Single(entry => entry.FileName == "war3map.w3u");
        Assert.Equal(w3u, written.Data);
        Assert.Equal(before.Single(entry => entry.FileName == "war3map.w3u").Flags, written.Flags);
        Assert.Equal(w3u, map.Read("war3map.w3u"));
        Assert.False(File.Exists(path + ".saving"));
        Assert.False(File.Exists(path + ".replacing"));

        // The MPQ header's sizes: 32 bytes of header, and the archive is the whole file.
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(32u, BitConverter.ToUInt32(bytes, 4));
        Assert.Equal((uint)bytes.Length, BitConverter.ToUInt32(bytes, 8));

        // A file the archive did not have is added, with War3Net's default flags.
        byte[] skin = [9, 8, 7];
        map.Write([("war3mapSkin.w3a", skin)]);
        var later = Entries(path);
        AssertCarried(after, later, "war3mapSkin.w3a");
        var added = later.Single(entry => entry.FileName == "war3mapSkin.w3a");
        Assert.Equal(skin, added.Data);
        Assert.Equal(MpqFileFlags.Exists | MpqFileFlags.CompressedMulti, added.Flags);
        Assert.Equal(skin, map.Read("war3mapSkin.w3a"));
    }

    [Fact]
    public void Replaced_file_keeps_its_flags()
    {
        // war3map.w3u stored as one unit and encrypted, not as War3Net would write a new file.
        var flags = MpqFileFlags.Exists | MpqFileFlags.CompressedMulti | MpqFileFlags.SingleUnit
            | MpqFileFlags.Encrypted;
        var path = Path.Combine(_folder, "flags.w3x");
        Rebuild(Sample, path, flags: ("war3map.w3u", flags));
        var before = Entries(path);
        Assert.Equal(flags, before.Single(entry => entry.FileName == "war3map.w3u").Flags);

        byte[] w3u = [1, 2, 3, 4];
        MapArchive.Open(path).Write([("war3map.w3u", w3u)]);

        var after = Entries(path);
        AssertCarried(before, after, "war3map.w3u");
        var written = after.Single(entry => entry.FileName == "war3map.w3u");
        Assert.Equal(flags, written.Flags);
        Assert.Equal(w3u, written.Data);
    }

    [Fact]
    public void Pre_archive_header_is_kept()
    {
        var header = new byte[512];
        "HM3W"u8.CopyTo(header);
        "Test Map\0"u8.CopyTo(header.AsSpan(8));
        header[511] = 0x5A;
        var path = Path.Combine(_folder, "behind.w3x");
        File.WriteAllBytes(path, [.. header, .. File.ReadAllBytes(Sample)]);
        var before = Entries(path);

        var map = MapArchive.Open(path);
        map.Write([("war3map.w3u", [1, 2, 3])]);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(header, bytes[..512]);
        Assert.Equal("MPQ\x1A"u8.ToArray(), bytes[512..516]);

        // War3Net writes these two fields wrong behind a header (32 - 512, and 512 too many): the save sets them again.
        Assert.Equal(32u, BitConverter.ToUInt32(bytes, 512 + 4));
        Assert.Equal((uint)(bytes.Length - 512), BitConverter.ToUInt32(bytes, 512 + 8));

        AssertCarried(before, Entries(path), "war3map.w3u");
        Assert.Equal(header, MapArchive.Open(path).PreArchive);
        Assert.Equal([1, 2, 3], map.Read("war3map.w3u"));
    }

    [Fact]
    public void Unnamed_files_survive()
    {
        // Moonwell\hello.txt left out of (listfile): a name nobody knows, so War3Net carries it by its hash alone.
        const string unlisted = "Moonwell\\hello.txt";
        var path = Path.Combine(_folder, "unlisted.w3x");
        Rebuild(Sample, path, unlisted: unlisted);
        var hash = MpqHash.GetHashedFileName(unlisted);
        var before = Entries(path);
        Assert.Null(before.Single(entry => entry.Name == hash).FileName);
        Assert.DoesNotContain(unlisted, MapArchive.Open(path).Names);

        MapArchive.Open(path).Write([("war3map.w3u", [1, 2, 3])]);

        var after = Entries(path);
        AssertCarried(before, after, "war3map.w3u");
        var kept = after.Single(entry => entry.Name == hash);
        Assert.Null(kept.FileName);
        Assert.Equal(before.Single(entry => entry.Name == hash).Stored, kept.Stored);
        Assert.Equal(War3NetReads(Sample, unlisted), War3NetReads(path, unlisted));
    }

    [Fact]
    public void Unnamed_file_stays_where_the_game_finds_it()
    {
        // Moonwell\hello.txt unnamed, and found by the game (from the slot its name hashes to, forward to the first
        // empty slot) only past three entries War3Net leaves out of the table it writes: a deleted entry, a
        // (signature) and an entry that points at no block. War3Net puts the file back at its old slot.
        const string unlisted = "Moonwell\\hello.txt";
        var path = Path.Combine(_folder, "chain.w3x");
        Rebuild(Sample, path, unlisted: unlisted, signature: true);
        var dropped = ChainPastDroppedEntries(path, unlisted);
        var copy = Copy(path, "chain-copy.w3x");
        Assert.True(GameFinds(path, unlisted));
        var before = Entries(path);

        // The save keeps the chain (a deleted entry where each left-out one was), so the game still finds it.
        MapArchive.Open(path).Write([("war3map.w3u", [1, 2, 3])]);
        Assert.True(GameFinds(path, unlisted));
        AssertCarried(before, Entries(path), "war3map.w3u");
        Assert.Equal(War3NetReads(Sample, unlisted), War3NetReads(path, unlisted));

        // With those slots empty, as War3Net writes them, the game could not find it: the check refuses the save.
        var bytes = File.ReadAllBytes(copy);
        var map = MapArchive.Open(copy);
        map.BeforeCheck = saving => ChangeHashTable(saving, table =>
        {
            foreach (var slot in dropped.Where(slot => !table[slot].IsLive))
            {
                table[slot] = StormHashTable.Slot.Empty;
            }
        });
        var problem = Assert.Throws<MapArchiveException>(() => map.Write([("war3map.w3u", [1, 2, 3])]));
        Assert.Contains($"{MpqHash.GetHashedFileName(unlisted):X16}", problem.Message);
        Assert.Contains("the way the game looks files up", problem.Message);
        Assert.False(problem.Saved);
        Assert.Equal(bytes, File.ReadAllBytes(copy));
        Assert.False(File.Exists(copy + ".saving"));
    }

    [Fact]
    public void Failed_check_leaves_the_map()
    {
        // What the check is made to see in the new archive, and the file its message names.
        var spoilers = new (Action<string> Spoil, string Named)[]
        {
            (saving => Spoil(saving, "war3map.lua", middle: true), "war3map.lua"),
            (saving => Spoil(saving, "war3map.w3u", middle: false), "war3map.w3u"),
            (saving => Spoil(saving, "Moonwell\\hello.txt", middle: false), "Moonwell\\hello.txt"),
            (saving => AddFile(saving, "extra.txt"), "files where"),
            (saving => MoveOutOfReach(saving, "war3map.lua"), "war3map.lua"),
        };

        foreach (var (spoil, named) in spoilers)
        {
            var path = Path.Combine(_folder, "Sample.w3x");
            File.Copy(Sample, path, true);
            var time = new DateTime(2001, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, time);
            var bytes = File.ReadAllBytes(path);

            var map = MapArchive.Open(path);
            map.BeforeCheck = spoil;
            var problem = Assert.Throws<MapArchiveException>(() => map.Write([("war3map.w3u", [1, 2, 3])]));

            Assert.Contains(path, problem.Message);
            Assert.Contains(named, problem.Message);
            Assert.False(problem.Saved);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(time, File.GetLastWriteTimeUtc(path));
            Assert.False(File.Exists(path + ".saving"));
            Assert.False(File.Exists(path + ".replacing"));
            AssertFree(path);
        }
    }

    [Fact]
    public void Failed_replace_never_loses_the_map()
    {
        // Win32's ReplaceFile can fail half way. 1177: the map was moved to the backup name and the new archive was
        // not moved in. 1176 (as without a backup name): the map is gone and the new archive kept its own name.
        var original = File.ReadAllBytes(Sample);
        byte[] w3u = [1, 2, 3];

        var path = Copy(Sample, "moved.w3x");
        var map = MapArchive.Open(path);
        map.ReplaceFile = (saving, target, backup) =>
        {
            File.Move(target, backup!);
            throw new IOException("Simulated", unchecked((int)0x80070499));
        };
        var problem = Assert.Throws<MapArchiveException>(() => map.Write([("war3map.w3u", w3u)]));
        Assert.Contains("is as it was", problem.Message);
        Assert.False(problem.Saved);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".saving"));
        Assert.False(File.Exists(path + ".replacing"));
        Assert.Equal(War3NetReads(Sample, "war3map.w3u"), MapArchive.Open(path).Read("war3map.w3u"));

        path = Copy(Sample, "gone.w3x");
        var before = Entries(path);
        map = MapArchive.Open(path);
        map.ReplaceFile = (saving, target, backup) =>
        {
            File.Delete(target);
            throw new IOException("Simulated", unchecked((int)0x80070498));
        };
        problem = Assert.Throws<MapArchiveException>(() => map.Write([("war3map.w3u", w3u)]));
        Assert.Contains("was saved", problem.Message);
        Assert.True(problem.Saved);
        Assert.False(File.Exists(path + ".saving"));
        Assert.False(File.Exists(path + ".replacing"));
        AssertCarried(before, Entries(path), "war3map.w3u");
        Assert.Equal(w3u, MapArchive.Open(path).Read("war3map.w3u"));
    }

    [Fact]
    public void Undecryptable_file_is_checked_by_its_stored_bytes()
    {
        // Moonwell\hello.txt unnamed, stored whole and encrypted by its position: War3Net cannot find its key, so it
        // keeps the file where it is, and the check compares it as stored.
        const string unlisted = "Moonwell\\hello.txt";
        var path = Path.Combine(_folder, "undecryptable.w3x");
        var flags = MpqFileFlags.Exists | MpqFileFlags.Encrypted | MpqFileFlags.BlockOffsetAdjustedKey;
        Rebuild(Sample, path, unlisted: unlisted, flags: (unlisted, flags));
        var hash = MpqHash.GetHashedFileName(unlisted);
        var before = Entries(path);
        Assert.Null(before.Single(entry => entry.Name == hash).Data);

        MapArchive.Open(path).Write([("war3map.w3u", [1, 2, 3])]);

        AssertCarried(before, Entries(path), "war3map.w3u");
    }

    [Fact]
    public void Save_is_refused_when_the_map_changed_after_its_stamp_was_taken()
    {
        var path = Copy(Sample, "Sample.w3x");
        var map = MapArchive.Open(path);
        var stamp = map.Stamp();

        // World Editor saved the map after the stamp was taken, before the save held it.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        var bytes = File.ReadAllBytes(path);
        var problem = Assert.Throws<MapArchiveException>(() => map.Write([("war3map.w3u", [1, 2, 3])], stamp));
        Assert.True(problem.ChangedOnDisk);
        Assert.False(problem.Saved);
        Assert.Contains("changed on disk", problem.Message);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".saving"));

        // Its time changed while the new archive was checked (the save holds the map from writes, not from that): the
        // stamp is checked again just before the replace.
        map.BeforeCheck = _ => File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(2));
        bytes = File.ReadAllBytes(path);
        problem = Assert.Throws<MapArchiveException>(() => map.Write([("war3map.w3u", [1, 2, 3])], map.Stamp()));
        Assert.True(problem.ChangedOnDisk);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".saving"));
        map.BeforeCheck = null;

        // With the stamp it has now, the save goes through.
        map.Write([("war3map.w3u", [1, 2, 3])], map.Stamp());
        Assert.Equal([1, 2, 3], War3NetReads(path, "war3map.w3u"));
    }

    [Fact]
    public void Locked_archive_refuses_the_save()
    {
        var path = Copy(Sample, "Sample.w3x");
        var bytes = File.ReadAllBytes(path);
        var map = MapArchive.Open(path);

        // Held with no sharing at all, and held for reading only (the map can be read, but not replaced).
        var holds = new[] { (FileAccess.ReadWrite, FileShare.None), (FileAccess.Read, FileShare.Read) };
        foreach (var (access, share) in holds)
        {
            using (new FileStream(path, FileMode.Open, access, share))
            {
                var problem = Assert.Throws<MapArchiveException>(() => map.Write([("war3map.w3u", [1, 2, 3])]));
                Assert.Equal(
                    "The map is open in another program (World Editor?): close it there and save again.",
                    problem.Message);
                Assert.False(problem.Saved);
                Assert.Equal(path, problem.Path);
                Assert.False(File.Exists(path + ".saving"));
                Assert.False(File.Exists(path + ".replacing"));
            }

            Assert.Equal(bytes, File.ReadAllBytes(path));
        }

        // Once it is closed there, the save goes through.
        map.Write([("war3map.w3u", [1, 2, 3])]);
        Assert.Equal([1, 2, 3], War3NetReads(path, "war3map.w3u"));
    }

    [Fact]
    public void Stale_backup_that_cannot_be_deleted_refuses_the_save()
    {
        // A <map>.replacing left by an earlier save cut short, which cannot be deleted now: held by another program,
        // or read only (File.Replace gives it the old map's attributes).
        var path = Copy(Sample, "Sample.w3x");
        var bytes = File.ReadAllBytes(path);
        var replacing = path + ".replacing";
        var map = MapArchive.Open(path);

        File.WriteAllBytes(replacing, [1, 2, 3]);
        using (new FileStream(replacing, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var problem = Assert.Throws<MapArchiveException>(() => map.Write([("war3map.w3u", [1, 2, 3])]));
            Assert.Contains(replacing, problem.Message);
            Assert.Contains("was not saved", problem.Message);
            Assert.False(problem.Saved);
        }

        File.SetAttributes(replacing, FileAttributes.ReadOnly);
        try
        {
            var problem = Assert.Throws<MapArchiveException>(() => map.Write([("war3map.w3u", [1, 2, 3])]));
            Assert.Contains(replacing, problem.Message);
        }
        finally
        {
            File.SetAttributes(replacing, FileAttributes.Normal);
        }

        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".saving"));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(replacing));

        // One that can be deleted is: it is stale, since the map itself was just read.
        map.Write([("war3map.w3u", [1, 2, 3])]);
        Assert.False(File.Exists(replacing));
        Assert.Equal([1, 2, 3], War3NetReads(path, "war3map.w3u"));
    }

    [Fact]
    public void Delete_is_refused()
    {
        var path = Copy(Sample, "Sample.w3x");
        var bytes = File.ReadAllBytes(path);
        var map = MapArchive.Open(path);

        var problem = Assert.Throws<MapArchiveException>(
            () => map.Write([("war3map.w3a", [1, 2, 3]), ("war3map.w3u", null)]));
        Assert.Contains("war3map.w3u", problem.Message);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".saving"));
        Assert.False(File.Exists(path + ".replacing"));
    }

    [Fact]
    public void Shared_block_is_a_problem()
    {
        // Two hash entries that point at one block (a protector's trick): War3Net would keep only one name.
        var path = Copy(Sample, "shared.w3x");
        ShareOneBlock(path);
        using (var archive = MpqArchive.Open(path, loadListFile: true))
        {
            var blocks = archive.EnumerateHashes().Select(hash => hash.BlockIndex).ToList();
            Assert.NotEqual(blocks.Count, blocks.Distinct().Count());   // War3Net itself opens it
        }

        var problem = Assert.Throws<MapArchiveException>(() => MapArchive.Open(path));
        Assert.Contains(path, problem.Message);
        Assert.Contains("share", problem.Message);
    }

    [Fact]
    public void Losttemple_opens()
    {
        var folder = Environment.GetEnvironmentVariable(GameDataVariable);
        var original = string.IsNullOrEmpty(folder) ? "" : Path.Combine(folder, "maps", "(4)losttemple.w3m");
        if (!File.Exists(original))
        {
            _output.WriteLine($"{GameDataVariable} names no game data folder with maps\\(4)losttemple.w3m; skipped");
            return;
        }

        var path = Copy(original, "(4)losttemple.w3m");
        var map = MapArchive.Open(path);

        Assert.True(map.IsArchive);
        Assert.Null(map.Read("war3map.w3u"));
        Assert.Equal(War3NetReads(path, "war3map.wts"), map.Read("war3map.wts"));
        Assert.NotNull(map.Read("war3map.w3i"));
        Assert.Empty(map.PreArchive);
        Assert.Contains("war3map.w3i", map.Names);

        // A save adds war3map.w3u (the map has none), keeps every other file as stored, and drops Blizzard's signature
        // after the archive.
        var before = Entries(path);
        Assert.True(new FileInfo(path).Length > BitConverter.ToUInt32(File.ReadAllBytes(path), 8));
        map.Write([("war3map.w3u", [1, 2, 3])]);
        AssertCarried(before, Entries(path), "war3map.w3u");
        Assert.Equal(new FileInfo(path).Length, BitConverter.ToUInt32(File.ReadAllBytes(path), 8));
        Assert.Equal([1, 2, 3], map.Read("war3map.w3u"));
    }

    [Fact]
    public void World_editors_own_archive_opens()
    {
        // Saved by World Editor from the template map with no object edits (File > Save Map As): no object files.
        var path = Copy(WorldEditorArchive, "ObjectEditorTest.w3x");
        var bytes = File.ReadAllBytes(path);
        var map = MapArchive.Open(path);

        // The MPQ header is the first thing in the file: this World Editor writes no 512-byte HM3W header before it.
        Assert.Equal("MPQ\x1A"u8.ToArray(), bytes[..4]);
        Assert.Empty(map.PreArchive);
        Assert.Equal(16634, bytes.Length);
        Assert.Equal(32u, BitConverter.ToUInt32(bytes, 4));
        Assert.Equal((uint)bytes.Length, BitConverter.ToUInt32(bytes, 8));
        Assert.Equal(3, BitConverter.ToUInt16(bytes, 14));

        // Its seventeen files by name, besides (listfile) and (attributes).
        string[] expected =
        [
            "conversation.json", "war3map.doo", "war3map.lua", "war3map.mmp", "war3map.shd", "war3map.w3c",
            "war3map.w3e", "war3map.w3grp", "war3map.w3i", "war3map.w3l", "war3map.w3r", "war3map.wct", "war3map.wpm",
            "war3map.wtg", "war3map.wts", "war3mapMap.blp", "war3mapUnits.doo",
        ];
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            map.Names.Where(name => !Specials.Contains(name)).Order(StringComparer.Ordinal));
        Assert.Contains("(listfile)", map.Names);
        Assert.Contains("(attributes)", map.Names);

        // No read-only kind (no object or strings file under another locale), and what the app reads is what War3Net
        // reads: the strings file, and no object file at all.
        foreach (var name in MapStore.ObjectAndStringFiles)
        {
            Assert.Null(map.WriteProblem(name));
            Assert.Equal(War3NetReads(path, name), map.Read(name));
            Assert.Equal(name == "war3map.wts", map.Read(name) is not null);
        }

        // The strings file is the template's own, with the byte order mark World Editor writes.
        var strings = map.Read("war3map.wts")!;
        Assert.Equal(176, strings.Length);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, strings[..3]);
        Assert.Contains("Moonwell Template", System.Text.Encoding.UTF8.GetString(strings));

        // Files the app does not read at open are read when asked for.
        foreach (var name in new[] { "war3map.w3i", "war3map.lua", "war3map.w3e", "war3mapMap.blp" })
        {
            Assert.Equal(War3NetReads(path, name), map.Read(name));
        }

        Assert.IsType<MapArchive>(MapStore.Open(path));
    }

    [Fact]
    public void World_editors_own_archive_saves_every_file_as_it_was()
    {
        var path = Copy(WorldEditorArchive, "ObjectEditorTest.w3x");
        var before = Entries(path);
        var map = MapArchive.Open(path);
        var names = map.Names.Where(name => !Specials.Contains(name)).Order(StringComparer.Ordinal).ToList();
        var read = names.ToDictionary(name => name, name => map.Read(name)!);
        Assert.Equal(17, names.Count);

        // A war3map.w3u the archive did not have (the heavy folder fixture's) and a changed war3map.wts.
        var units = Fixtures.Bytes("we-heavy/war3map.w3u");
        byte[] strings = [.. read["war3map.wts"], .. "STRING 7\r\n{\r\nChanged\r\n}\r\n\r\n"u8];
        map.Write([("war3map.w3u", units), ("war3map.wts", strings)]);

        // Every other file is carried as stored (same flags, same bytes), and reads the same.
        var after = Entries(path);
        AssertCarried(before, after, "war3map.w3u", "war3map.wts");
        Assert.Equal(
            before.Single(entry => entry.FileName == "war3map.wts").Flags,
            after.Single(entry => entry.FileName == "war3map.wts").Flags);
        var reopened = MapArchive.Open(path);
        Assert.Equal(units, reopened.Read("war3map.w3u"));
        Assert.Equal(strings, reopened.Read("war3map.wts"));
        foreach (var (name, original) in read.Where(file => file.Key != "war3map.wts"))
        {
            Assert.True(original.AsSpan().SequenceEqual(reopened.Read(name)), $"{name} changed");
        }

        // Every name is still listed, with the new file; the MPQ header is still the first thing in the file.
        Assert.Equal(
            names.Append("war3map.w3u").Order(StringComparer.Ordinal),
            reopened.Names.Where(name => !Specials.Contains(name)).Order(StringComparer.Ordinal));
        Assert.Empty(reopened.PreArchive);
        var saved = File.ReadAllBytes(path);
        Assert.Equal("MPQ\x1A"u8.ToArray(), saved[..4]);
        Assert.Equal((uint)saved.Length, BitConverter.ToUInt32(saved, 8));
        Assert.Equal(3, BitConverter.ToUInt16(saved, 14));
        Assert.False(File.Exists(path + ".saving"));
        Assert.False(File.Exists(path + ".replacing"));
    }

    [Fact]
    public void Block_size_is_kept()
    {
        // Saved with War3Net's default block size, 7 of these files would be compressed again (the spike).
        var path = Path.Combine(_folder, "blocks.w3x");
        Rebuild(Sample, path, blockSize: 4);
        var before = Entries(path);

        MapArchive.Open(path).Write([("war3map.w3u", [1, 2, 3])]);

        Assert.Equal(4, BitConverter.ToUInt16(File.ReadAllBytes(path), 14));
        AssertCarried(before, Entries(path), "war3map.w3u");
    }

    [Fact]
    public void Stamp_changes_when_the_file_does()
    {
        var path = Copy(Sample, "Sample.w3x");
        var time = new DateTime(2001, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, time);
        var map = MapArchive.Open(path);
        var stamp = map.Stamp();

        Assert.Equal(stamp, map.Stamp());

        // A later write time.
        File.SetLastWriteTimeUtc(path, time.AddSeconds(1));
        Assert.NotEqual(stamp, map.Stamp());

        // The same write time, but another length.
        File.AppendAllText(path, "x");
        File.SetLastWriteTimeUtc(path, time);
        Assert.NotEqual(stamp, map.Stamp());

        // Another archive in its place: what is read follows the file on disk.
        var other = Path.Combine(_folder, "other.w3x");
        using (var w3u = new MemoryStream([1, 2, 3]))
        using (MpqArchive.Create(other, [MpqFile.New(w3u, "war3map.w3u")], new MpqArchiveCreateOptions()))
        {
        }

        File.Copy(other, path, true);
        Assert.Equal([1, 2, 3], map.Read("war3map.w3u"));
        Assert.Null(map.Read("war3map.wts"));

        File.Delete(path);
        Assert.NotEqual(stamp, map.Stamp());
    }

    // Copies a file into this test's folder.
    private string Copy(string source, string name)
    {
        var path = Path.Combine(_folder, name);
        File.Copy(source, path);
        return path;
    }

    // No program holds the file: it can be taken with no sharing and cut to nothing (which a memory-mapped view left
    // open by War3Net would refuse).
    private static void AssertFree(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.SetLength(0);
    }

    // The specials War3Net writes anew in every save.
    private static readonly string[] Specials = ["(listfile)", "(attributes)", "(signature)"];

    /// <summary>One hash entry of an archive, as the file on disk holds it.</summary>
    /// <param name="Name">The file name's hash (what the archive itself keeps of a name).</param>
    /// <param name="Locale">The entry's locale.</param>
    /// <param name="FileName">The name, when War3Net knows it.</param>
    /// <param name="Flags">The block's flags.</param>
    /// <param name="Stored">The block's bytes as stored: compressed, maybe encrypted.</param>
    /// <param name="Data">The file's bytes, decompressed; null when War3Net cannot read them.</param>
    private sealed record Entry(
        ulong Name, MpqLocale Locale, string? FileName, MpqFileFlags Flags, byte[] Stored, byte[]? Data);

    // Every hash entry of an archive, with the names the app knows.
    private static List<Entry> Entries(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var archive = MpqArchive.Open(new MemoryStream(bytes), loadListFile: true);
        archive.AddFileNames(MapArchive.KnownNames);
        var entries = new List<Entry>();
        foreach (var hash in archive.EnumerateHashes().Where(hash => hash.BlockIndex < archive.Count))
        {
            var block = archive[(int)hash.BlockIndex];
            var stored = bytes.AsSpan((int)block.FilePosition, (int)block.CompressedSize).ToArray();
            byte[]? data = null;
            using (var stream = archive.OpenFile(block))
            {
                if (stream.CanRead)
                {
                    var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    data = memory.ToArray();
                }
            }

            entries.Add(new Entry(hash.Name, hash.Locale, block.FileName, block.Flags, stored, data));
        }

        return entries;
    }

    // Every entry of before, other than the written files and the specials, is in after with the same flags and the
    // same stored bytes; after has no other entries than those and the written files.
    private static void AssertCarried(List<Entry> before, List<Entry> after, params string[] written)
    {
        var writtenHashes = written.Select(MpqHash.GetHashedFileName).ToHashSet();
        var specialHashes = Specials.Select(MpqHash.GetHashedFileName).ToHashSet();
        var carried = before.Where(entry => !specialHashes.Contains(entry.Name) && !writtenHashes.Contains(entry.Name))
            .ToList();
        foreach (var old in carried)
        {
            var now = after.Single(entry => entry.Name == old.Name && entry.Locale == old.Locale);
            Assert.Equal(old.Flags, now.Flags);
            Assert.True(old.Stored.AsSpan().SequenceEqual(now.Stored), $"{old.FileName ?? $"{old.Name:X16}"} changed");
        }

        Assert.Equal(carried.Count + written.Length, after.Count(entry => !specialHashes.Contains(entry.Name)));
    }

    // Changes one byte of a file's stored bytes in an archive (MPQ header at 0): the middle one or the last one.
    private static void Spoil(string path, string name, bool middle)
    {
        var bytes = File.ReadAllBytes(path);
        int position;
        using (var archive = MpqArchive.Open(new MemoryStream(bytes), loadListFile: true))
        {
            var block = archive.GetMpqEntries(name).First();
            position = (int)(block.FilePosition + (middle ? block.CompressedSize / 2 : block.CompressedSize - 1));
        }

        bytes[position] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
    }

    // Adds a file to an archive (MPQ header at 0), keeping every other file as it is.
    private static void AddFile(string path, string name)
    {
        using var memory = new MemoryStream();
        using (var archive = MpqArchive.Open(new MemoryStream(File.ReadAllBytes(path)), loadListFile: true))
        {
            var builder = new MpqArchiveBuilder(archive);
            builder.AddFile(MpqFile.New(new MemoryStream([1, 2, 3]), name));
            builder.SaveTo(memory, leaveOpen: true);
        }

        File.WriteAllBytes(path, memory.ToArray());
    }

    // Writes source's files into a new archive at target, as the spike did: optionally one file left out of
    // (listfile), one file stored with other flags, another block size than War3Net's default (3), and a
    // (signature) file (72 bytes, as a weak signature is).
    private static void Rebuild(
        string source,
        string target,
        string? unlisted = null,
        (string Name, MpqFileFlags Flags)? flags = null,
        ushort blockSize = 3,
        bool signature = false)
    {
        using var original = MpqArchive.Open(new MemoryStream(File.ReadAllBytes(source)), loadListFile: true);
        List<string> listed;
        using (var reader = new StreamReader(original.OpenFile("(listfile)")))
        {
            listed = [.. reader.ReadToEnd().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)];
        }

        var files = new List<MpqFile>();
        foreach (var name in listed.Where(name => !Specials.Contains(name) && original.FileExists(name)))
        {
            var memory = new MemoryStream();
            using (var stream = original.OpenFile(name))
            {
                stream.CopyTo(memory);
            }

            memory.Position = 0;
            var file = MpqFile.New(memory, name);
            file.TargetFlags = flags is { } other && other.Name == name
                ? other.Flags
                : original.GetMpqEntries(name).First().Flags & ~MpqFileFlags.Garbage;
            files.Add(file);
        }

        var listText = string.Join("\r\n", listed.Where(name => name != unlisted)) + "\r\n";
        var listFile = MpqFile.New(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(listText)), "(listfile)");
        listFile.TargetFlags = MpqFileFlags.Exists | MpqFileFlags.CompressedMulti | MpqFileFlags.Encrypted
            | MpqFileFlags.BlockOffsetAdjustedKey;
        files.Add(listFile);
        if (signature)
        {
            var signatureFile = MpqFile.New(new MemoryStream(new byte[72]), "(signature)");
            signatureFile.TargetFlags = MpqFileFlags.Exists;
            files.Add(signatureFile);
        }

        var options = new MpqArchiveCreateOptions
        {
            ListFileCreateMode = MpqFileCreateMode.None,
            AttributesCreateMode = MpqFileCreateMode.Overwrite,
            AttributesFlags = AttributesFlags.Crc32,
            BlockSize = blockSize,
            SignatureCreateMode = signature ? MpqFileCreateMode.None : null,
        };

        using var archive = new MemoryStream();
        MpqArchive.Create(archive, files, options, leaveOpen: true).Dispose();
        File.WriteAllBytes(target, archive.ToArray());
    }

    // A file's bytes as War3Net reads them from the archive, or null when it has none.
    private static byte[]? War3NetReads(string path, string name)
    {
        using var archive = MpqArchive.Open(new MemoryStream(File.ReadAllBytes(path)), loadListFile: true);
        if (!archive.TryOpenFile(name, out var stream))
        {
            return null;
        }

        using (stream)
        {
            var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }

    // Moves a file's hash entry in an archive (MPQ header at 0) to just past three entries War3Net leaves out of the
    // table it writes: from the slot its name hashes to, the first three slots free (empty or deleted) get a deleted
    // entry, the archive's (signature) entry and an entry that points at no block, and the fourth the file's entry.
    // The slots the file and (signature) left get a deleted entry, so no other file's chain is cut. Returns the
    // three slots.
    private static int[] ChainPastDroppedEntries(string path, string name)
    {
        var dropped = Array.Empty<int>();
        ChangeHashTable(path, table =>
        {
            var file = Array.FindIndex(table, slot => slot.Name == MpqHash.GetHashedFileName(name));
            var signature = Array.FindIndex(table, slot => slot.Name == MpqHash.GetHashedFileName("(signature)"));
            Assert.True(file >= 0 && signature >= 0);
            var (fileEntry, signatureEntry) = (table[file], table[signature]);
            table[file] = StormHashTable.Slot.Deleted;
            table[signature] = StormHashTable.Slot.Deleted;

            var home = (int)(StormHashTable.Hash(name, 0) & (uint)(table.Length - 1));
            var free = Enumerable.Range(0, table.Length)
                .Select(step => (home + step) % table.Length)
                .Where(slot => !table[slot].IsLive)
                .Take(4)
                .ToArray();
            table[free[0]] = StormHashTable.Slot.Deleted;
            table[free[1]] = signatureEntry;
            table[free[2]] = new StormHashTable.Slot(
                StormHashTable.Hash("no block", 0x100), StormHashTable.Hash("no block", 0x200), 0, 0x7FFFFFFF);
            table[free[3]] = fileEntry;
            dropped = free[..3];
        });

        return dropped;
    }

    // Moves a file's hash entry in an archive (MPQ header at 0) to the nearest empty slot before the one its name
    // hashes to: War3Net still finds it (it searches the whole table), the game does not (it stops at the first empty
    // slot after the home). The slot it left gets a deleted entry.
    private static void MoveOutOfReach(string path, string name)
    {
        ChangeHashTable(path, table =>
        {
            var file = Array.FindIndex(table, slot => slot.Name == MpqHash.GetHashedFileName(name));
            var entry = table[file];
            table[file] = StormHashTable.Slot.Deleted;
            var home = (int)(StormHashTable.Hash(name, 0) & (uint)(table.Length - 1));
            var before = Enumerable.Range(1, table.Length - 1)
                .Select(back => (home - back + table.Length) % table.Length)
                .First(slot => table[slot].IsEmpty);
            table[before] = entry;
        });
    }

    // Whether the game would find a file (neutral locale) in an archive (MPQ header at 0): from the slot its name
    // hashes to, forward to the first empty slot.
    private static bool GameFinds(string path, string name)
    {
        using var stream = File.OpenRead(path);
        var blocks = new byte[4];
        stream.Position = 28;
        stream.ReadExactly(blocks);
        return StormHashTable.Find(StormHashTable.Read(stream, 0), name, 0, BitConverter.ToInt32(blocks)) >= 0;
    }

    // Points the second live hash entry of an archive (MPQ header at 0) at the first one's block.
    private static void ShareOneBlock(string path)
    {
        ChangeHashTable(path, table =>
        {
            var live = Enumerable.Range(0, table.Length).Where(slot => table[slot].IsLive).ToList();
            table[live[1]] = table[live[1]] with { Block = table[live[0]].Block };
        });
    }

    // Changes the hash table of an archive (MPQ header at 0), decrypted, and writes it back encrypted.
    private static void ChangeHashTable(string path, Action<StormHashTable.Slot[]> change)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        var table = StormHashTable.Read(stream, 0);
        change(table);
        StormHashTable.Write(stream, 0, table);
    }
}
