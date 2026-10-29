using System.Text;

namespace MoonwellEditor.Map.Tests;

public sealed class DayLightsTests(ITestOutputHelper output)
{
    [Fact]
    public void Classic_Lordaeron_noon_is_the_sun_from_the_south_west()
    {
        var files = InstallGameFiles.Open();
        if (files == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var lights = DayLights.Read("L", files, "sd");

        // FDirectSun's one rotation turns straight up toward the south-west, 38 degrees above the ground.
        Near([-0.6755f, -0.4058f, 0.6157f], lights.Terrain.Toward);
        Near([1.2949f, 1.2949f, 1.2949f], lights.Terrain.Colour); // white, 0.9961, at intensity 1.3
        Near([0.1961f, 0.1680f, 0.1680f], lights.Terrain.Ambient); // (0.9804, 0.8399, 0.8399) at 0.2
        Near(lights.Terrain.Toward, lights.Units.Toward);
        Near([0.2941f, 0.2520f, 0.2520f], lights.Units.Ambient); // the units' light has 0.3
        Assert.Empty(lights.Sky);
    }

    [Fact]
    public void Definitive_Lordaeron_noon_is_between_its_keys()
    {
        var files = InstallGameFiles.Open();
        if (files == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        var lights = DayLights.Read("L", files, "de");

        // Noon is frame 33367 of 3367 to 63367: the rotation and colour between their keys either side.
        Near([-0.2357f, -0.4120f, 0.8802f], lights.Terrain.Toward);
        Near([0.9542f, 0.9575f, 0.9955f], lights.Terrain.Colour);
        Near([0f, 0f, 0.1f], lights.Terrain.Ambient);
        Assert.Equal(18, lights.Sky.Length);
        var up = lights.Sky[12..15].Sum();
        var down = lights.Sky[15..18].Sum();
        for (var face = 0; face < 4; face++)
        {
            var side = lights.Sky[(face * 3)..(face * 3 + 3)].Sum();
            Assert.InRange(side, down, up);
        }
    }

    [Fact]
    public void Icecrown_takes_its_own_lights_and_Lordaeron_Summers_sky()
    {
        var files = InstallGameFiles.Open();
        if (files == null)
        {
            output.WriteLine(InstallMaps.NoInstall);
            return;
        }

        // World Editor's data gives Icecrown Lordaeron's lights and no environment map of its own.
        Assert.Equal(DayLights.Read("L", files, "de"), DayLights.Read("I", files, "de"), new SameLights());
    }

    [Fact]
    public void Without_the_files_the_light_is_the_plan_default()
    {
        var lights = DayLights.Read("L", new NoGameFiles(), "de");

        Assert.Equal(DayLights.Default, lights.Terrain);
        Assert.Equal(DayLights.Default, lights.Units);
        Assert.Empty(lights.Sky);
    }

    [Fact]
    public void Reads_a_light_of_the_old_layout_and_takes_its_static_values()
    {
        var mdx = Mdx(800, light: Light(800, intensity: 2f, ambient: 0.5f));

        var light = DayLights.Noon(mdx);

        Assert.NotNull(light);
        Near([0f, 0f, 1f], light.Toward);
        Near([2f, 2f, 2f], light.Colour);
        Near([0.5f, 0.5f, 0.5f], light.Ambient);
    }

    [Fact]
    public void Is_null_for_a_model_with_no_directional_light()
    {
        Assert.Null(DayLights.Noon(Mdx(800, light: null)));
        Assert.Null(DayLights.Noon([1, 2, 3]));
    }

    [Fact]
    public void A_file_of_bad_sizes_reads_as_no_light_without_hanging()
    {
        // A chunk that says it is -8 long, then a light that says it is 0 long.
        byte[] negative = [.. Encoding.ASCII.GetBytes("MDLX"), .. Encoding.ASCII.GetBytes("JUNK"), .. BitConverter.GetBytes(-8)];
        byte[] lite = [.. Encoding.ASCII.GetBytes("LITE"), .. BitConverter.GetBytes(8), .. new byte[8]];
        byte[] empty = [.. Encoding.ASCII.GetBytes("MDLX"), .. lite];

        Assert.Null(Finishes(() => DayLights.Noon(negative)));
        Assert.Null(Finishes(() => DayLights.Noon(empty)));
    }

    [Fact]
    public void An_environment_map_of_impossible_size_reads_as_no_sky()
    {
        var dds = new byte[200];
        Encoding.ASCII.GetBytes("DDS ").CopyTo(dds, 0);
        Encoding.ASCII.GetBytes("DX10").CopyTo(dds, 84);
        BitConverter.GetBytes(int.MaxValue).CopyTo(dds, 16); // width
        BitConverter.GetBytes(int.MaxValue).CopyTo(dds, 28); // mipmaps
        BitConverter.GetBytes(72).CopyTo(dds, 128); // BC1 sRGB
        BitConverter.GetBytes(4).CopyTo(dds, 136); // a cube
        BitConverter.GetBytes(2).CopyTo(dds, 140); // two of them

        Assert.Null(Finishes(() => DayLights.SkyColours(dds)));
    }

    // The result of a read that must end within two seconds.
    private static T Finishes<T>(Func<T> read)
    {
        var task = Task.Run(read);
        Assert.True(task.Wait(TimeSpan.FromSeconds(2)), "the read did not end");
        return task.Result;
    }

    private static void Near(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], 0.002f);
        }
    }

    // A model file of a version with one sequence, 0 to 60000, and the given light chunk.
    private static byte[] Mdx(int version, byte[]? light)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("MDLX"));
        writer.Write(Encoding.ASCII.GetBytes("VERS"));
        writer.Write(4);
        writer.Write(version);
        writer.Write(Encoding.ASCII.GetBytes("SEQS"));
        writer.Write(132);
        writer.Write(new byte[80]);
        writer.Write(0);
        writer.Write(60000);
        writer.Write(new byte[132 - 88]);
        if (light != null)
        {
            writer.Write(Encoding.ASCII.GetBytes("LITE"));
            writer.Write(light.Length);
            writer.Write(light);
        }

        return stream.ToArray();
    }

    // One directional light with no tracks: white at `intensity`, white ambient at `ambient`.
    private static byte[] Light(int version, float intensity, float ambient)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0); // the light's size, set below
        writer.Write(96); // the node's size: no tracks
        writer.Write(new byte[80]);
        writer.Write(0);
        writer.Write(-1);
        writer.Write(0x200);
        writer.Write(1); // directional
        if (version > 800)
        {
            writer.Write(0f);
        }

        float[] values = [80f, 200f, 1f, 1f, 1f, intensity, 1f, 1f, 1f, ambient];
        foreach (var value in values)
        {
            writer.Write(value);
        }

        if (version > 800)
        {
            writer.Write(new byte[6 * 4]);
        }

        var bytes = stream.ToArray();
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 0);
        return bytes;
    }

    private sealed class SameLights : IEqualityComparer<SceneLights>
    {
        public bool Equals(SceneLights? x, SceneLights? y) =>
            x != null && y != null && x.Terrain.Toward.SequenceEqual(y.Terrain.Toward)
            && x.Units.Colour.SequenceEqual(y.Units.Colour) && x.Sky.SequenceEqual(y.Sky);

        public int GetHashCode(SceneLights obj) => 0;
    }
}
