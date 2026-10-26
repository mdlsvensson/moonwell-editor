using System.Globalization;
using System.Text.RegularExpressions;
using ObjectEditor.Files;

namespace ObjectEditor.Model;

/// <summary>
/// Saving the map's files, and reloading them from disk. A save writes only the files whose bytes changed since they
/// were read (or last saved), after copying their previous bytes into a backup folder beside the map folder.
/// </summary>
public sealed partial class Session
{
    /// <summary>How many backup folders of a map are kept.</summary>
    private const int BackupsKept = 10;

    /// <summary>
    /// A backup folder's name: the save's local time, then <c>-2</c>, <c>-3</c> ... for more saves in that second.
    /// </summary>
    private const string BackupTime = "yyyy-MM-dd HH-mm-ss";

    private static readonly Regex BackupName = new(@"^(\d{4}-\d{2}-\d{2} \d{2}-\d{2}-\d{2})(?:-(\d{1,9}))?$");

    /// <summary>
    /// Saves the edits: writes each object file and the strings file whose bytes differ from what was read at open
    /// (or written by the last save), each through a temporary file that then replaces it. The previous bytes of each
    /// file that is replaced go first into a new folder of <c>&lt;map folder&gt;.backup</c>, which keeps the newest 10.
    /// A kind whose files are a problem (see <see cref="Problems"/>) is never written; a file the map did not have is
    /// made only when it has records; a file the map has is never deleted (one an undo took back to none is written
    /// with no records). Afterwards <see cref="Dirty"/> is false and undo still works.
    /// </summary>
    /// <exception cref="ChangedOnDiskException">
    /// The map's files changed on disk since they were read or last saved; nothing was written.
    /// </exception>
    public SaveReport Save()
    {
        if (_folder.Stamp() != _stamp)
        {
            throw new ChangedOnDiskException(_folder.Path);
        }

        var changes = Changes();

        // The previous bytes, from disk; a file whose bytes are not the ones read changed even if its time did not.
        var previous = new List<(string Name, byte[] Bytes)>();
        foreach (var (name, _) in changes)
        {
            var onDisk = _folder.Read(name);
            if (!SameBytes(onDisk, _baseline.GetValueOrDefault(name)))
            {
                throw new ChangedOnDiskException(_folder.Path);
            }

            if (onDisk != null)
            {
                previous.Add((name, onDisk));
            }
        }

        var backup = previous.Count == 0 ? "" : Backup(previous);
        var written = new List<string>();
        try
        {
            foreach (var (name, bytes) in changes)
            {
                _folder.Write([(name, bytes)]);
                _baseline[name] = bytes;
                written.Add(name);
            }
        }
        finally
        {
            // Also after a failed write, so the files written so far are not taken for a change on disk.
            _stamp = _folder.Stamp();
        }

        if (backup != "")
        {
            Prune(backup);
        }

        _history.Saved();
        _savedObjects = Describe();
        return new SaveReport(written, backup);
    }

    /// <summary>
    /// Reads the map folder again, as <see cref="Open(MapFolder, GameData.GameData)"/> does (with the same reader): the
    /// edits that were not saved are dropped, and so is the undo history.
    /// </summary>
    public void Reload()
    {
        var loaded = Load(_folder, _read);
        _kinds = loaded.Kinds;
        _strings = loaded.Strings;
        _problems = loaded.Problems;
        _baseline = loaded.Baseline;
        _stamp = loaded.Stamp;
        _unreadable = loaded.Unreadable;
        _history = new History();
        _search.Rebuild(_kinds, _strings);
        _savedObjects = Describe();
    }

    /// <summary>
    /// The files to write, by name, with their new bytes: the object files by kind, then the strings file.
    /// </summary>
    private List<(string Name, byte[] Bytes)> Changes()
    {
        var changes = new List<(string Name, byte[] Bytes)>();
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            var map = _kinds[kind];
            if (map.Problem != null)
            {
                continue;   // read only: what is in memory may not be what the file holds
            }

            AddChange(changes, map.MainName, ObjectBytes(kind, map.MainName, map.Main));
            AddChange(changes, map.SkinName, ObjectBytes(kind, map.SkinName, map.Skin));
        }

        AddChange(changes, StringsName, _strings?.ToBytes());
        return changes;
    }

    /// <summary>
    /// An object file's bytes to save; null when there is nothing to save: no file, or one with no records, that the
    /// folder does not have either. A file the folder has but memory does not (an undo took it back) has no records.
    /// </summary>
    private byte[]? ObjectBytes(ObjectKind kind, string name, ObjectFile? file)
    {
        var empty = file == null || file.Original.Count + file.Custom.Count == 0;
        if (empty && _baseline.GetValueOrDefault(name) == null)
        {
            return null;
        }

        return (file ?? ObjectFile.Empty(kind)).ToBytes();
    }

    /// <summary>Adds a file to the changes when its bytes differ from the baseline.</summary>
    private void AddChange(List<(string Name, byte[] Bytes)> changes, string name, byte[]? bytes)
    {
        if (bytes != null && !SameBytes(bytes, _baseline.GetValueOrDefault(name)))
        {
            changes.Add((name, bytes));
        }
    }

    private static bool SameBytes(byte[]? one, byte[]? other) =>
        one == null || other == null ? one == other : one.AsSpan().SequenceEqual(other);

    /// <summary>
    /// Copies the files' previous bytes into a new backup folder, named by the time, in
    /// <c>&lt;map folder&gt;.backup</c> beside the map folder; returns the new folder's path.
    /// </summary>
    private string Backup(List<(string Name, byte[] Bytes)> previous)
    {
        var root = Path.TrimEndingDirectorySeparator(_folder.Path) + ".backup";
        Directory.CreateDirectory(root);
        var time = DateTime.Now.ToString(BackupTime, CultureInfo.InvariantCulture);
        var taken = Backups(root).Where(backup => backup.Time == time).Select(backup => backup.Number);
        var number = taken.DefaultIfEmpty(0).Max() + 1;
        var folder = Path.Combine(root, number == 1 ? time : $"{time}-{number}");
        Directory.CreateDirectory(folder);
        foreach (var (name, bytes) in previous)
        {
            File.WriteAllBytes(Path.Combine(folder, name), bytes);
        }

        return folder;
    }

    /// <summary>
    /// Deletes the backup folders older than the newest 10, beside <paramref name="made"/>, the one this save just
    /// made. That one is always kept and counted, even when others are named later (the clock was set back since
    /// they were made). Only folders named as a backup are counted or deleted; one that cannot be deleted now is left
    /// for a later save.
    /// </summary>
    private static void Prune(string made)
    {
        var old = Backups(Path.GetDirectoryName(made)!)
            .Where(backup => !string.Equals(backup.Path, made, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(backup => backup.Time, StringComparer.Ordinal)
            .ThenByDescending(backup => backup.Number)
            .Skip(BackupsKept - 1);
        foreach (var backup in old)
        {
            try
            {
                Directory.Delete(backup.Path, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The save itself is done; an old backup that is in use goes next time.
            }
        }
    }

    /// <summary>
    /// The backup folders in <paramref name="root"/>: each one's path, its time as named, and its number in that
    /// second (1 for the name without a suffix).
    /// </summary>
    private static List<(string Path, string Time, int Number)> Backups(string root)
    {
        var backups = new List<(string Path, string Time, int Number)>();
        foreach (var path in Directory.GetDirectories(root))
        {
            var match = BackupName.Match(Path.GetFileName(path));
            if (match.Success)
            {
                var suffix = match.Groups[2];
                var number = suffix.Success ? int.Parse(suffix.Value, CultureInfo.InvariantCulture) : 1;
                backups.Add((path, match.Groups[1].Value, number));
            }
        }

        return backups;
    }
}

/// <summary>What a save did.</summary>
/// <param name="Written">
/// The files written, by name (<c>war3map.w3u</c>), in the order written; empty when none changed.
/// </param>
/// <param name="Backup">
/// The backup folder that holds the previous bytes of the files written; "" when none had any (nothing was written, or
/// only files the map did not have).
/// </param>
public sealed record SaveReport(IReadOnlyList<string> Written, string Backup);

/// <summary>
/// A save that was refused because the map's files changed on disk since they were read or last saved (another tool
/// wrote them). Nothing was written; <see cref="Session.Reload"/> reads them again.
/// </summary>
public sealed class ChangedOnDiskException : Exception
{
    /// <summary>The map in <paramref name="folder"/> changed on disk.</summary>
    public ChangedOnDiskException(string folder)
        : base($"The map's files in {folder} changed on disk since they were opened or last saved, so nothing was "
            + "saved. Reload the map to see the changes; edits that were not saved are then lost.")
    {
        Folder = folder;
    }

    /// <summary>The map folder.</summary>
    public string Folder { get; }
}
