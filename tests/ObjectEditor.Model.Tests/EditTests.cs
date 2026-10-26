using ObjectEditor.Files;
using Xunit.Abstractions;

namespace ObjectEditor.Model.Tests;

[Collection(GameDataCollection.Name)]
public sealed class EditTests(GameDataFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public void Set_and_read_back()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        session.Set(ObjectKind.Unit, "h000", "uhpm", 0, "275");
        session.Set(ObjectKind.Unit, "hfoo", "ua1c", 0, "1.5");
        session.Set(ObjectKind.Ability, "AHhb", "acdn", 2, "4.5");
        session.Set(ObjectKind.Doodad, "D000", "dvr1", 1, "100");

        var footman = session.Object(ObjectKind.Unit, "hfoo")!;
        Assert.Equal("500", Single(footman, "uhpm", 0).Value);
        Assert.Equal("420", Single(footman, "uhpm", 0).Base);
        Assert.True(Single(footman, "uhpm", 0).Changed);
        Assert.Equal("1.5", Single(footman, "ua1c", 0).Value);
        Assert.Equal("275", Single(session.Object(ObjectKind.Unit, "h000")!, "uhpm", 0).Value);
        var holyLight = session.Object(ObjectKind.Ability, "AHhb")!;
        Assert.Equal("4.5", Single(holyLight, "acdn", 2).Value);
        Assert.False(Single(holyLight, "acdn", 1).Changed);
        Assert.Equal("100", Single(session.Object(ObjectKind.Doodad, "D000")!, "dvr1", 1).Value);
        Assert.True(session.Tree(ObjectKind.Unit).Single(entry => entry.Id == "hfoo").Changed);
        Assert.True(session.Dirty);

        var main = session.FileOf(ObjectKind.Unit, false)!.Original.Single(entry => entry.BaseId == "hfoo");
        Assert.Equal(new FieldValue.Int(500), main.Find("uhpm", 0)!.Value);
        Assert.Equal(new FieldValue.Unreal(1.5f), main.Find("ua1c", 0)!.Value);

        // A yes/no field takes 1 and 0; a choice stored as text takes its value.
        var units = game.Kind(ObjectKind.Unit);
        var flag = units.Fields.First(field => field.Type == "bool" && units.Applies(field, units.Object("hfoo")!));
        var other = Single(footman, flag.Id, 0).Value == "1" ? "0" : "1";
        session.Set(ObjectKind.Unit, "hfoo", flag.Id, 0, other);
        Assert.Equal(other, Single(session.Object(ObjectKind.Unit, "hfoo")!, flag.Id, 0).Value);
        session.Set(ObjectKind.Unit, "hfoo", "urac", 0, "undead");
        Assert.Equal("undead", Single(session.Object(ObjectKind.Unit, "hfoo")!, "urac", 0).Value);

        Assert.Equal("noObject", Refused(() => session.Set(ObjectKind.Unit, "zzzz", "uhpm", 0, "1")));
        Assert.Equal("noField", Refused(() => session.Set(ObjectKind.Unit, "hfoo", "zzzz", 0, "1")));
        Assert.Equal("noField", Refused(() => session.Set(ObjectKind.Unit, "hfoo", "ustr", 0, "1")));   // a hero's
        Assert.Equal("noField", Refused(() => session.Set(ObjectKind.Ability, "AHhb", "acdn", 4, "1")));
        Assert.Equal("noField", Refused(() => session.Set(ObjectKind.Ability, "AHhb", "acdn", 0, "1")));
        Assert.Equal("noField", Refused(() => session.Set(ObjectKind.Unit, "hfoo", "uhpm", 1, "1")));
        Assert.Equal("noObject", Refused(() => session.Reset(ObjectKind.Unit, "zzzz", "uhpm", 0)));
    }

    [Fact]
    public void Set_goes_to_skin_file()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        session.Set(ObjectKind.Unit, "hfoo", "unam", 0, "Captain");
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");

        var main = session.FileOf(ObjectKind.Unit, false)!.Original.Single(entry => entry.BaseId == "hfoo");
        var skin = session.FileOf(ObjectKind.Unit, true)!.Original.Single(entry => entry.BaseId == "hfoo");
        Assert.Equal(new FieldValue.Text("Captain"), skin.Find("unam", 0)!.Value);
        Assert.Null(skin.Find("uhpm", 0));
        Assert.Equal(new FieldValue.Int(500), main.Find("uhpm", 0)!.Value);
        Assert.Null(main.Find("unam", 0));
        Assert.Equal("Captain", session.Object(ObjectKind.Unit, "hfoo")!.Name);

        // A map without object files gets them.
        using var empty = TempMap.Of(null);
        var fresh = Session.Open(MapFolder.Open(empty.Folder), game);
        fresh.Set(ObjectKind.Unit, "hfoo", "unam", 0, "Captain");
        Assert.Null(fresh.FileOf(ObjectKind.Unit, false));
        Assert.Equal(
            new FieldValue.Text("Captain"),
            fresh.FileOf(ObjectKind.Unit, true)!.Original.Single().Find("unam", 0)!.Value);
        Assert.True(fresh.Undo());
        Assert.Null(fresh.FileOf(ObjectKind.Unit, true));
    }

    [Fact]
    public void Float_equal_to_base_removes_record()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        session.Set(ObjectKind.Unit, "hfoo", "ua1c", 0, "2");
        var footman = session.FileOf(ObjectKind.Unit, false)!.Original.Single(entry => entry.BaseId == "hfoo");
        Assert.Equal(new FieldValue.Unreal(2), footman.Find("ua1c", 0)!.Value);
        session.Set(ObjectKind.Unit, "hfoo", "ua1c", 0, "1.35");

        var cooldown = Single(session.Object(ObjectKind.Unit, "hfoo")!, "ua1c", 0);
        Assert.False(cooldown.Changed);
        Assert.Equal("1.35", cooldown.Value);
        Assert.DoesNotContain(session.FileOf(ObjectKind.Unit, false)!.Original, entry => entry.BaseId == "hfoo");
        Assert.False(session.Tree(ObjectKind.Unit).Single(entry => entry.Id == "hfoo").Changed);

        // Numbers are compared as numbers; a value equal to the base with no mod is no edit.
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "0420");
        session.Set(ObjectKind.Unit, "hfoo", "ua1c", 0, "1.350");
        Assert.False(Single(session.Object(ObjectKind.Unit, "hfoo")!, "ua1c", 0).Changed);
        Assert.True(session.Undo());   // the 1.35 that removed the record
        Assert.Equal("2", Single(session.Object(ObjectKind.Unit, "hfoo")!, "ua1c", 0).Value);
        Assert.True(session.Undo());
        Assert.False(session.Undo());
        Assert.False(session.Dirty);
    }

    [Fact]
    public void Mode_values_keep_record()
    {
        if (Skipped(out var game))
        {
            return;
        }

        var units = game.Kind(ObjectKind.Unit);
        var (standard, field) = units.Objects
            .SelectMany(obj => obj.Modes.Keys.Select(key => (obj, field: units.Field(key))))
            .First(pair => pair.field is { PerLevel: false } && units.Applies(pair.field, pair.obj));
        output.WriteLine($"{standard.Id} {field!.Id}: {string.Join(", ", standard.Modes[field.Id])}");

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        var baseValue = Single(session.Object(ObjectKind.Unit, standard.Id)!, field.Id, 0).Base;

        session.Set(ObjectKind.Unit, standard.Id, field.Id, 0, baseValue);

        var view = Single(session.Object(ObjectKind.Unit, standard.Id)!, field.Id, 0);
        Assert.True(view.Changed);
        Assert.Equal(baseValue, view.Value);
        Assert.NotNull(view.Modes);
    }

    [Fact]
    public void Trigstr_edit_updates_wts()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        var before = session.Strings!.Get(12);

        session.Set(ObjectKind.Unit, "h000", "unam", 0, "Knight");

        Assert.Equal("Knight", session.Object(ObjectKind.Unit, "h000")!.Name);
        Assert.Equal("Knight", session.Strings!.Get(12));
        var skin = session.FileOf(ObjectKind.Unit, true)!.Custom.Single(entry => entry.Id == "h000");
        Assert.Equal(new FieldValue.Text("TRIGSTR_012"), skin.Find("unam", 0)!.Value);

        // A string that was inline stays inline.
        session.Set(ObjectKind.Unit, "hfoo", "unam", 0, "Captain");
        var footman = session.FileOf(ObjectKind.Unit, true)!.Original.Single(entry => entry.BaseId == "hfoo");
        Assert.Equal(new FieldValue.Text("Captain"), footman.Find("unam", 0)!.Value);

        // The format cannot hold a line that is a lone }.
        Assert.Equal("badText", Refused(() => session.Set(ObjectKind.Unit, "h000", "unam", 0, "a\n}\nb")));
        Assert.Equal("Knight", session.Strings!.Get(12));

        Assert.True(session.Undo());
        Assert.True(session.Undo());
        Assert.Equal(before, session.Strings!.Get(12));
        Assert.Equal(File.ReadAllBytes(Path.Combine(map.Folder, "war3map.wts")), session.Strings!.ToBytes());
    }

    [Fact]
    public void Shared_trigstr_is_written_inline()
    {
        if (Skipped(out var game))
        {
            return;
        }

        // Another kind's modification refers to the same entry as h000's name (TRIGSTR_012), spelled differently.
        using var map = TempMap.Of("we-3.0");
        var abilityPath = Path.Combine(map.Folder, "war3map.w3a");
        var abilities = ObjectFile.Read(ObjectKind.Ability, File.ReadAllBytes(abilityPath));
        abilities.AddOriginal("AHhb").Set("atp1", 1, 0, new FieldValue.Text("TRIGSTR_12"));
        File.WriteAllBytes(abilityPath, abilities.ToBytes());
        var wts = File.ReadAllBytes(Path.Combine(map.Folder, "war3map.wts"));
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        var before = session.Strings!.Get(12);

        session.Set(ObjectKind.Unit, "h000", "unam", 0, "Knight");

        Assert.Equal("Knight", session.Object(ObjectKind.Unit, "h000")!.Name);
        var skin = session.FileOf(ObjectKind.Unit, true)!.Custom.Single(entry => entry.Id == "h000");
        Assert.Equal(new FieldValue.Text("Knight"), skin.Find("unam", 0)!.Value);
        Assert.Equal(before, session.Strings!.Get(12));
        Assert.Equal(wts, session.Strings!.ToBytes());
        var shared = session.FileOf(ObjectKind.Ability, false)!.Original.Single(entry => entry.BaseId == "AHhb");
        Assert.Equal(new FieldValue.Text("TRIGSTR_12"), shared.Find("atp1", 1)!.Value);

        Assert.True(session.Undo());
        Assert.Equal(new FieldValue.Text("TRIGSTR_012"), skin.Find("unam", 0)!.Value);
        Assert.False(session.Dirty);
    }

    [Fact]
    public void New_ids_follow_world_editor()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        Assert.Equal("h001", session.Create(ObjectKind.Unit, "hfoo", null));
        Assert.Equal("h002", session.Create(ObjectKind.Unit, "hpea", null));
        Assert.Equal("H000", session.Create(ObjectKind.Unit, "Hpal", null));
        Assert.Equal("I001", session.Create(ObjectKind.Item, "ratf", null));
        Assert.Equal("B001", session.Create(ObjectKind.Destructable, "DTrf", null));
        Assert.Equal("D001", session.Create(ObjectKind.Doodad, "UObb", null));
        Assert.Equal("A001", session.Create(ObjectKind.Ability, "AHhb", null));
        Assert.Equal("B001", session.Create(ObjectKind.Buff, "BNab", null));
        Assert.Equal("R001", session.Create(ObjectKind.Upgrade, "Rhme", null));
        Assert.Equal("h00Z", session.Create(ObjectKind.Unit, "hfoo", "h00Z"));
        Assert.Equal("h003", session.Create(ObjectKind.Unit, "hfoo", null));

        Assert.Equal("idTaken", Refused(() => session.Create(ObjectKind.Ability, "AHhb", "A000")));
        Assert.Equal("idTaken", Refused(() => session.Create(ObjectKind.Unit, "hfoo", "hpea")));
        Assert.Equal("idTaken", Refused(() => session.Create(ObjectKind.Unit, "hfoo", "h001")));
        Assert.Equal("idInvalid", Refused(() => session.Create(ObjectKind.Unit, "hfoo", "h0")));
        Assert.Equal("idInvalid", Refused(() => session.Create(ObjectKind.Unit, "hfoo", "h0 1")));
        Assert.Equal("noObject", Refused(() => session.Create(ObjectKind.Unit, "zzzz", null)));

        // The new object is in both files, with no modifications, after the map's own objects.
        var view = session.Object(ObjectKind.Unit, "h001")!;
        Assert.Equal("hfoo", view.BaseId);
        Assert.Equal("Footman", view.Name);
        Assert.All(view.Fields, field => Assert.False(field.Changed, field.Id));
        foreach (var skin in new[] { false, true })
        {
            var file = session.FileOf(ObjectKind.Unit, skin)!;
            Assert.Equal(
                new[] { "h000", "h001", "h002", "H000", "h00Z", "h003" },
                file.Custom.Select(entry => entry.Id));
            Assert.Empty(file.Custom[1].Mods);
            Assert.Equal(1, file.Custom[1].SetCount);
        }

        Assert.Equal(
            new[] { "h000", "h001", "h002", "H000", "h00Z", "h003" },
            session.Tree(ObjectKind.Unit).Where(entry => entry.Path[0] == "Custom").Select(entry => entry.Id));

        // Copies: a custom object's records (a TRIGSTR name written inline), a standard object's changes.
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        Assert.Equal("h004", session.Copy(ObjectKind.Unit, "h000", null));
        Assert.Equal("h005", session.Copy(ObjectKind.Unit, "hfoo", null));
        Assert.Equal("H001", session.Copy(ObjectKind.Unit, "H000", null));
        Assert.Equal("A00A", session.Copy(ObjectKind.Ability, "A000", "A00A"));
        var copy = session.Object(ObjectKind.Unit, "h004")!;
        Assert.Equal("hpea", copy.BaseId);
        Assert.Equal(session.Object(ObjectKind.Unit, "h000")!.Name, copy.Name);
        var copied = session.FileOf(ObjectKind.Unit, true)!.Custom.Single(entry => entry.Id == "h004");
        Assert.Equal(new FieldValue.Text(session.Strings!.Get(12)!), copied.Find("unam", 0)!.Value);
        var footman = session.Object(ObjectKind.Unit, "h005")!;
        Assert.Equal("hfoo", footman.BaseId);
        Assert.Equal("500", Single(footman, "uhpm", 0).Value);
        Assert.Equal("idTaken", Refused(() => session.Copy(ObjectKind.Unit, "hfoo", "h000")));
        Assert.Equal("noObject", Refused(() => session.Copy(ObjectKind.Unit, "zzzz", null)));

        session.Delete(ObjectKind.Unit, "h001");
        Assert.Null(session.Object(ObjectKind.Unit, "h001"));
        Assert.DoesNotContain(session.FileOf(ObjectKind.Unit, false)!.Custom, entry => entry.Id == "h001");
        Assert.DoesNotContain(session.FileOf(ObjectKind.Unit, true)!.Custom, entry => entry.Id == "h001");
        Assert.Equal("h001", session.Create(ObjectKind.Unit, "hfoo", null));
        Assert.Equal("noObject", Refused(() => session.Delete(ObjectKind.Unit, "hfoo")));
        Assert.Equal("noObject", Refused(() => session.Delete(ObjectKind.Unit, "zzzz")));
    }

    [Fact]
    public void Pointer_comes_from_metadata_unless_the_record_has_one()
    {
        if (Skipped(out var game))
        {
            return;
        }

        var heal = game.Kind(ObjectKind.Ability).Field("Hhb1")!;
        Assert.True(heal.Pointer > 0);
        using var map = TempMap.Of(null);
        var abilities = ObjectFile.Empty(ObjectKind.Ability);
        abilities.AddOriginal("AHhb").Set("Hhb1", 1, 0, new FieldValue.Unreal(300));
        File.WriteAllBytes(Path.Combine(map.Folder, "war3map.w3a"), abilities.ToBytes());
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        session.Set(ObjectKind.Ability, "AHhb", "Hhb1", 1, "400");
        session.Set(ObjectKind.Ability, "AHhb", "Hhb1", 2, "500");

        var entry = session.FileOf(ObjectKind.Ability, false)!.Original.Single();
        Assert.Equal(0, entry.Find("Hhb1", 1)!.Pointer);
        Assert.Equal(new FieldValue.Unreal(400), entry.Find("Hhb1", 1)!.Value);
        Assert.Equal(heal.Pointer, entry.Find("Hhb1", 2)!.Pointer);
    }

    [Fact]
    public void Undo_redo_every_edit()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        var name = session.Object(ObjectKind.Unit, "h000")!.Name;

        Assert.False(session.Dirty);
        Assert.False(session.Undo());
        Assert.False(session.Redo());

        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");                   // 1
        session.Set(ObjectKind.Unit, "h000", "unam", 0, "Knight");                // 2: the .wts entry
        session.Reset(ObjectKind.Unit, "h000", "unam", 0);                        // 3
        var created = session.Create(ObjectKind.Unit, "hfoo", null);              // 4
        session.Set(ObjectKind.Unit, created, "uhpm", 0, "700");                  // 5
        var copied = session.Copy(ObjectKind.Unit, "hfoo", null);                 // 6
        session.Delete(ObjectKind.Unit, "h000");                                  // 7
        session.Set(ObjectKind.Ability, "A000", "acdn", 1, "5");                  // 8
        session.Set(ObjectKind.Ability, "AHhb", "Hhb1", 1, "400");                // 9
        session.Reset(ObjectKind.Unit, "hfoo", "uhpm", 0);                        // 10
        Assert.True(session.Dirty);

        var after = Snapshot(session);
        Assert.Equal("h001", created);
        Assert.Equal("h002", copied);
        Assert.Null(session.Object(ObjectKind.Unit, "h000"));
        Assert.Equal("500", Single(session.Object(ObjectKind.Unit, copied)!, "uhpm", 0).Value);
        Assert.False(Single(session.Object(ObjectKind.Unit, "hfoo")!, "uhpm", 0).Changed);

        Assert.True(session.Undo());   // 10
        Assert.Equal("500", Single(session.Object(ObjectKind.Unit, "hfoo")!, "uhpm", 0).Value);
        Assert.True(session.Undo());   // 9
        Assert.False(Single(session.Object(ObjectKind.Ability, "AHhb")!, "Hhb1", 1).Changed);
        Assert.True(session.Undo());   // 8
        Assert.True(session.Undo());   // 7
        Assert.Equal("Peasant", session.Object(ObjectKind.Unit, "h000")!.Name);
        Assert.Equal(
            new[] { "h000", created, copied },
            session.Tree(ObjectKind.Unit).Where(entry => entry.Path[0] == "Custom").Select(entry => entry.Id));
        Assert.True(session.Undo());   // 6
        Assert.Null(session.Object(ObjectKind.Unit, copied));
        Assert.True(session.Undo());   // 5
        Assert.False(Single(session.Object(ObjectKind.Unit, created)!, "uhpm", 0).Changed);
        Assert.True(session.Undo());   // 4
        Assert.Null(session.Object(ObjectKind.Unit, created));
        Assert.True(session.Undo());   // 3
        Assert.Equal("Knight", session.Object(ObjectKind.Unit, "h000")!.Name);
        Assert.True(session.Undo());   // 2
        Assert.Equal(name, session.Object(ObjectKind.Unit, "h000")!.Name);
        Assert.True(session.Undo());   // 1
        Assert.False(session.Undo());
        Assert.False(session.Dirty);

        // Every file is back to its bytes as read.
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            foreach (var skin in new[] { false, true })
            {
                var path = Path.Combine(map.Folder, (skin ? "war3mapSkin." : "war3map.") + ObjectKinds.Extension(kind));
                Assert.Equal(File.ReadAllBytes(path), session.FileOf(kind, skin)!.ToBytes());
            }
        }

        Assert.Equal(File.ReadAllBytes(Path.Combine(map.Folder, "war3map.wts")), session.Strings!.ToBytes());

        for (var i = 0; i < 10; i++)
        {
            Assert.True(session.Redo());
        }

        Assert.False(session.Redo());
        Assert.True(session.Dirty);
        Assert.Equal(after, Snapshot(session));

        // A new edit after an undo drops what could be redone.
        Assert.True(session.Undo());
        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "600");
        Assert.False(session.Redo());
        Assert.Equal("600", Single(session.Object(ObjectKind.Unit, "hfoo")!, "uhpm", 0).Value);
    }

    [Fact]
    public void Past_limit_warns_not_fails()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);
        Assert.Null(Single(session.Object(ObjectKind.Unit, "hfoo")!, "uhpm", 0).Warning);

        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "600000");
        session.Set(ObjectKind.Unit, "h000", "uhpm", 0, "0");
        session.Set(ObjectKind.Unit, "hfoo", "unam", 0, "");

        var hitPoints = Single(session.Object(ObjectKind.Unit, "hfoo")!, "uhpm", 0);
        Assert.Equal("600000", hitPoints.Value);
        Assert.Equal("Above World Editor's limit of 500000.", hitPoints.Warning);
        Assert.Equal(
            "Below World Editor's limit of 1.",
            Single(session.Object(ObjectKind.Unit, "h000")!, "uhpm", 0).Warning);
        Assert.Equal("May not be empty.", Single(session.Object(ObjectKind.Unit, "hfoo")!, "unam", 0).Warning);

        var units = game.Kind(ObjectKind.Unit);
        var footman = units.Object("hfoo")!;
        var text = units.Fields.First(field =>
            field is { MaxLength: not null, Store: "string", Required: false, MaxEntries: null }
            && units.Applies(field, footman));
        var list = units.Fields.First(field =>
            field is { MaxEntries: not null, Store: "string", MaxLength: null } && units.Applies(field, footman));
        output.WriteLine($"{text.Id} {list.Id}");
        session.Set(ObjectKind.Unit, "hfoo", text.Id, 0, new string('a', text.MaxLength!.Value + 1));
        var entries = string.Join(",", Enumerable.Repeat("hfoo", list.MaxEntries!.Value + 1));
        session.Set(ObjectKind.Unit, "hfoo", list.Id, 0, entries);
        var view = session.Object(ObjectKind.Unit, "hfoo")!;
        Assert.Equal(
            $"Longer than World Editor's limit of {text.MaxLength} characters.",
            Single(view, text.Id, 0).Warning);
        Assert.Equal(
            $"More than World Editor's limit of {list.MaxEntries} entries.",
            Single(view, list.Id, 0).Warning);
    }

    [Fact]
    public void Text_in_number_field_refused()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        Assert.Equal("notNumber", Refused(() => session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "lots")));
        Assert.Equal("notNumber", Refused(() => session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "1.5")));
        Assert.Equal("notNumber", Refused(() => session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "")));
        Assert.Equal("notNumber", Refused(() => session.Set(ObjectKind.Unit, "hfoo", "ua1c", 0, "fast")));
        Assert.Equal("notNumber", Refused(() => session.Set(ObjectKind.Unit, "hfoo", "ua1c", 0, "NaN")));
        Assert.Equal("notNumber", Refused(() => session.Set(ObjectKind.Unit, "hfoo", "ua1c", 0, "1,5")));

        Assert.False(session.Dirty);
        Assert.False(Single(session.Object(ObjectKind.Unit, "hfoo")!, "uhpm", 0).Changed);
        Assert.DoesNotContain(session.FileOf(ObjectKind.Unit, false)!.Original, entry => entry.BaseId == "hfoo");
    }

    [Fact]
    public void Read_only_kind_refuses()
    {
        if (Skipped(out var game))
        {
            return;
        }

        using var map = TempMap.Of("we-3.0");
        File.WriteAllBytes(Path.Combine(map.Folder, "war3map.w3a"), [3, 0, 0, 0, 0]);
        var session = Session.Open(MapFolder.Open(map.Folder), game);

        Assert.Equal("readOnly", Refused(() => session.Set(ObjectKind.Ability, "AHhb", "acdn", 1, "5")));
        Assert.Equal("readOnly", Refused(() => session.Reset(ObjectKind.Ability, "A000", "anam", 0)));
        Assert.Equal("readOnly", Refused(() => session.Create(ObjectKind.Ability, "AHhb", null)));
        Assert.Equal("readOnly", Refused(() => session.Copy(ObjectKind.Ability, "A000", null)));
        Assert.Equal("readOnly", Refused(() => session.Delete(ObjectKind.Ability, "A000")));
        Assert.False(session.Dirty);
        Assert.NotNull(session.FileOf(ObjectKind.Ability, true)!.Custom.Single().Find("anam", 0));

        session.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");   // other kinds can still be edited
        Assert.True(session.Dirty);

        // A version 3 record with two set flags is read only; the kind's other objects are not.
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "we-3.0", "war3map.w3u"));
        using var twoSets = TempMap.Of(null);
        File.WriteAllBytes(
            Path.Combine(twoSets.Folder, "war3map.w3u"),
            [.. bytes[..20], 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, .. bytes[28..]]);
        var sets = Session.Open(MapFolder.Open(twoSets.Folder), game);
        Assert.Equal("readOnly", Refused(() => sets.Set(ObjectKind.Unit, "h000", "uhpm", 0, "5")));
        Assert.Equal("readOnly", Refused(() => sets.Delete(ObjectKind.Unit, "h000")));
        Assert.Equal("readOnly", Refused(() => sets.Copy(ObjectKind.Unit, "h000", null)));
        sets.Set(ObjectKind.Unit, "hfoo", "uhpm", 0, "500");
        Assert.Equal("h001", sets.Create(ObjectKind.Unit, "hfoo", null));
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

    private static string Refused(Action edit) => Assert.Throws<EditException>(edit).Code;

    private static FieldView Single(ObjectView view, string field, int level) =>
        Assert.Single(view.Fields, candidate => candidate.Id == field && candidate.Level == level);

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
