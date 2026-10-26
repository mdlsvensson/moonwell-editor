using System.Globalization;
using System.Text.RegularExpressions;
using MoonwellEditor.Files;

namespace MoonwellEditor.Model;

/// <summary>
/// Saving the map's files, and reloading them from disk. A save writes only the files whose bytes changed since they
/// were read (or last saved), after a backup beside the map: for a folder, the previous bytes of the files it replaces;
/// for an archive, the whole previous archive.
/// </summary>
public sealed partial class Session
{
    /// <summary>How many backup folders of a map folder are kept.</summary>
    private const int BackupsKept = 10;

    /// <summary>How many backup folders of a map archive are kept (each holds the whole archive).</summary>
    private const int ArchiveBackupsKept = 5;

    /// <summary>What a save or reload says first when the map on disk cannot be read.</summary>
    private const string Unavailable = "The map file is gone or locked: ";

    /// <summary>
    /// A backup folder's name: the save's local time, then <c>-2</c>, <c>-3</c> ... for more saves in that second.
    /// </summary>
    private const string BackupTime = "yyyy-MM-dd HH-mm-ss";

    private static readonly Regex BackupName = new(@"^(\d{4}-\d{2}-\d{2} \d{2}-\d{2}-\d{2})(?:-(\d{1,9}))?$");

    /// <summary>
    /// Saves the edits: writes each object file and the strings file whose bytes differ from what was read at open
    /// (or written by the last save). A kind whose files are a problem (see <see cref="Problems"/>) is never written; a
    /// file the map did not have is made only when it has records; a file the map has is never deleted (one an undo
    /// took back to none is written with no records). Afterwards <see cref="Dirty"/> is false and undo still works.
    /// <para>
    /// A folder's files are written one at a time, each through a temporary file that then replaces it; the previous
    /// bytes of each file that is replaced go first into a new folder of <c>&lt;map folder&gt;.backup</c>, which keeps
    /// the newest 10. An archive is written once, with every changed file (see <see cref="MapArchive.Write"/>); the
    /// whole previous archive is copied first into a new folder of <c>&lt;archive&gt;.backup</c>, which keeps the
    /// newest 5, as <c>&lt;archive name&gt;.bak</c> (so the game's map list does not show it). An archive that was
    /// saved although replacing the old one reported an error is a save, with that error as the report's
    /// <see cref="SaveReport.Note"/>.
    /// </para>
    /// </summary>
    /// <exception cref="ChangedOnDiskException">
    /// The map's files changed on disk since they were read or last saved; nothing was written.
    /// </exception>
    /// <exception cref="MapFileException">
    /// The map could not be read or written (a file gone or locked, a backup that could not be made, an archive's save
    /// that failed); the message says why. The edits that were not written stay unsaved.
    /// </exception>
    public SaveReport Save()
    {
        if (StampNow() != _stamp)
        {
            throw new ChangedOnDiskException(_store.Path);
        }

        var changes = Changes();

        // The previous bytes, from disk. In a folder, a file whose bytes are not the ones read changed even if its time
        // did not. An archive gives the bytes it read while its stamp is the same, so there only the stamp sees a
        // change.
        var previous = new List<(string Name, byte[] Bytes)>();
        foreach (var (name, _) in changes)
        {
            var onDisk = ReadNow(name);
            if (!SameBytes(onDisk, _baseline.GetValueOrDefault(name)))
            {
                throw new ChangedOnDiskException(_store.Path);
            }

            if (onDisk != null)
            {
                previous.Add((name, onDisk));
            }
        }

        var (written, backup, note) = _store.IsArchive ? SaveArchive(changes) : SaveFolder(changes, previous);
        _history.Saved();
        _savedObjects = Describe();
        return new SaveReport(written, backup, note);
    }

    /// <summary>
    /// Reads the map again, as <see cref="Open(IMapStore, GameData.GameData)"/> does (with the same reader): the edits
    /// that were not saved are dropped, and so is the undo history.
    /// </summary>
    /// <exception cref="MapFileException">
    /// The map cannot be read (gone, locked, or an archive War3Net cannot read now); the session is as it was.
    /// </exception>
    public void Reload()
    {
        Loaded loaded;
        try
        {
            loaded = Load(_store, _read);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or MapArchiveException)
        {
            throw new MapFileException(Unavailable + e.Message, e);
        }

        _kinds = loaded.Kinds;
        _strings = loaded.Strings;
        _problems = loaded.Problems;
        _baseline = loaded.Baseline;
        _stamp = loaded.Stamp;
        _unreadable = loaded.Unreadable;
        _stringsReadOnly = loaded.StringsReadOnly;
        _history = new History();
        _search.Rebuild(_kinds, _strings);
        _savedObjects = Describe();
    }

    /// <summary>
    /// Writes a folder's changed files one at a time, after backing up their previous bytes. A file written goes into
    /// the baseline at once, so a write that fails half way leaves the files written before it saved.
    /// </summary>
    private (List<string> Written, string Backup, string Note) SaveFolder(
        List<(string Name, byte[] Bytes)> changes, List<(string Name, byte[] Bytes)> previous)
    {
        var backup = previous.Count == 0 ? "" : Backup(folder =>
        {
            foreach (var (name, bytes) in previous)
            {
                File.WriteAllBytes(Path.Combine(folder, name), bytes);
            }
        });

        var written = new List<string>();
        try
        {
            foreach (var (name, bytes) in changes)
            {
                try
                {
                    _store.Write([(name, bytes)]);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    var before = written.Count == 0 ? "" : $" ({string.Join(", ", written)} saved before it)";
                    throw new MapFileException($"{name} was not saved{before}: {e.Message}", e);
                }

                _baseline[name] = bytes;
                written.Add(name);
            }
        }
        finally
        {
            // Also after a failed write, so the files written so far are not taken for a change on disk.
            _stamp = TryStamp() ?? _stamp;
        }

        if (backup != "")
        {
            Prune(backup, BackupsKept);
        }

        return (written, backup, "");
    }

    /// <summary>
    /// Writes an archive's changed files in one save, after copying the whole archive into a backup. A save that put
    /// the new archive in place although the replace reported an error is a save, with that error as its note. Any
    /// other failure leaves the baseline and the stamp as they were; the backup is then deleted only when the map's
    /// stamp could be read again and is the one from before the save (the map is as it was).
    /// </summary>
    private (List<string> Written, string Backup, string Note) SaveArchive(List<(string Name, byte[] Bytes)> changes)
    {
        if (changes.Count == 0)
        {
            return ([], "", "");
        }

        var map = _store.Path;
        var backup = Backup(folder => File.Copy(map, Path.Combine(folder, Path.GetFileName(map) + ".bak")));
        var note = "";
        try
        {
            _store.Write([.. changes.Select(change => (change.Name, (byte[]?)change.Bytes))], _stamp);
        }
        catch (MapArchiveException e) when (e.Saved)
        {
            note = e.Message;
        }
        catch (Exception e) when (e is MapArchiveException or IOException or UnauthorizedAccessException)
        {
            // The stamp is kept: a map that is not as it was is then a change on disk at the next save.
            if (TryStamp() == _stamp)
            {
                // The map is as it was: its backup would only push out an older one.
                DeleteFolder(backup);
            }

            if (e is MapArchiveException { ChangedOnDisk: true })
            {
                throw new ChangedOnDiskException(_store.Path);
            }

            throw new MapFileException(e.Message, e);
        }

        foreach (var (name, bytes) in changes)
        {
            _baseline[name] = bytes;
        }

        _stamp = TryStamp() ?? _stamp;
        Prune(backup, ArchiveBackupsKept);
        return ([.. changes.Select(change => change.Name)], backup, note);
    }

    /// <summary>The map's stamp now; a map that cannot be read gives a <see cref="MapFileException"/>.</summary>
    private string StampNow()
    {
        try
        {
            return _store.Stamp();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or MapArchiveException)
        {
            throw new MapFileException(Unavailable + e.Message, e);
        }
    }

    /// <summary>
    /// The map's stamp now, or null when it cannot be read. After a save that wrote, a null keeps the stamp from
    /// before: the next save then reads the map again, and takes a change for a change on disk.
    /// </summary>
    private string? TryStamp()
    {
        try
        {
            return _store.Stamp();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or MapArchiveException)
        {
            return null;
        }
    }

    /// <summary>A file's bytes on disk now; a map that cannot be read gives a <see cref="MapFileException"/>.</summary>
    private byte[]? ReadNow(string name)
    {
        try
        {
            return _store.Read(name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or MapArchiveException)
        {
            throw new MapFileException(Unavailable + e.Message, e);
        }
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

        if (!_stringsReadOnly)
        {
            AddChange(changes, StringsName, _strings?.ToBytes());
        }

        return changes;
    }

    /// <summary>
    /// An object file's bytes to save; null when there is nothing to save: no file, or one with no records, that the
    /// map does not have either. A file the map has but memory does not (an undo took it back) has no records.
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
    /// Makes a new backup folder, named by the time, in <c>&lt;map&gt;.backup</c> beside the map (the folder or the
    /// archive file), and fills it with <paramref name="fill"/>; returns the new folder's path. A backup that cannot
    /// be made stops the save before anything is written.
    /// </summary>
    private string Backup(Action<string> fill)
    {
        var root = Path.TrimEndingDirectorySeparator(_store.Path) + ".backup";
        string? folder = null;
        try
        {
            Directory.CreateDirectory(root);
            var time = DateTime.Now.ToString(BackupTime, CultureInfo.InvariantCulture);
            var taken = Backups(root).Where(backup => backup.Time == time).Select(backup => backup.Number);
            var number = taken.DefaultIfEmpty(0).Max() + 1;
            folder = Path.Combine(root, number == 1 ? time : $"{time}-{number}");
            Directory.CreateDirectory(folder);
            fill(folder);
            return folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (folder != null)
            {
                DeleteFolder(folder);
            }

            throw new MapFileException($"Nothing was saved: the backup in {root} could not be made ({e.Message})", e);
        }
    }

    /// <summary>Deletes a folder and what it holds when it can; one that is in use is left.</summary>
    private static void DeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Deletes the backup folders older than the newest <paramref name="kept"/>, beside <paramref name="made"/>, the
    /// one this save just made. That one is always kept and counted, even when others are named later (the clock was
    /// set back since they were made). Only folders named as a backup are counted or deleted; one that cannot be
    /// deleted now is left for a later save.
    /// </summary>
    private static void Prune(string made, int kept)
    {
        var old = Backups(Path.GetDirectoryName(made)!)
            .Where(backup => !string.Equals(backup.Path, made, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(backup => backup.Time, StringComparer.Ordinal)
            .ThenByDescending(backup => backup.Number)
            .Skip(kept - 1);
        foreach (var backup in old)
        {
            // The save itself is done; an old backup that is in use goes next time.
            DeleteFolder(backup.Path);
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
/// The backup folder: for a folder map it holds the previous bytes of the files written, for an archive the whole
/// previous archive (<c>&lt;archive name&gt;.bak</c>). "" when there is none (nothing was written, or only files a
/// folder map did not have).
/// </param>
/// <param name="Note">
/// What the user should know about a save that worked: for an archive saved although replacing the old one reported
/// an error, that error. "" for none.
/// </param>
public sealed record SaveReport(IReadOnlyList<string> Written, string Backup, string Note);

/// <summary>
/// A save or reload that could not read or write the map on disk: a file gone or locked, a backup that could not be
/// made, or an archive's save that failed (and left the map as it was). The message says why. The edits that were not
/// written stay unsaved; a failed reload changes nothing.
/// </summary>
/// <param name="message">Why, as the user sees it.</param>
/// <param name="inner">The error that caused it.</param>
public sealed class MapFileException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// A save that was refused because the map's files changed on disk since they were read or last saved (another tool
/// wrote them). Nothing was written; <see cref="Session.Reload"/> reads them again.
/// </summary>
public sealed class ChangedOnDiskException : Exception
{
    /// <summary>The map at <paramref name="folder"/> (a folder or an archive file) changed on disk.</summary>
    public ChangedOnDiskException(string folder)
        : base($"The map's files in {folder} changed on disk since they were opened or last saved, so nothing was "
            + "saved. Reload the map to see the changes; edits that were not saved are then lost.")
    {
        Folder = folder;
    }

    /// <summary>The map: the folder, or the archive file's path.</summary>
    public string Folder { get; }
}
