using War3Net.IO.Mpq;

namespace ObjectEditor.Files;

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

    /// <summary>Not yet: saving an archive comes with the checked save.</summary>
    public void Write(IReadOnlyList<(string Name, byte[]? Bytes)> files)
    {
        throw new NotSupportedException($"Saving a map archive ({Path}) is not supported yet.");
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

    // Reads the archive at path: its stamp (taken first, so a change made while reading is seen later), the bytes
    // before the MPQ header, the names of its files, and the bytes of the files named (null for one it does not have).
    private static Contents Load(string path, IReadOnlyList<string> names)
    {
        var stamp = StampOf(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            using var archive = MpqArchive.Open(stream, loadListFile: true);
            archive.AddFileNames(KnownNames);

            var shared = archive.EnumerateHashes().GroupBy(hash => hash.BlockIndex).Count(block => block.Count() > 1);
            if (shared > 0)
            {
                throw new MapArchiveException(path, $"{shared} of its files are under more than one name (they share "
                    + "one block of data, as protected maps do), and War3Net would keep only one name of each");
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

            return new Contents(stamp, ReadPreArchive(stream), listed, files);
        }
        catch (Exception e) when (e is not MapArchiveException)
        {
            throw new MapArchiveException(path, e.Message, e);
        }
    }

    // A file's bytes, decompressed, or null when the archive has no file of that name.
    private static byte[]? ReadFile(MpqArchive archive, string name)
    {
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

        throw new InvalidDataException("There is no MPQ header.");
    }

    /// <summary>What <see cref="Load"/> read from the archive.</summary>
    /// <param name="Stamp">The archive's <see cref="MapArchive.Stamp"/> before reading.</param>
    /// <param name="PreArchive">The bytes before the MPQ header.</param>
    /// <param name="Names">The names of the files that have one.</param>
    /// <param name="Files">
    /// The files read, by name (letter case ignored); null for one the archive does not have.
    /// </param>
    private sealed record Contents(
        string Stamp, byte[] PreArchive, IReadOnlyList<string> Names, Dictionary<string, byte[]?> Files);
}

/// <summary>
/// A map archive the app cannot open: War3Net cannot read it (a damaged or protected map), or it is a protected map a
/// save would damage. The message names the file and the reason.
/// </summary>
public sealed class MapArchiveException : Exception
{
    /// <summary>The archive at <paramref name="path"/> cannot be opened, for <paramref name="reason"/>.</summary>
    public MapArchiveException(string path, string reason, Exception? inner = null)
        : base($"The map archive {path} cannot be opened: {reason}", inner)
    {
        Path = path;
    }

    /// <summary>The archive file's path.</summary>
    public string Path { get; }
}
