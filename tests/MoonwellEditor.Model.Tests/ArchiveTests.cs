using MoonwellEditor.Files;
using War3Net.IO.Mpq;
using Xunit.Abstractions;

namespace MoonwellEditor.Model.Tests;

/// <summary>Maps stored as an archive (<c>.w3x</c>), and the errors a save or reload of any map can meet.</summary>
[Collection(GameDataCollection.Name)]
public sealed class ArchiveTests(GameDataFixture fixture, ITestOutputHelper output)
{
    // The archive's own files, which War3Net writes anew in every save.
    private static readonly string[] Specials = ["(listfile)", "(attributes)", "(signature)"];

    [Fact]
    public void Archive_map_opens_edits_and_saves()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.OfArchive();
        var before = Files(map.Map);
        var session = Session.Open(MapStore.Open(map.Map), game);
        Assert.Empty(session.Problems);

        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        session.Set(ObjectKind.Unit, "hfoo", "unam", 0, "Captain");   // the skin file
        var report = session.Save();

        Assert.Equal(new[] { "war3map.w3u", "war3mapSkin.w3u" }, report.Written);
        Assert.Equal("", report.Note);
        Assert.False(session.Dirty);
        var reopened = Session.Open(MapStore.Open(map.Map), game);
        Assert.Empty(reopened.Problems);
        Assert.Equal("500", Value(reopened, ObjectKind.Unit, "hfoo", "uhpm", 0));
        Assert.Equal("Captain", reopened.Object(ObjectKind.Unit, "hfoo")!.Name);

        // Every other file of the archive is as it was; the written ones hold what the session has.
        var after = Files(map.Map);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (name, bytes) in before.Where(file => !report.Written.Contains(file.Key)))
        {
            Assert.True(bytes.AsSpan().SequenceEqual(after[name]), $"{name} changed");
        }

        Assert.Equal(session.FileOf(ObjectKind.Unit, false)!.ToBytes(), after["war3map.w3u"]);
        Assert.Equal(session.FileOf(ObjectKind.Unit, true)!.ToBytes(), after["war3mapSkin.w3u"]);

        // Nothing is left beside the map but its backups; a save with nothing to write makes none.
        Assert.Equal(
            new[] { "Sample.w3x", "Sample.w3x.backup" },
            Directory.GetFileSystemEntries(map.Folder).Select(Path.GetFileName).Order());
        var empty = session.Save();
        Assert.Empty(empty.Written);
        Assert.Equal("", empty.Backup);
        Assert.Single(Directory.GetDirectories(map.Map + ".backup"));

        // The archive changed on disk (World Editor saved it): the save is refused, and a reload reads it again.
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "600");
        File.SetLastWriteTimeUtc(map.Map, DateTime.UtcNow.AddMinutes(1));
        Assert.Throws<ChangedOnDiskException>(() => session.Save());
        session.Reload();
        Assert.False(session.Dirty);
        Assert.Equal("500", Value(session, ObjectKind.Unit, "hfoo", "uhpm", 0));
    }

    [Fact]
    public void World_editors_own_archive_opens_edits_and_saves()
    {
        if (Skipped(out var game))
        {
            return;
        }

        // Saved by World Editor from the template map with no object edits: it has no object files, and no kind of it
        // is read-only.
        using var map = TempMap.OfArchive("we-heavy-archive/ObjectEditorTest.w3x");
        var before = Files(map.Map);
        var session = Session.Open(MapStore.Open(map.Map), game);
        Assert.Empty(session.Problems);
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            var standard = session.Tree(kind).First(entry => entry.BaseId == null);
            Assert.False(standard.Changed);
            Assert.Null(session.Object(kind, standard.Id)!.ReadOnly);
        }

        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        session.Set(ObjectKind.Unit, "hfoo", "unam", 0, "Captain");   // the skin file
        session.Set(ObjectKind.Ability, "AHhb", "atp1", 1, "Healing");   // a level, the skin file
        var created = session.Create(ObjectKind.Unit, "hfoo", null);
        var report = session.Save();

        Assert.Equal(
            new[] { "war3map.w3u", "war3mapSkin.w3a", "war3mapSkin.w3u" },
            report.Written.Order(StringComparer.Ordinal));
        Assert.Equal("", report.Note);
        Assert.False(session.Dirty);
        var reopened = Session.Open(MapStore.Open(map.Map), game);
        Assert.Empty(reopened.Problems);
        Assert.Equal("500", Value(reopened, ObjectKind.Unit, "hfoo", "uhpm", 0));
        Assert.Equal("Captain", reopened.Object(ObjectKind.Unit, "hfoo")!.Name);
        Assert.Equal("Healing", Value(reopened, ObjectKind.Ability, "AHhb", "atp1", 1));
        Assert.Equal("hfoo", reopened.Object(ObjectKind.Unit, created)!.BaseId);

        // Every other file of the archive is as it was, byte for byte; the written ones hold what the session has.
        var after = Files(map.Map);
        Assert.Equal(before.Keys.Concat(report.Written).Distinct().Order(), after.Keys.Order());
        foreach (var (name, bytes) in before.Where(file => !report.Written.Contains(file.Key)))
        {
            Assert.True(bytes.AsSpan().SequenceEqual(after[name]), $"{name} changed");
        }

        Assert.Equal(session.FileOf(ObjectKind.Unit, false)!.ToBytes(), after["war3map.w3u"]);
        Assert.Equal(session.FileOf(ObjectKind.Ability, true)!.ToBytes(), after["war3mapSkin.w3a"]);
    }

    [Fact]
    public void Archive_backup_holds_the_previous_archive()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.OfArchive();
        var backups = map.Map + ".backup";
        var oldest = Path.Combine(backups, "2000-01-01 00-00-00");
        Directory.CreateDirectory(oldest);
        var session = Session.Open(MapStore.Open(map.Map), game);

        // Seven saves, many in the same second: each backup holds the whole archive as it was before that save, named
        // with .bak after the map's own name.
        var made = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, (500 + i).ToString());
            var previous = File.ReadAllBytes(map.Map);
            var report = session.Save();
            output.WriteLine(report.Backup);
            Assert.Equal(backups, Path.GetDirectoryName(report.Backup));
            var copy = Assert.Single(Directory.GetFiles(report.Backup));
            Assert.Equal("Sample.w3x.bak", Path.GetFileName(copy));   // not a map the game's list shows
            Assert.Equal(previous, File.ReadAllBytes(copy));
            made.Add(report.Backup);
        }

        // The newest five are kept.
        Assert.Equal(7, made.Distinct().Count());
        Assert.Equal(5, Directory.GetDirectories(backups).Length);
        Assert.False(Directory.Exists(oldest));
        Assert.False(Directory.Exists(made[0]));
        Assert.False(Directory.Exists(made[1]));
        Assert.All(made.Skip(2), folder => Assert.True(Directory.Exists(folder), folder));

        // Five backups named later than now (the clock was set back since): the save's own backup still stays.
        using var other = TempMap.OfArchive();
        var later = Enumerable.Range(1, 5)
            .Select(i => Path.Combine(other.Map + ".backup", $"2999-01-{i:00} 00-00-00"))
            .ToList();
        later.ForEach(folder => Directory.CreateDirectory(folder));
        var otherSession = Session.Open(MapStore.Open(other.Map), game);
        otherSession.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        var kept = otherSession.Save();
        Assert.True(Directory.Exists(kept.Backup));
        Assert.Equal(5, Directory.GetDirectories(other.Map + ".backup").Length);
        Assert.False(Directory.Exists(later[0]));
    }

    [Fact]
    public void Archive_in_a_moonwell_build_folder_warns()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var built = TempMap.OfArchive(inside: Path.Combine("Sample", "build"));
        File.WriteAllText(Path.Combine(built.Root, "Sample", "Sample.csproj"), "<Project />\n");
        using var notProject = TempMap.OfArchive(inside: Path.Combine("Other", "build"));

        Assert.Equal(new[] { Session.BuildOutputNote }, Session.Open(MapStore.Open(built.Map), game).Problems);
        Assert.Empty(Session.Open(MapStore.Open(notProject.Map), game).Problems);
    }

    [Fact]
    public void Archive_saved_although_the_replace_reported_an_error_is_a_save_with_a_note()
    {
        if (Skipped(out var game))
        {
            return;
        }

        // Win32's ReplaceFile fails half way with the map gone (1176): the checked new archive is put in its place.
        using var map = TempMap.OfArchive();
        var store = MapArchive.Open(map.Map);
        store.ReplaceFile = (_, target, _) =>
        {
            File.Delete(target);
            throw new IOException("Simulated", unchecked((int)0x80070498));
        };
        var session = Session.Open(store, game);
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");

        var report = session.Save();

        Assert.Equal(new[] { "war3map.w3u" }, report.Written);
        Assert.Contains("was saved", report.Note);
        Assert.True(Directory.Exists(report.Backup));
        Assert.False(session.Dirty);
        Assert.Equal("500", Value(Session.Open(MapStore.Open(map.Map), game), ObjectKind.Unit, "hfoo", "uhpm", 0));

        // The baseline is what was written and the stamp the new archive's: the next save is not taken for a change
        // on disk.
        store.ReplaceFile = File.Replace;
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "600");
        var next = session.Save();
        Assert.Equal(new[] { "war3map.w3u" }, next.Written);
        Assert.Equal("", next.Note);
        Assert.Equal("600", Value(Session.Open(MapStore.Open(map.Map), game), ObjectKind.Unit, "hfoo", "uhpm", 0));
    }

    [Fact]
    public void Failed_archive_save_keeps_the_edits_unsaved()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.OfArchive();
        var bytes = File.ReadAllBytes(map.Map);
        var session = Session.Open(MapStore.Open(map.Map), game);
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");

        // World Editor holds the map: it can be read, not replaced.
        using (new FileStream(map.Map, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var problem = Assert.Throws<MapFileException>(() => session.Save());
            Assert.Equal(
                "The map is open in another program (World Editor?): close it there and save again.",
                problem.Message);
        }

        Assert.True(session.Dirty);
        Assert.Equal(bytes, File.ReadAllBytes(map.Map));

        // The backup of a save that left the map as it was is not kept (it would push out the older ones).
        Assert.Empty(Directory.GetDirectories(map.Map + ".backup"));

        // Once it is closed there, the save goes through: nothing was taken into the baseline.
        Assert.Equal(new[] { "war3map.w3u" }, session.Save().Written);
        Assert.False(session.Dirty);
        Assert.Single(Directory.GetDirectories(map.Map + ".backup"));
        Assert.Equal("500", Value(Session.Open(MapStore.Open(map.Map), game), ObjectKind.Unit, "hfoo", "uhpm", 0));
    }

    [Fact]
    public void Locked_or_missing_map_file_is_a_clear_error()
    {
        if (Skipped(out var game))
        {
            return;
        }

        // A folder map's file held by another program, with no sharing: the save and the reload cannot read it.
        using var folder = TempMap.Of("we-3.0");
        var session = Session.Open(MapStore.Open(folder.Map), game);
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        var units = Path.Combine(folder.Folder, "war3map.w3u");
        var unitBytes = File.ReadAllBytes(units);
        using (new FileStream(units, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var saving = Assert.Throws<MapFileException>(() => session.Save());
            Assert.StartsWith("The map file is gone or locked: ", saving.Message);
            var reloading = Assert.Throws<MapFileException>(() => session.Reload());
            Assert.StartsWith("The map file is gone or locked: ", reloading.Message);
        }

        Assert.True(session.Dirty);
        Assert.Equal(unitBytes, File.ReadAllBytes(units));
        Assert.Equal("500", Value(session, ObjectKind.Unit, "hfoo", "uhpm", 0));
        Assert.Equal(new[] { "war3map.w3u" }, session.Save().Written);

        // An archive moved away: the save is refused as changed on disk, and the reload cannot read it.
        using var archive = TempMap.OfArchive();
        var archived = Session.Open(MapStore.Open(archive.Map), game);
        archived.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        File.Move(archive.Map, archive.Map + ".moved");
        Assert.Throws<ChangedOnDiskException>(() => archived.Save());
        var gone = Assert.Throws<MapFileException>(() => archived.Reload());
        Assert.StartsWith("The map file is gone or locked: ", gone.Message);
        Assert.True(archived.Dirty);
        Assert.Equal("500", Value(archived, ObjectKind.Unit, "hfoo", "uhpm", 0));
    }

    [Fact]
    public void Failed_archive_save_keeps_its_backup_unless_the_map_is_known_to_be_as_it_was()
    {
        if (Skipped(out var game))
        {
            return;
        }

        // The save fails, and then the map cannot be read: whether it is as it was is not known, so the backup stays.
        using var map = TempMap.OfArchive();
        var store = new FailingStore(MapArchive.Open(map.Map));
        var session = Session.Open(store, game);
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        store.FailWrite = () => store.FailStamp = true;
        Assert.Throws<MapFileException>(() => session.Save());
        Assert.Single(Directory.GetDirectories(map.Map + ".backup"));
        Assert.True(session.Dirty);

        // Readable again and as it was: the next save goes through.
        store.FailStamp = false;
        store.FailWrite = null;
        Assert.Equal(new[] { "war3map.w3u" }, session.Save().Written);
        Assert.Equal(2, Directory.GetDirectories(map.Map + ".backup").Length);

        // The save fails and the map on disk is not as it was (another program wrote it meanwhile): the backup stays,
        // and the next save is refused as changed on disk, not taken for the map as the session last saw it.
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "600");
        store.FailWrite = () => File.SetLastWriteTimeUtc(map.Map, DateTime.UtcNow.AddMinutes(1));
        Assert.Throws<MapFileException>(() => session.Save());
        Assert.Equal(3, Directory.GetDirectories(map.Map + ".backup").Length);
        store.FailWrite = null;
        Assert.Throws<ChangedOnDiskException>(() => session.Save());
    }

    [Fact]
    public void Archive_changed_after_the_save_checked_it_is_refused_as_changed_on_disk()
    {
        if (Skipped(out var game))
        {
            return;
        }

        // World Editor saves the map after the session checked its stamp, before the archive's save holds it.
        using var map = TempMap.OfArchive();
        var store = new FailingStore(MapArchive.Open(map.Map));
        var session = Session.Open(store, game);
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        store.BeforeWrite = () => File.SetLastWriteTimeUtc(map.Map, DateTime.UtcNow.AddMinutes(1));
        var bytes = File.ReadAllBytes(map.Map);

        Assert.Throws<ChangedOnDiskException>(() => session.Save());
        Assert.Equal(bytes, File.ReadAllBytes(map.Map));
        Assert.True(session.Dirty);
        store.BeforeWrite = null;
        Assert.Throws<ChangedOnDiskException>(() => session.Save());
    }

    [Fact]
    public void File_under_another_locale_is_read_only()
    {
        if (Skipped(out var game))
        {
            return;
        }

        // we-3.0's files in an archive: war3map.w3a and war3map.wts under the German locale (0x407) only, every other
        // file under the neutral one. A save would write a neutral copy beside each.
        using var map = TempMap.Of(null);
        var path = Path.Combine(map.Folder, "localized.w3x");
        var german = (MpqLocale)0x407;
        BuildArchive(path, "we-3.0", name => name is "war3map.w3a" or "war3map.wts" ? german : MpqLocale.Neutral);
        var wts = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "we-3.0", "war3map.wts"));

        var session = Session.Open(MapStore.Open(path), game);
        Assert.Equal(2, session.Problems.Count);
        Assert.Contains(session.Problems, problem => problem.Contains("war3map.w3a") && problem.Contains("0x0407"));
        Assert.Contains(session.Problems, problem => problem.Contains("war3map.wts") && problem.Contains("0x0407"));
        Assert.Equal(
            "readOnly",
            Assert.Throws<EditException>(() => session.Set(ObjectKind.Ability, "AHhb", "atp1", 1, "Heal")).Code);

        // The strings file is never written: a name that is a TRIGSTR in it is written into the object file.
        session.Set(ObjectKind.Unit, "h000", "unam", 0, "Knight");
        Assert.Equal("Knight", session.Object(ObjectKind.Unit, "h000")!.Name);
        var report = session.Save();
        Assert.Equal(new[] { "war3mapSkin.w3u" }, report.Written);

        var reopened = Session.Open(MapStore.Open(path), game);
        Assert.Equal("Knight", reopened.Object(ObjectKind.Unit, "h000")!.Name);
        using var archive = MpqArchive.Open(new MemoryStream(File.ReadAllBytes(path)), loadListFile: true);
        var strings = archive.EnumerateHashes()
            .Where(hash => hash.Name == MpqHash.GetHashedFileName("war3map.wts"))
            .ToList();
        Assert.Equal(german, Assert.Single(strings).Locale);
        using var stream = archive.OpenFile(archive[(int)strings[0].BlockIndex]);
        var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        Assert.Equal(wts, bytes.ToArray());
    }

    // Writes a fixture folder's map files into a new archive, each under the locale given for its name.
    private static void BuildArchive(string path, string fixture, Func<string, MpqLocale> locale)
    {
        var files = new List<MpqFile>();
        var folder = Path.Combine(AppContext.BaseDirectory, "fixtures", fixture);
        foreach (var file in Directory.GetFiles(folder, "war3map*"))
        {
            var name = Path.GetFileName(file);
            files.Add(MpqFile.New(new MemoryStream(File.ReadAllBytes(file)), name, locale(name)));
        }

        using var archive = new MemoryStream();
        MpqArchive.Create(archive, files, new MpqArchiveCreateOptions(), leaveOpen: true).Dispose();
        File.WriteAllBytes(path, archive.ToArray());
    }

    // Every named file of an archive and its bytes, but the archive's own files.
    private static Dictionary<string, byte[]> Files(string path)
    {
        var archive = MapArchive.Open(path);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in archive.Names.Where(name => !Specials.Contains(name)))
        {
            files[name] = archive.Read(name)!;
        }

        return files;
    }

    /// <summary>
    /// A map archive whose write fails (after running <see cref="FailWrite"/>) while that is set, and whose stamp
    /// cannot be read while <see cref="FailStamp"/> is true.
    /// </summary>
    private sealed class FailingStore(MapArchive archive) : IMapStore
    {
        public Action? FailWrite { get; set; }

        public Action? BeforeWrite { get; set; }

        public bool FailStamp { get; set; }

        public string Path => archive.Path;

        public bool IsArchive => true;

        public byte[]? Read(string name) => archive.Read(name);

        public string? WriteProblem(string name) => archive.WriteProblem(name);

        public void Write(IReadOnlyList<(string Name, byte[]? Bytes)> files, string? stamp = null)
        {
            if (FailWrite is { } fail)
            {
                fail();
                throw new IOException("Simulated");
            }

            BeforeWrite?.Invoke();
            archive.Write(files, stamp);
        }

        public string Stamp() =>
            FailStamp ? throw new IOException("Simulated: the map cannot be read") : archive.Stamp();
    }

    private static string Value(Session session, ObjectKind kind, string id, string field, int level) =>
        Assert.Single(
            session.Object(kind, id)!.Fields, candidate => candidate.Id == field && candidate.Level == level).Value;

    private bool Skipped(out GameData.GameData game)
    {
        if (fixture.Data == null)
        {
            output.WriteLine(fixture.Reason);
            game = null!;
            return true;
        }

        game = fixture.Data;
        return false;
    }
}
