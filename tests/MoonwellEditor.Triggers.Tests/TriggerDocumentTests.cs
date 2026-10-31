using War3Net.Build.Extensions;
using War3Net.Build.Script;

namespace MoonwellEditor.Triggers.Tests;

public sealed class TriggerDocumentTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_map_of_the_install_comes_back_the_same_bytes()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var wrong = new List<string>();
        var maps = 0;
        foreach (var path in InstallMaps.All())
        {
            var store = InstallMaps.Open(path)!;
            var triggers = store.Read(TriggerDocument.TriggersFile);
            var custom = store.Read(TriggerDocument.CustomTextFile);
            var document = TriggerDocument.Read(triggers, custom, catalog);
            maps++;
            if (document.Problem != null)
            {
                wrong.Add($"{path}: {document.Problem}");
            }
            else if (!document.TriggerBytes()!.AsSpan().SequenceEqual(triggers)
                || !document.CustomTextBytes()!.AsSpan().SequenceEqual(custom))
            {
                wrong.Add($"{path}: not the same bytes");
            }
        }

        output.WriteLine($"{maps} maps");
        Assert.True(maps > 200);
        Assert.Empty(wrong);
    }

    [Fact]
    public void A_map_with_no_trigger_files_starts_empty_and_writes_nothing()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var document = TriggerDocument.Read(null, null, catalog);

        Assert.Null(document.Problem);
        Assert.False(document.ReadOnly);
        Assert.Null(document.TriggerBytes());
        Assert.Null(document.CustomTextBytes());
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_a_problem_and_read_only()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var document = TriggerDocument.Read(new byte[12], null, catalog);

        Assert.NotNull(document.Problem);
        Assert.True(document.ReadOnly);
        Assert.Null(document.TriggerBytes());
    }

    [Fact]
    public void Lost_Temple_shows_one_category_and_one_trigger()
    {
        var catalog = InstallMaps.Catalog;
        var store = InstallMaps.Open(InstallMaps.LostTemple);
        if (catalog == null || store == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var document = TriggerDocument.Read(
            store.Read(TriggerDocument.TriggersFile), store.Read(TriggerDocument.CustomTextFile), catalog);
        var tree = document.Tree();

        Assert.Equal("root", tree[0].Kind);
        Assert.Equal("category", tree[1].Kind);
        Assert.Equal("Initialization", tree[1].Name);
        var trigger = Assert.Single(tree, item => item.Kind == "trigger");
        Assert.Equal("Melee Initialization", trigger.Name);
        Assert.Equal(tree[1].Id, trigger.Parent);
    }

    [Fact]
    public void An_older_format_shows_under_a_root_and_is_read_only()
    {
        var catalog = InstallMaps.Catalog;
        if (catalog == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var old = new MapTriggers(MapTriggersFormatVersion.v7, null) { GameVersion = 2 };
        old.TriggerItems.Add(new TriggerCategoryDefinition { Id = 5, Name = "Setup" });
        foreach (var name in new[] { "One", "Two" })
        {
            old.TriggerItems.Add(new TriggerDefinition { Name = name, Description = "", ParentId = 5, IsEnabled = true });
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(old);
        writer.Flush();

        var document = TriggerDocument.Read(stream.ToArray(), null, catalog);

        Assert.True(document.ReadOnly);
        Assert.NotNull(document.Problem);
        var tree = document.Tree();
        Assert.Equal(["root", "category", "trigger", "trigger"], tree.Select(item => item.Kind));
        Assert.Equal(tree[0].Id, tree[1].Parent);
        Assert.Equal(4, tree.Select(item => item.Id).Distinct().Count());
        Assert.Null(document.TriggerBytes());
    }

    [Fact]
    public void Custom_text_pairs_with_its_trigger()
    {
        var catalog = InstallMaps.Catalog;
        var store = InstallMaps.Open(InstallMaps.ForgottenHollow);
        if (catalog == null || store == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var document = TriggerDocument.Read(
            store.Read(TriggerDocument.TriggersFile), store.Read(TriggerDocument.CustomTextFile), catalog);
        var script = document.Tree().First(item => item.Kind == "script");

        Assert.False(string.IsNullOrEmpty(document.CodeOf(script.Id)));
    }

    // World Editor's flag for a file at its own path in the archive.
    private const byte OwnPath = 13;

    private static byte[] Imports(params (byte Flags, string Path)[] files)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(1);
            writer.Write(files.Length);
            foreach (var (flags, path) in files)
            {
                writer.Write(flags);
                writer.Write(System.Text.Encoding.UTF8.GetBytes(path));
                writer.Write((byte)0);
            }
        }

        return stream.ToArray();
    }

    [Fact]
    public void A_map_with_no_imports_gets_them_with_the_layout_file()
    {
        var imports = TriggerDocument.ImportsWithLayout(null);

        Assert.Equal(Imports((OwnPath, TriggerDocument.LayoutFile)), imports);
    }

    [Fact]
    public void The_layout_file_goes_after_the_imports_a_map_has()
    {
        var imports = TriggerDocument.ImportsWithLayout(Imports((24, "Fire.mdx"), (29, @"UI\Hud.fdf")));

        Assert.Equal(
            Imports((24, "Fire.mdx"), (29, @"UI\Hud.fdf"), (OwnPath, TriggerDocument.LayoutFile)),
            imports);
    }

    [Theory]
    [InlineData(TriggerDocument.LayoutFile)]
    [InlineData("War3Map.TriggerLayout.JSON")]
    public void Imports_that_name_the_layout_file_stay_as_they_are(string path)
    {
        Assert.Null(TriggerDocument.ImportsWithLayout(Imports((24, "Fire.mdx"), (OwnPath, path))));
    }

    [Fact]
    public void Imports_that_cannot_be_read_stay_as_they_are()
    {
        Assert.Null(TriggerDocument.ImportsWithLayout([9, 0, 0, 0, 1, 0, 0, 0]));
        Assert.Null(TriggerDocument.ImportsWithLayout([1, 0, 0, 0, 5, 0, 0, 0]));
    }

    [Fact]
    public void Imports_that_would_not_come_back_the_same_bytes_stay_as_they_are()
    {
        byte[] trailing = [.. Imports((24, "Fire.mdx")), 7, 7, 7];

        Assert.Null(TriggerDocument.ImportsWithLayout(trailing));
    }
}
