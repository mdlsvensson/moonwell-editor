namespace ObjectEditor.Files;

/// <summary>
/// A map stored as a folder of loose files (a World Editor folder map, or a Moonwell project's <c>map/</c>). Files are
/// named by their plain file name, such as <c>war3map.w3u</c>.
/// </summary>
public sealed class MapFolder : IMapStore
{
    private MapFolder(string path)
    {
        Path = path;
    }

    /// <summary>The folder's full path.</summary>
    public string Path { get; }

    /// <summary>False: a folder is not an archive.</summary>
    public bool IsArchive => false;

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

    /// <summary>Null: a folder's files may always be written.</summary>
    public string? WriteProblem(string name) => null;

    /// <summary>
    /// Writes files: each one is written to a temporary file first, which then replaces the old one. Null bytes delete
    /// the file (nothing happens when it is not there). The stamp is not checked (see <see cref="IMapStore.Write"/>).
    /// </summary>
    public void Write(IReadOnlyList<(string Name, byte[]? Bytes)> files, string? stamp = null)
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
    /// The newest write time (UTC, in ticks) of the folder's object files, main and skin, and its
    /// <c>war3map.wts</c>, and how many of those files there are, as <c>ticks:count</c> (<c>0:0</c> when it has
    /// none). A different stamp means the map changed on disk; the count sees a file deleted that was not the newest.
    /// </summary>
    public string Stamp()
    {
        var newest = DateTime.MinValue;
        var count = 0;
        foreach (var name in MapStore.ObjectAndStringFiles)
        {
            var path = FilePath(name);
            if (File.Exists(path))
            {
                count++;
                var time = File.GetLastWriteTimeUtc(path);
                if (time > newest)
                {
                    newest = time;
                }
            }
        }

        return $"{newest.Ticks}:{count}";
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
