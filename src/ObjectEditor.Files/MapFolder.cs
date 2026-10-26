namespace ObjectEditor.Files;

/// <summary>
/// A map stored as a folder of loose files (a World Editor folder map, or a Moonwell project's <c>map/</c>). Files are
/// named by their plain file name, such as <c>war3map.w3u</c>.
/// </summary>
public sealed class MapFolder
{
    private MapFolder(string path)
    {
        Path = path;
    }

    /// <summary>The folder's full path.</summary>
    public string Path { get; }

    /// <summary>
    /// Opens a map folder; throws <see cref="DirectoryNotFoundException"/> when there is no folder there.
    /// </summary>
    public static MapFolder Open(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"There is no folder at {full}.");
        }

        return new MapFolder(full);
    }

    /// <summary>A file's bytes, or null when the folder has no such file.</summary>
    public byte[]? Read(string name)
    {
        var path = FilePath(name);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>
    /// Writes files: each one is written to a temporary file first, which then replaces the old one. Null bytes delete
    /// the file (nothing happens when it is not there).
    /// </summary>
    public void Write(IReadOnlyList<(string Name, byte[]? Bytes)> files)
    {
        var paths = files.Select(file => FilePath(file.Name)).ToList();
        for (var i = 0; i < files.Count; i++)
        {
            var path = paths[i];
            var bytes = files[i].Bytes;
            if (bytes is null)
            {
                File.Delete(path);
                continue;
            }

            var temporary = path + ".writing";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
    }

    /// <summary>
    /// The newest write time (UTC) of the folder's object files, main and skin, and its <c>war3map.wts</c>;
    /// <see cref="DateTime.MinValue"/> when it has none. A later stamp means the map changed on disk.
    /// </summary>
    public DateTime Stamp()
    {
        var names = new List<string> { "war3map.wts" };
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            names.Add("war3map." + ObjectKinds.Extension(kind));
            names.Add("war3mapSkin." + ObjectKinds.Extension(kind));
        }

        var newest = DateTime.MinValue;
        foreach (var name in names)
        {
            var path = FilePath(name);
            if (File.Exists(path))
            {
                var time = File.GetLastWriteTimeUtc(path);
                if (time > newest)
                {
                    newest = time;
                }
            }
        }

        return newest;
    }

    // A file of this folder: the name must be a plain file name, never a path that leaves the folder.
    private string FilePath(string name)
    {
        if (string.IsNullOrEmpty(name) || name != System.IO.Path.GetFileName(name) || name is "." or ".."
            || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"\"{name}\" is not a file name.", nameof(name));
        }

        return System.IO.Path.Combine(Path, name);
    }
}
