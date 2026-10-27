using System.Globalization;
using MoonwellEditor.Files;
using Xunit.Abstractions;

namespace MoonwellEditor.Model.Tests;

[Collection(GameDataCollection.Name)]
public sealed class TableTests(GameDataFixture fixture, ITestOutputHelper output)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void Set_cells_changes_every_cell()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("moonwell-mymap");
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        var units = session.SetCells(ObjectKind.Unit, [
            new CellEdit("H001", "uhpm", 0, "1234"),
            new CellEdit("u001", "uhpm", 0, "1235"),
            new CellEdit("H002", "uhpm", 0, "1236"),
        ]);
        var abilities = session.SetCells(ObjectKind.Ability, [
            new CellEdit("A001", "acdn", 1, "7.5"),
            new CellEdit("A001", "acdn", 2, "8.5"),
            new CellEdit("A001", "acdn", 3, "9.5"),
        ]);

        Assert.Equal(3, units);
        Assert.Equal(3, abilities);
        Assert.Equal("1234", Value(session, ObjectKind.Unit, "H001", "uhpm", 0));
        Assert.Equal("1235", Value(session, ObjectKind.Unit, "u001", "uhpm", 0));
        Assert.Equal("1236", Value(session, ObjectKind.Unit, "H002", "uhpm", 0));
        Assert.Equal("7.5", Value(session, ObjectKind.Ability, "A001", "acdn", 1));
        Assert.Equal("8.5", Value(session, ObjectKind.Ability, "A001", "acdn", 2));
        Assert.Equal("9.5", Value(session, ObjectKind.Ability, "A001", "acdn", 3));
        Assert.True(session.Dirty);
    }

    [Fact]
    public void Set_cells_undoes_in_one_step()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("moonwell-mymap");
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        session.SetCells(ObjectKind.Unit, [
            new CellEdit("H001", "uhpm", 0, "1234"),
            new CellEdit("u001", "unam", 0, "Bruiser"),
            new CellEdit("hpea", "udef", 0, null),
            new CellEdit("hfoo", "uhpm", 0, "999"),
        ]);
        var after = Snapshot(session);

        Assert.True(session.Undo());

        Assert.False(session.Dirty);
        Assert.Equal(OnDisk(map), Snapshot(session));
        Assert.False(session.Undo());

        Assert.True(session.Redo());
        Assert.Equal(after, Snapshot(session));
        Assert.Equal("1234", Value(session, ObjectKind.Unit, "H001", "uhpm", 0));
        Assert.Equal("Bruiser", session.Object(ObjectKind.Unit, "u001")!.Name);
        Assert.Equal("999", Value(session, ObjectKind.Unit, "hfoo", "uhpm", 0));
        Assert.False(session.Redo());
    }

    [Fact]
    public void Set_cells_is_all_or_nothing()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("moonwell-mymap");
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        // Text in a number field, at the third cell.
        var notNumber = Refused(() => session.SetCells(ObjectKind.Unit, [
            new CellEdit("H001", "uhpm", 0, "1234"),
            new CellEdit("u001", "uhpm", 0, "1235"),
            new CellEdit("H002", "uhpm", 0, "lots"),
        ]));
        Assert.Equal("notNumber", notNumber.Code);
        Named(notNumber, "H002", "uhpm");
        Unchanged(session, map);

        // A field that does not apply to the object (a hero's strength on a footman), at the second cell.
        var noField = Refused(() => session.SetCells(ObjectKind.Unit, [
            new CellEdit("H001", "uhpm", 0, "1234"),
            new CellEdit("u001", "ustr", 0, "5"),
        ]));
        Assert.Equal("noField", noField.Code);
        Named(noField, "u001", "ustr");
        Unchanged(session, map);

        // A level the object does not have.
        var noLevel = Refused(() => session.SetCells(ObjectKind.Ability, [
            new CellEdit("A001", "acdn", 1, "5"),
            new CellEdit("A001", "acdn", 99, "5"),
        ]));
        Assert.Equal("noField", noLevel.Code);
        Named(noLevel, "A001", "acdn");
        Unchanged(session, map);

        // Text a strings file entry cannot hold, found only when the cell is written: the cells before it are taken
        // back too.
        using var named = TempMap.Of("moonwell-mymap");
        Name(named, ("H001", "Hpal", "TRIGSTR_003"));
        var strings = Session.Open(MapFolder.Open(named.Folder), game);
        var badText = Refused(() => strings.SetCells(ObjectKind.Unit, [
            new CellEdit("u001", "uhpm", 0, "1235"),
            new CellEdit("H001", "unam", 0, "a\n}\nb"),
        ]));
        Assert.Equal("badText", badText.Code);
        Named(badText, "H001", "unam");
        Unchanged(strings, named);

        // A kind War3Net cannot write back exactly is read only (Lossy stands in such a war3map.w3a).
        using var lossy = TempMap.Of("moonwell-mymap");
        var readOnlyKind = Lossy.Open(lossy.Folder, game);
        var kind = Refused(() => readOnlyKind.SetCells(ObjectKind.Ability, [
            new CellEdit("A001", "acdn", 1, "5"),
            new CellEdit("A003", "acdn", 1, "5"),
        ]));
        Assert.Equal("readOnly", kind.Code);
        Named(kind, "A001", "acdn");
        Unchanged(readOnlyKind, lossy);

        // A read-only object at the second cell: World Editor's h000 with a set count of 2 (version 3).
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "we-3.0", "war3map.w3u"));
        using var twoSets = TempMap.Of(null);
        File.WriteAllBytes(
            Path.Combine(twoSets.Folder, "war3map.w3u"),
            [.. bytes[..20], 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, .. bytes[28..]]);
        var sets = Session.Open(MapFolder.Open(twoSets.Folder), game);
        var readOnly = Refused(() => sets.SetCells(ObjectKind.Unit, [
            new CellEdit("hfoo", "uhpm", 0, "500"),
            new CellEdit("h000", "uhpm", 0, "5"),
        ]));
        Assert.Equal("readOnly", readOnly.Code);
        Named(readOnly, "h000", "uhpm");
        Unchanged(sets, twoSets);
    }

    [Fact]
    public void Set_cells_resets_with_null()
    {
        if (Skipped(out var game))
        {
            return;
        }

        // hpea's udef and uhpm are changed by the map; a null value resets a cell, and sets and resets mix.
        using var map = TempMap.Of("moonwell-mymap");
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        Assert.True(Field(session, ObjectKind.Unit, "hpea", "udef", 0).Changed);
        Assert.True(Field(session, ObjectKind.Unit, "hpea", "uhpm", 0).Changed);

        var changed = session.SetCells(ObjectKind.Unit, [
            new CellEdit("hpea", "udef", 0, null),
            new CellEdit("hpea", "uhpm", 0, null),
            new CellEdit("H001", "uhpm", 0, "1234"),
            new CellEdit("hfoo", "uhpm", 0, null),   // not changed by the map: nothing to reset
        ]);

        Assert.Equal(3, changed);
        Assert.False(Field(session, ObjectKind.Unit, "hpea", "udef", 0).Changed);
        Assert.False(Field(session, ObjectKind.Unit, "hpea", "uhpm", 0).Changed);
        Assert.Equal("1234", Value(session, ObjectKind.Unit, "H001", "uhpm", 0));

        Assert.True(session.Undo());
        Assert.Equal(OnDisk(map), Snapshot(session));
        Assert.True(Field(session, ObjectKind.Unit, "hpea", "udef", 0).Changed);
    }

    [Fact]
    public void Set_cells_keeps_the_shared_string_rule()
    {
        if (Skipped(out var game))
        {
            return;
        }

        // H001's and u001's names refer to one strings file entry (spelled differently); H002's has an entry alone.
        using var map = TempMap.Of("moonwell-mymap");
        Name(map, ("H001", "Hpal", "TRIGSTR_003"), ("u001", "hfoo", "TRIGSTR_3"), ("H002", "Hamg", "TRIGSTR_004"));
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        Assert.Equal("Nondescript", session.Object(ObjectKind.Unit, "H001")!.Name);
        Assert.Equal("Nondescript", session.Object(ObjectKind.Unit, "u001")!.Name);

        var changed = session.SetCells(ObjectKind.Unit, [
            new CellEdit("H001", "unam", 0, "Alpha"),
            new CellEdit("u001", "unam", 0, "Beta"),
            new CellEdit("H002", "unam", 0, "Gamma"),
        ]);

        // Both shared names go inline: the second is still shared, as the map was before this edit.
        Assert.Equal(3, changed);
        Assert.Equal("Alpha", session.Object(ObjectKind.Unit, "H001")!.Name);
        Assert.Equal("Beta", session.Object(ObjectKind.Unit, "u001")!.Name);
        Assert.Equal("Gamma", session.Object(ObjectKind.Unit, "H002")!.Name);
        var skin = session.FileOf(ObjectKind.Unit, true)!;
        FieldValue NameIn(string id) => skin.Custom.Single(entry => entry.Id == id).Find("unam", 0)!.Value;
        Assert.Equal(new FieldValue.Text("Alpha"), NameIn("H001"));
        Assert.Equal(new FieldValue.Text("Beta"), NameIn("u001"));
        Assert.Equal(new FieldValue.Text("TRIGSTR_004"), NameIn("H002"));
        Assert.Equal("Nondescript", session.Strings!.Get(3));
        Assert.Equal("Gamma", session.Strings!.Get(4));

        // The tree and the search index know every touched object's new name.
        var tree = session.Tree(ObjectKind.Unit);
        foreach (var (id, name) in new[] { ("H001", "Alpha"), ("u001", "Beta"), ("H002", "Gamma") })
        {
            Assert.Equal(name, tree.Single(entry => entry.Id == id).Name);
            Assert.Contains(session.Search(name, 50), hit => hit.Id == id && hit.Field == null);
        }

        Assert.True(session.Undo());
        Assert.Equal(OnDisk(map), Snapshot(session));
        Assert.Equal("Nondescript", session.Object(ObjectKind.Unit, "u001")!.Name);
    }

    [Fact]
    public void Set_cells_with_no_edits_changes_nothing()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("moonwell-mymap");
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        Assert.Equal(0, session.SetCells(ObjectKind.Unit, []));

        Assert.False(session.Dirty);
        Assert.False(session.Undo());
        Assert.Equal(OnDisk(map), Snapshot(session));
    }

    [Fact]
    public void Table_matches_object_view()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("moonwell-mymap");
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        // A value past World Editor's limit gives a warning; a name changed through the strings file shows its text.
        session.SetCells(ObjectKind.Unit, [new CellEdit("hfoo", "uhpm", 0, "99999999")]);

        var units = new[] { "H001", "u001", "H002", "hpea", "hfoo" };
        var unitColumns = new[] { "unam", "uhpm", "udef", "ustr", "umvs", "ubdg" }
            .Select(field => new Column(field, 0))
            .ToList();
        var abilities = new[] { "A001", "A003" };
        var abilityColumns = new List<Column>
        {
            new("anam", 0),
            new("acdn", 1),
            new("acdn", 3),
            new("acdn", 4),
        };

        var changed = 0;
        var warned = 0;
        foreach (var (kind, ids, columns) in new[]
        {
            (ObjectKind.Unit, units, unitColumns),
            (ObjectKind.Ability, abilities, abilityColumns),
        })
        {
            var rows = session.Table(kind, ids, columns);
            Assert.Equal(ids, rows.Select(row => row.Id));
            foreach (var row in rows)
            {
                var view = session.Object(kind, row.Id)!;
                Assert.Equal(view.Name, row.Name);
                Assert.Equal(view.BaseId, row.BaseId);
                Assert.Equal(view.ReadOnly, row.ReadOnly);
                Assert.Equal(columns.Count, row.Cells.Count);
                for (var i = 0; i < columns.Count; i++)
                {
                    var cell = row.Cells[i];
                    var field = view.Fields.SingleOrDefault(
                        candidate => candidate.Id == columns[i].Field && candidate.Level == columns[i].Level);
                    if (field == null)
                    {
                        Assert.Equal(new TableCell("", "", false, false, null), cell);
                        continue;
                    }

                    Assert.Equal(new TableCell(field.Value, field.Base, field.Changed, true, field.Warning), cell);
                    changed += cell.Changed ? 1 : 0;
                    warned += cell.Warning != null ? 1 : 0;
                }
            }
        }

        // The comparison above is not empty: the map changes cells, and one has a warning.
        Assert.True(changed > 0);
        Assert.True(warned > 0);
    }

    [Fact]
    public void Table_marks_cells_that_do_not_apply()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("moonwell-mymap");
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        // Strength is a hero's field; hfoo is a footman.
        var unit = Assert.Single(
            session.Table(ObjectKind.Unit, ["hfoo"], [new Column("ustr", 0), new Column("uhpm", 0)]));
        Assert.Equal(new TableCell("", "", false, false, null), unit.Cells[0]);
        Assert.True(unit.Cells[1].Applies);

        // A level above the ability's count (acdn has a value for each level).
        var levels = session.Object(ObjectKind.Ability, "A001")!.Levels;
        Assert.True(levels is > 0 and < 4, $"A001 has {levels} levels; the test needs fewer than 4.");
        var ability = Assert.Single(session.Table(
            ObjectKind.Ability,
            ["A001"],
            [new Column("acdn", levels), new Column("acdn", levels + 1), new Column("acdn", 0)]));
        Assert.True(ability.Cells[0].Applies);
        Assert.Equal(new TableCell("", "", false, false, null), ability.Cells[1]);
        Assert.Equal(new TableCell("", "", false, false, null), ability.Cells[2]);
    }

    [Fact]
    public void Table_keeps_the_order_of_ids_and_skips_unknown()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("moonwell-mymap");
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        var rows = session.Table(ObjectKind.Unit, ["hpea", "zzzz", "H001", "hfoo"], [new Column("uhpm", 0)]);

        Assert.Equal(["hpea", "H001", "hfoo"], rows.Select(row => row.Id));
        Assert.Empty(session.Table(ObjectKind.Unit, [], [new Column("uhpm", 0)]));
        var noField = Assert.Throws<EditException>(
            () => session.Table(ObjectKind.Unit, ["hfoo"], [new Column("uhpm", 0), new Column("nope", 0)]));
        Assert.Equal("noField", noField.Code);
    }

    [Fact]
    public void Table_of_every_unit_is_fast()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("moonwell-mymap");
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        var ids = game.Kind(ObjectKind.Unit).Objects.Select(unit => unit.Id).ToList();
        var columns = new[] { "unam", "uhpm", "umpm", "udef", "ustr", "uagi", "uint", "umvs", "ua1b", "ua1c" }
            .Select(field => new Column(field, 0))
            .ToList();
        Assert.Equal(10, columns.Count);

        session.Table(ObjectKind.Unit, ids, columns);   // warm-up
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var rows = session.Table(ObjectKind.Unit, ids, columns);
        watch.Stop();

        output.WriteLine($"{rows.Count} units x {columns.Count} columns: {watch.ElapsedMilliseconds} ms");
        Assert.Equal(ids.Count, rows.Count);
#if DEBUG
        Assert.True(watch.ElapsedMilliseconds < 1500, $"{watch.ElapsedMilliseconds} ms");
#else
        Assert.True(watch.ElapsedMilliseconds < 300, $"{watch.ElapsedMilliseconds} ms");
#endif
    }

    [Fact]
    public void Set_cells_of_every_unit_is_fast_and_undoes_exactly()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("moonwell-mymap");
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        var ids = game.Kind(ObjectKind.Unit).Objects.Select(unit => unit.Id).ToList();
        var columns = new[] { "ugol", "ulum", "uhpm", "umpm", "udef", "umvs", "ua1b", "ua1c" }
            .Select(field => new Column(field, 0))
            .ToList();
        var fields = columns.Select(column => game.Kind(ObjectKind.Unit).Field(column.Field)!).ToList();
        Assert.All(fields, field => Assert.Contains(field.Type, new[] { "int", "float" }));

        // +10% of every cell that applies, as the bar works it out: whole numbers rounded, decimals to 4 places.
        var edits = new List<CellEdit>();
        foreach (var row in session.Table(ObjectKind.Unit, ids, columns))
        {
            for (var i = 0; i < columns.Count; i++)
            {
                // A cell with no number (no standard value) is one the bar would refuse; it is left out here.
                var cell = row.Cells[i];
                if (!cell.Applies || !double.TryParse(cell.Value, NumberStyles.Float, Invariant, out var number))
                {
                    continue;
                }

                var value = number * 1.1;
                var text = fields[i].Type == "int"
                    ? Math.Round(value, MidpointRounding.AwayFromZero).ToString(Invariant)
                    : Math.Round(value, 4, MidpointRounding.AwayFromZero).ToString(Invariant);
                edits.Add(new CellEdit(row.Id, columns[i].Field, 0, text));
            }
        }

        var before = Snapshot(session);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var changed = session.SetCells(ObjectKind.Unit, edits);
        watch.Stop();

        output.WriteLine($"{edits.Count} cells ({changed} changed) of {ids.Count} units: {watch.ElapsedMilliseconds} ms");
        Assert.True(edits.Count > ids.Count * 6, $"{edits.Count} cells");
        Assert.True(changed > edits.Count / 2, $"{changed} changed");
#if DEBUG
        Assert.True(watch.ElapsedMilliseconds < 3000, $"{watch.ElapsedMilliseconds} ms");
#else
        Assert.True(watch.ElapsedMilliseconds < 1500, $"{watch.ElapsedMilliseconds} ms");
#endif

        Assert.NotEqual(before, Snapshot(session));
        Assert.True(session.Undo());
        Assert.Equal(before, Snapshot(session));
        Assert.Equal(OnDisk(map), Snapshot(session));
        Assert.False(session.Dirty);
    }

    /// <summary>Writes names into the map's war3mapSkin.w3u: each custom unit's (id, base id, name).</summary>
    private static void Name(TempMap map, params (string Id, string BaseId, string Name)[] names)
    {
        var path = Path.Combine(map.Folder, "war3mapSkin.w3u");
        var skin = ObjectFile.Read(ObjectKind.Unit, File.ReadAllBytes(path));
        foreach (var (id, baseId, name) in names)
        {
            var entry = skin.Custom.FirstOrDefault(candidate => candidate.Id == id) ?? skin.AddCustom(baseId, id);
            entry.Set("unam", 0, 0, new FieldValue.Text(name));
        }

        File.WriteAllBytes(path, skin.ToBytes());
    }

    /// <summary>The refused edit's message names the cell first: its object, then its field.</summary>
    private void Named(EditException refused, string id, string field)
    {
        output.WriteLine(refused.Message);
        Assert.StartsWith(id + " ", refused.Message);
        Assert.Contains($"({field}): ", refused.Message);
    }

    /// <summary>Nothing was changed: no edit to undo, and every file as on disk.</summary>
    private static void Unchanged(Session session, TempMap map)
    {
        Assert.False(session.Dirty);
        Assert.False(session.Undo());
        Assert.Equal(OnDisk(map), Snapshot(session));
    }

    /// <summary>What every kind's files and the strings file hold, to compare two states.</summary>
    private static List<string> Snapshot(Session session)
    {
        var lines = new List<string>();
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            foreach (var skin in new[] { false, true })
            {
                var bytes = session.FileOf(kind, skin)?.ToBytes();
                lines.Add($"{kind} {skin}: {(bytes == null ? "none" : Convert.ToHexString(bytes))}");
            }
        }

        lines.Add("wts: " + Convert.ToHexString(session.Strings?.ToBytes() ?? []));
        return lines;
    }

    /// <summary>The same as <see cref="Snapshot"/>, read from the map folder's files.</summary>
    private static List<string> OnDisk(TempMap map)
    {
        var lines = new List<string>();
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            foreach (var skin in new[] { false, true })
            {
                var path = Path.Combine(map.Folder, (skin ? "war3mapSkin." : "war3map.") + ObjectKinds.Extension(kind));
                var bytes = File.Exists(path) ? Convert.ToHexString(File.ReadAllBytes(path)) : "none";
                lines.Add($"{kind} {skin}: {bytes}");
            }
        }

        var wts = Path.Combine(map.Folder, "war3map.wts");
        lines.Add("wts: " + (File.Exists(wts) ? Convert.ToHexString(File.ReadAllBytes(wts)) : ""));
        return lines;
    }

    private static EditException Refused(Func<int> edit) => Assert.Throws<EditException>(() => edit());

    private static FieldView Field(Session session, ObjectKind kind, string id, string field, int level) =>
        Assert.Single(
            session.Object(kind, id)!.Fields, candidate => candidate.Id == field && candidate.Level == level);

    private static string Value(Session session, ObjectKind kind, string id, string field, int level) =>
        Field(session, kind, id, field, level).Value;

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
