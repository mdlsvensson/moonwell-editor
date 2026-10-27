namespace MoonwellEditor.Assets.Tests;

/// <summary>
/// Reads the Warcraft III install on this computer (MOONWELL_EDITOR_GAME, else the usual folder); without one these
/// tests pass without checking anything, and say so.
/// </summary>
public sealed class GameFilesTests : IClassFixture<GameFilesTests.Install>
{
    private readonly Install _install;

    public GameFilesTests(Install install, ITestOutputHelper output)
    {
        _install = install;
        if (install.Files == null)
        {
            output.WriteLine(install.Reason);
        }
    }

    [Fact]
    public void Paths_are_written_the_ways_object_data_writes_them()
    {
        Assert.Equal("units/human/Peasant/Peasant", GameFiles.Normalized(@" \units\human\Peasant\Peasant "));
        Assert.Equal("Textures/x.dds", GameFiles.Normalized("War3.w3mod:Textures/x.dds"));
    }

    [Fact]
    public void A_unit_icon_and_model_are_found_as_the_game_stores_them()
    {
        if (_install.Files is not { } files)
        {
            return;
        }

        Assert.EndsWith(".dds", files.Find(@"ReplaceableTextures\CommandButtons\BTNPeasant.blp"), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".mdx", files.Find(@"units\human\Peasant\Peasant"), StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".mdx", files.Find(@"units\human\Peasant\Peasant.mdl"), StringComparison.OrdinalIgnoreCase);
        Assert.Null(files.Find(@"units\human\NoSuchUnit\NoSuchUnit"));

        // Each graphics mode reads its own tier first: the Definitive Edition's Peasant icon is its own.
        Assert.StartsWith(
            "War3.w3mod:_DE.w3mod:",
            files.Find(@"ReplaceableTextures\CommandButtons\BTNPeasant.blp", "de"),
            StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(
            "War3.w3mod:ReplaceableTextures",
            files.Find(@"ReplaceableTextures\CommandButtons\BTNPeasant.blp", "sd"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            files.List("ReplaceableTextures/CommandButtons/", "de", ".dds", ".blp"),
            icon => icon.Contains("BTNPriestV1", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            files.List("ReplaceableTextures/CommandButtons/", "sd", ".dds", ".blp"),
            icon => icon.Contains("BTNPriestV1", StringComparison.OrdinalIgnoreCase));

        // Only the Reforged tiers hold the icon the data gives the Priest.
        Assert.Contains(
            "_HD.w3mod:",
            files.Find(@"ReplaceableTextures\CommandButtons\BTNPriestV1.blp"),
            StringComparison.OrdinalIgnoreCase);

        var model = files.Read(@"units\human\Peasant\Peasant")!;
        Assert.Equal("MDLX"u8.ToArray(), model[..4]);
    }

    [Fact]
    public void Icons_decode_to_images()
    {
        if (_install.Files is not { } files)
        {
            return;
        }

        var dds = Textures.Decode(files.Read(@"ReplaceableTextures\CommandButtons\BTNPeasant.blp")!)!;
        Assert.Equal((64, 64), (dds.Width, dds.Height));
        Assert.Contains(dds.Pixels.Chunk(4), pixel => pixel[0] + pixel[1] + pixel[2] > 100);

        var blp = files.List("ReplaceableTextures/CommandButtons/", "sd", ".blp");
        Assert.NotEmpty(blp);
        var image = Textures.Decode(files.Read(blp[0])!)!;
        Assert.True(image.Width > 0 && image.Pixels.Length == image.Width * image.Height * 4);

        var png = Textures.Png(dds);
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, png[..4]);
    }

    [Fact]
    public void The_icons_and_models_can_be_listed()
    {
        if (_install.Files is not { } files)
        {
            return;
        }

        var icons = files.List("ReplaceableTextures/CommandButtons/", "sd", ".dds", ".blp");
        Assert.True(icons.Count > 1000, $"{icons.Count} icons");
        Assert.Contains(icons, icon => icon.EndsWith("BTNPeasant.dds", StringComparison.OrdinalIgnoreCase));
        var models = files.List("", "sd", ".mdx");
        Assert.True(models.Count > 1000);
        Assert.DoesNotContain(models, model => model.Contains(".w3mod:", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The install, opened once for the class.</summary>
    public sealed class Install : IDisposable
    {
        public Install()
        {
            var folder = Environment.GetEnvironmentVariable("MOONWELL_EDITOR_GAME") ?? GameFiles.DefaultFolder;
            if (!File.Exists(Path.Combine(folder, ".build.info")))
            {
                Reason = $"No Warcraft III install in {folder}; set MOONWELL_EDITOR_GAME to run these tests";
                return;
            }

            Files = GameFiles.Open(folder);
        }

        public GameFiles? Files { get; }

        public string? Reason { get; }

        public void Dispose() => Files?.Dispose();
    }
}
