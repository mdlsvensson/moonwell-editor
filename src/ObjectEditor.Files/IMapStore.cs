namespace ObjectEditor.Files;

/// <summary>
/// Where a map's files are kept: a folder of loose files (<see cref="MapFolder"/>) or an MPQ archive, a <c>.w3x</c>
/// or <c>.w3m</c> (<see cref="MapArchive"/>). Files are named as the map names them, such as <c>war3map.w3u</c>.
/// </summary>
public interface IMapStore
{
    /// <summary>The full path of the folder or of the archive file.</summary>
    string Path { get; }

    /// <summary>True for an archive, false for a folder.</summary>
    bool IsArchive { get; }

    /// <summary>A file's bytes, or null when the map has no such file.</summary>
    byte[]? Read(string name);

    /// <summary>
    /// Why the map's object or strings file <paramref name="name"/> may not be written, or null when it may (an
    /// archive that holds it under another locale than the neutral one; a folder has no such reason).
    /// </summary>
    string? WriteProblem(string name);

    /// <summary>
    /// Writes files; null bytes delete a file (an archive refuses a delete). With <paramref name="stamp"/>, the stamp
    /// the caller checked, an archive refuses the save (<see cref="MapArchiveException.ChangedOnDisk"/>) when the map
    /// has another one once the save holds it; a folder, whose files are written one at a time and not held, ignores
    /// it.
    /// </summary>
    void Write(IReadOnlyList<(string Name, byte[]? Bytes)> files, string? stamp = null);

    /// <summary>
    /// A fingerprint of the map's files on disk, compared for equality: a different stamp means the map changed on
    /// disk since the last one was taken.
    /// </summary>
    string Stamp();
}

/// <summary>Opens a map, whichever way it is stored.</summary>
public static class MapStore
{
    /// <summary>
    /// The files the app reads and writes: each kind's object files, main and skin (<c>war3map.w3u</c>,
    /// <c>war3mapSkin.w3u</c> ...), then the strings file <c>war3map.wts</c>.
    /// </summary>
    internal static readonly IReadOnlyList<string> ObjectAndStringFiles = MakeObjectAndStringFiles();

    /// <summary>
    /// Opens the map at <paramref name="path"/>: a folder as a <see cref="MapFolder"/>, a <c>.w3x</c> or <c>.w3m</c>
    /// file (letter case ignored) as a <see cref="MapArchive"/>. Throws <see cref="FileNotFoundException"/> when there
    /// is neither a folder nor a file, <see cref="InvalidDataException"/> ("Not a map: ...") for any other file, and
    /// <see cref="MapArchiveException"/> when the file is not an archive that can be read.
    /// </summary>
    public static IMapStore Open(string path)
    {
        if (Directory.Exists(path))
        {
            return MapFolder.Open(path);
        }

        var full = System.IO.Path.GetFullPath(path);
        if (File.Exists(full))
        {
            var extension = System.IO.Path.GetExtension(full);
            if (!extension.Equals(".w3x", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".w3m", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Not a map: {full} is a file, but not a .w3x or .w3m map archive.");
            }

            return MapArchive.Open(full);
        }

        throw new FileNotFoundException($"There is no map folder or archive at {full}.", full);
    }

    private static List<string> MakeObjectAndStringFiles()
    {
        var names = new List<string>();
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            names.Add("war3map." + ObjectKinds.Extension(kind));
            names.Add("war3mapSkin." + ObjectKinds.Extension(kind));
        }

        names.Add("war3map.wts");
        return names;
    }
}
