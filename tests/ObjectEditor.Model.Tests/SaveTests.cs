using ObjectEditor.Files;
using Xunit.Abstractions;

namespace ObjectEditor.Model.Tests;

[Collection(GameDataCollection.Name)]
public sealed class SaveTests(GameDataFixture fixture, ITestOutputHelper output)
{
    private static readonly DateTime Old = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Save_writes_only_changed_files()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var paths = Directory.GetFiles(map.Folder);
        var before = paths.ToDictionary(path => path, File.ReadAllBytes);
        foreach (var path in paths)
        {
            File.SetLastWriteTimeUtc(path, Old);
        }

        var session = Open(map, game);
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        session.Set(ObjectKind.Ability, "AHhb", "acdn", 1, "5");   // changed, then back to its bytes as read
        session.Reset(ObjectKind.Ability, "AHhb", "acdn", 1);

        var report = session.Save();

        Assert.Equal(new[] { "war3map.w3u" }, report.Written);
        var units = Path.Combine(map.Folder, "war3map.w3u");
        Assert.Equal(session.FileOf(ObjectKind.Unit, false)!.ToBytes(), File.ReadAllBytes(units));
        foreach (var path in paths.Where(path => path != units))
        {
            Assert.Equal(before[path], File.ReadAllBytes(path));
            Assert.Equal(Old, File.GetLastWriteTimeUtc(path));
        }

        Assert.Equal(paths.Order(), Directory.GetFiles(map.Folder).Order());

        // The backup sits beside the map folder and holds the file's bytes as they were.
        output.WriteLine(report.Backup);
        Assert.Equal(map.Folder + ".backup", Path.GetDirectoryName(report.Backup));
        var backedUp = Assert.Single(Directory.GetFiles(report.Backup));
        Assert.Equal("war3map.w3u", Path.GetFileName(backedUp));
        Assert.Equal(before[units], File.ReadAllBytes(backedUp));
    }

    [Fact]
    public void Saved_edit_reads_back()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var opened = Open(map, game);
        var session = Open(map, game);
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");          // a new entry in war3map.w3u
        session.Set(ObjectKind.Unit, "h000", "unam", 0, "Knight");       // entry 12 of war3map.wts
        session.Set(ObjectKind.Ability, "A000", "acdn", 1, "5");         // the map's custom ability
        session.Set(ObjectKind.Doodad, "D000", "dvr1", 1, "100");        // a doodad's variation

        session.Save();
        var reopened = Open(map, game);

        Assert.Empty(reopened.Problems);
        Assert.Equal("500", Value(reopened, ObjectKind.Unit, "hfoo", "uhpm", 0));
        Assert.Equal("Knight", reopened.Object(ObjectKind.Unit, "h000")!.Name);
        Assert.Equal("5", Value(reopened, ObjectKind.Ability, "A000", "acdn", 1));
        Assert.Equal("100", Value(reopened, ObjectKind.Doodad, "D000", "dvr1", 1));

        // Every other modification is as it was, in the same order; so is every other strings entry.
        string[] edited = ["Unit main hfoo uhpm 0 ", "Ability main A000 acdn 1 ", "Doodad main D000 dvr1 1 "];
        bool Other(string line) => !edited.Any(line.StartsWith);
        Assert.Equal(Mods(opened).Where(Other), Mods(reopened).Where(Other));
        Assert.Equal(edited.Length, Mods(reopened).Count(line => !Other(line)));
        for (var number = 0; number < 40; number++)
        {
            if (number != 12)
            {
                Assert.Equal(opened.Strings!.Get(number), reopened.Strings!.Get(number));
            }
        }
    }

    [Fact]
    public void Save_creates_missing_skin_file()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of(null);
        var session = Open(map, game);
        session.Set(ObjectKind.Unit, "hfoo", "unam", 0, "Captain");     // the skin file only
        session.Set(ObjectKind.Ability, "AHhb", "acdn", 1, "5");        // makes war3map.w3a ...
        session.Reset(ObjectKind.Ability, "AHhb", "acdn", 1);           // ... which is left with no records

        var report = session.Save();

        Assert.Equal(new[] { "war3mapSkin.w3u" }, report.Written);
        Assert.Equal("", report.Backup);   // a new file has nothing to back up
        Assert.False(Directory.Exists(map.Folder + ".backup"));
        Assert.Equal(new[] { "war3mapSkin.w3u" }, Directory.GetFiles(map.Folder).Select(Path.GetFileName));
        var reopened = Open(map, game);
        Assert.Empty(reopened.Problems);
        Assert.Equal("Captain", reopened.Object(ObjectKind.Unit, "hfoo")!.Name);

        // Undone back to no skin file in memory: the saved file is emptied, not deleted.
        for (var i = 0; i < 3; i++)
        {
            Assert.True(session.Undo());
        }

        Assert.Null(session.FileOf(ObjectKind.Unit, true));
        Assert.Equal(new[] { "war3mapSkin.w3u" }, session.Save().Written);
        var path = Path.Combine(map.Folder, "war3mapSkin.w3u");
        Assert.Equal(ObjectFile.Empty(ObjectKind.Unit).ToBytes(), File.ReadAllBytes(path));
        Assert.Equal("Footman", Open(map, game).Object(ObjectKind.Unit, "hfoo")!.Name);
    }

    [Fact]
    public void Backup_keeps_ten()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var backups = map.Folder + ".backup";
        var oldest = Path.Combine(backups, "2000-01-01 00-00-00");
        var notes = Path.Combine(backups, "notes");
        Directory.CreateDirectory(oldest);
        Directory.CreateDirectory(notes);
        var session = Open(map, game);
        var units = Path.Combine(map.Folder, "war3map.w3u");

        // Twelve saves, many in the same second.
        var made = new List<string>();
        byte[] last = [];
        for (var i = 0; i < 12; i++)
        {
            session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, (500 + i).ToString());
            last = File.ReadAllBytes(units);
            made.Add(session.Save().Backup);
        }

        foreach (var folder in Directory.GetDirectories(backups))
        {
            output.WriteLine(Path.GetFileName(folder));
        }

        Assert.Equal(12, made.Distinct().Count());
        Assert.Equal(11, Directory.GetDirectories(backups).Length);   // the newest ten, and notes
        Assert.True(Directory.Exists(notes));
        Assert.False(Directory.Exists(oldest));
        Assert.False(Directory.Exists(made[0]));
        Assert.False(Directory.Exists(made[1]));
        Assert.All(made.Skip(2), folder => Assert.True(Directory.Exists(folder), folder));
        Assert.Equal(last, File.ReadAllBytes(Path.Combine(made[^1], "war3map.w3u")));
    }

    [Fact]
    public void Backup_just_made_is_kept_when_later_ones_exist()
    {
        if (Skipped(out var game))
        {
            return;
        }

        // Ten backups named later than now (the clock was set back since): the save's own backup still stays.
        using var map = TempMap.Of("we-3.0");
        var backups = map.Folder + ".backup";
        var later = Enumerable.Range(1, 10)
            .Select(i => Path.Combine(backups, $"2999-01-{i:00} 00-00-00"))
            .ToList();
        later.ForEach(folder => Directory.CreateDirectory(folder));
        var session = Open(map, game);
        var units = File.ReadAllBytes(Path.Combine(map.Folder, "war3map.w3u"));

        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        var report = session.Save();

        Assert.True(Directory.Exists(report.Backup));
        Assert.Equal(units, File.ReadAllBytes(Path.Combine(report.Backup, "war3map.w3u")));
        Assert.Equal(10, Directory.GetDirectories(backups).Length);   // the new one and the newest nine
        Assert.False(Directory.Exists(later[0]));
        Assert.All(later.Skip(1), folder => Assert.True(Directory.Exists(folder), folder));
    }

    [Fact]
    public void Save_refuses_when_changed_on_disk()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var units = Path.Combine(map.Folder, "war3map.w3u");
        var strings = Path.Combine(map.Folder, "war3map.wts");
        var unitBytes = File.ReadAllBytes(units);
        var session = Open(map, game);
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");

        // Another tool renames h000 in war3map.wts.
        var text = File.ReadAllText(strings).Replace("{\r\nUnit\r\n}", "{\r\nGrunt\r\n}");
        File.WriteAllText(strings, text);
        var stringBytes = File.ReadAllBytes(strings);

        Assert.Throws<ChangedOnDiskException>(() => session.Save());
        Assert.Equal(unitBytes, File.ReadAllBytes(units));
        Assert.Equal(stringBytes, File.ReadAllBytes(strings));
        Assert.False(Directory.Exists(map.Folder + ".backup"));
        Assert.True(session.Dirty);

        session.Reload();

        Assert.False(session.Dirty);
        Assert.False(session.Undo());
        Assert.Equal("Grunt", session.Object(ObjectKind.Unit, "h000")!.Name);
        Assert.Equal("420", Value(session, ObjectKind.Unit, "hfoo", "uhpm", 0));
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "600");
        Assert.Equal(new[] { "war3map.w3u" }, session.Save().Written);

        // A file whose bytes changed under the same write time is refused too.
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "700");
        var time = File.GetLastWriteTimeUtc(units);
        File.WriteAllBytes(units, unitBytes);
        File.SetLastWriteTimeUtc(units, time);
        Assert.Throws<ChangedOnDiskException>(() => session.Save());
        Assert.Equal(unitBytes, File.ReadAllBytes(units));
    }

    [Fact]
    public void Dirty_clears_after_save()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var units = Path.Combine(map.Folder, "war3map.w3u");
        var unitBytes = File.ReadAllBytes(units);
        var session = Open(map, game);

        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        Assert.True(session.Dirty);
        session.Save();
        Assert.False(session.Dirty);
        Assert.Empty(session.Save().Written);   // nothing new to write

        // Undo after a save is an edit like any other.
        Assert.True(session.Undo());
        Assert.True(session.Dirty);
        Assert.Equal(new[] { "war3map.w3u" }, session.Save().Written);
        Assert.False(session.Dirty);
        Assert.Equal(unitBytes, File.ReadAllBytes(units));
        Assert.True(session.Redo());
        Assert.True(session.Dirty);
    }

    [Fact]
    public void Changed_objects_counts_objects_that_differ_from_the_last_save()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Open(map, game);
        Assert.Equal(0, session.ChangedObjects());

        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        session.Set(ObjectKind.Unit, "hfoo", "umvs", 0, "300");
        session.Set(ObjectKind.Unit, "h000", "unam", 0, "Knight");   // a TRIGSTR text: only war3map.wts changes
        session.Create(ObjectKind.Item, "ratc", null);               // a new object, with no modifications yet
        Assert.Equal(3, session.ChangedObjects());

        Assert.True(session.Undo());
        Assert.Equal(2, session.ChangedObjects());

        session.Save();
        Assert.Equal(0, session.ChangedObjects());

        // Changed and changed back: dirty, but nothing differs from the save.
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "600");
        Assert.Equal(1, session.ChangedObjects());
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        Assert.True(session.Dirty);
        Assert.Equal(0, session.ChangedObjects());

        session.Delete(ObjectKind.Unit, "h000");
        Assert.Equal(1, session.ChangedObjects());
        session.Reload();
        Assert.Equal(0, session.ChangedObjects());
    }

    [Fact]
    public void Read_only_kind_is_never_written()
    {
        if (Skipped(out var game))
        {
            return;
        }

        // War3Net reads this war3map.w3a but (in this test) would drop its last 4 bytes on writing it back. An edit to
        // its records in memory makes them differ from the file, so a save would write it if read-only kinds were not
        // skipped.
        using var map = TempMap.Of("moonwell-mymap");
        var abilities = Path.Combine(map.Folder, "war3map.w3a");
        var lossy = File.ReadAllBytes(abilities);
        var skin = File.ReadAllBytes(Path.Combine(map.Folder, "war3mapSkin.w3a"));
        var session = Lossy.Open(map.Folder, game);
        var inMemory = session.FileOf(ObjectKind.Ability, false)!;
        inMemory.Custom[0].Set("Htb1", 1, 1, new FieldValue.Real(999));
        Assert.NotEqual(lossy, inMemory.ToBytes());

        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        var report = session.Save();

        // A reload reads the file the same lossy way: still read only.
        session.Reload();
        Assert.NotNull(session.Object(ObjectKind.Ability, "A001")!.ReadOnly);

        Assert.Equal(new[] { "war3map.w3u" }, report.Written);
        Assert.Equal(lossy, File.ReadAllBytes(abilities));
        Assert.Equal(skin, File.ReadAllBytes(Path.Combine(map.Folder, "war3mapSkin.w3a")));
    }

    private static Session Open(TempMap map, GameData.GameData game) => Session.Open(MapFolder.Open(map.Folder), game);

    private static string Value(Session session, ObjectKind kind, string id, string field, int level) =>
        Assert.Single(
            session.Object(kind, id)!.Fields, candidate => candidate.Id == field && candidate.Level == level).Value;

    /// <summary>Every modification of every object file, one line each, in file order.</summary>
    private static List<string> Mods(Session session)
    {
        var lines = new List<string>();
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            foreach (var skin in new[] { false, true })
            {
                var file = session.FileOf(kind, skin);
                foreach (var entry in (file?.Original ?? []).Concat(file?.Custom ?? []))
                {
                    var id = entry.Id == "" ? entry.BaseId : entry.Id;
                    foreach (var mod in entry.Mods)
                    {
                        lines.Add($"{kind} {(skin ? "skin" : "main")} {id} {mod.Field} {mod.Level} "
                            + $"from {entry.BaseId} at {mod.Pointer}: {mod.Value}");
                    }
                }
            }
        }

        return lines;
    }

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
