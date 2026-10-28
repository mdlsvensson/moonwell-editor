using Moonwell.GameData;

namespace MoonwellEditor.Assets.Tests;

/// <summary>
/// Decodes icons of the Warcraft III install on this computer (MOONWELL_WAR3, else the usual folder), read with
/// Moonwell's reader; without one these tests pass without checking anything, and say so. Moonwell's own tests check
/// the reader.
/// </summary>
public sealed class TexturesTests : IClassFixture<TexturesTests.Install>
{
    private readonly Install _install;

    public TexturesTests(Install install, ITestOutputHelper output)
    {
        _install = install;
        if (install.Files == null)
        {
            output.WriteLine(install.Reason);
        }
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

    /// <summary>The install, opened once for the class.</summary>
    public sealed class Install : IDisposable
    {
        public Install()
        {
            var folder = GameInstall.Find();
            if (folder == null)
            {
                Reason = $"No Warcraft III install in {string.Join(", ", GameInstall.Places())}; set {GameInstall.Variable} to run these tests";
                return;
            }

            Files = GameFiles.Open(folder);
        }

        public GameFiles? Files { get; }

        public string? Reason { get; }

        public void Dispose() => Files?.Dispose();
    }
}
