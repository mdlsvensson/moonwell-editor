using War3Net.IO.Mpq;
using Xunit.Abstractions;

namespace ObjectEditor.Files.Tests;

/// <summary>
/// Reading map archives (<c>.w3x</c>, <c>.w3m</c>), and the map store that opens folders and archives.
/// </summary>
public sealed class MapArchiveTests : IDisposable
{
    /// <summary>The environment variable that names the game data folder (war3.w3mod).</summary>
    private const string GameDataVariable = "OBJECT_EDITOR_GAME_DATA";

    private readonly ITestOutputHelper _output;

    // A temporary folder for this test's copies; the fixtures themselves are never written.
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "ObjectEditor.Files.Tests", Guid.NewGuid().ToString("N"));

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

        // The file is closed after opening: another program may take it for itself.
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
        }

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
        }
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

    // A file's bytes as War3Net reads them from the archive, or null when it has none.
    private static byte[]? War3NetReads(string path, string name)
    {
        using var archive = MpqArchive.Open(path, loadListFile: true);
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

    // Points the second live hash entry of an archive (MPQ header at 0) at the first one's block. The hash table is
    // encrypted, so it is decrypted, changed and encrypted again.
    private static void ShareOneBlock(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var position = BitConverter.ToInt32(bytes, 16);
        var entries = BitConverter.ToInt32(bytes, 24);
        var table = new uint[entries * 4];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = BitConverter.ToUInt32(bytes, position + i * 4);
        }

        var key = MpqCrypt.Hash("(hash table)", 0x300);
        MpqCrypt.Decrypt(table, key);
        var live = Enumerable.Range(0, entries).Where(entry => table[entry * 4 + 3] < 0xFFFFFFFE).ToList();
        table[live[1] * 4 + 3] = table[live[0] * 4 + 3];
        MpqCrypt.Encrypt(table, key);
        for (var i = 0; i < table.Length; i++)
        {
            BitConverter.GetBytes(table[i]).CopyTo(bytes, position + i * 4);
        }

        File.WriteAllBytes(path, bytes);
    }

    /// <summary>The MPQ format's hash and table encryption (War3Net has its own, but internal).</summary>
    private static class MpqCrypt
    {
        private static readonly uint[] Table = MakeTable();

        public static uint Hash(string text, uint type)
        {
            uint seed1 = 0x7FED7FED;
            uint seed2 = 0xEEEEEEEE;
            foreach (var letter in text.ToUpperInvariant())
            {
                seed1 = Table[type + letter] ^ (seed1 + seed2);
                seed2 = letter + seed1 + seed2 + (seed2 << 5) + 3;
            }

            return seed1;
        }

        public static void Decrypt(uint[] data, uint key)
        {
            uint seed = 0xEEEEEEEE;
            for (var i = 0; i < data.Length; i++)
            {
                seed += Table[0x400 + (key & 0xFF)];
                data[i] ^= key + seed;
                key = ((~key << 0x15) + 0x11111111) | (key >> 0x0B);
                seed = data[i] + seed + (seed << 5) + 3;
            }
        }

        public static void Encrypt(uint[] data, uint key)
        {
            uint seed = 0xEEEEEEEE;
            for (var i = 0; i < data.Length; i++)
            {
                seed += Table[0x400 + (key & 0xFF)];
                var plain = data[i];
                data[i] ^= key + seed;
                key = ((~key << 0x15) + 0x11111111) | (key >> 0x0B);
                seed = plain + seed + (seed << 5) + 3;
            }
        }

        private static uint[] MakeTable()
        {
            var table = new uint[0x500];
            uint seed = 0x00100001;
            for (var first = 0; first < 0x100; first++)
            {
                for (var i = 0; i < 5; i++)
                {
                    seed = (seed * 125 + 3) % 0x2AAAAB;
                    var high = (seed & 0xFFFF) << 0x10;
                    seed = (seed * 125 + 3) % 0x2AAAAB;
                    table[first + i * 0x100] = high | (seed & 0xFFFF);
                }
            }

            return table;
        }
    }
}
