using System.Numerics;
using System.Text;

namespace MoonwellEditor.Map;

/// <summary>One of the tileset's lights at noon.</summary>
/// <param name="Toward">The direction toward the light, a unit vector (x east, y north, z up).</param>
/// <param name="Colour">The light's colour times its intensity: red, green and blue.</param>
/// <param name="Ambient">The light's ambient colour times its ambient intensity.</param>
public sealed record SceneLight(float[] Toward, float[] Colour, float[] Ambient);

/// <summary>The tileset's lights at noon, as World Editor shows the map (its lighting set to Noon).</summary>
/// <param name="Terrain">The terrain's light, for the ground and the cliffs.</param>
/// <param name="Units">The units' light, for every placed thing's model.</param>
/// <param name="Sky">
/// For HD and Definitive Edition, the environment map's colour seen along +x, -x, +y, -y, +z and -z: 18 numbers, sRGB
/// from 0 to 1. Empty in Classic, or without one.
/// </param>
public sealed record SceneLights(SceneLight Terrain, SceneLight Units, float[] Sky);

/// <summary>
/// The tileset's day and night lights (*claim*: models the game plays over its day, read by hand): World Editor's data
/// (<c>UI\WorldEditData.txt</c>) names a terrain and a unit model a tileset (<c>[TerrainLights]</c>,
/// <c>[UnitLights]</c>), each a directional light whose colour, intensity, ambient and turn are keyed over its first
/// sequence, one day from midnight. Noon is that sequence's middle. HD and Definitive Edition also light by an
/// environment map (<c>[DayEnvironmentMap]</c>, Lordaeron Summer's for a tileset with none).
/// </summary>
public static class DayLights
{
    /// <summary>The light without the tileset's files: high in the south-west (plan's call).</summary>
    public static readonly SceneLight Default = new([-0.3f, -0.35f, 0.89f], [1f, 1f, 1f], [0.3f, 0.3f, 0.3f]);

    /// <summary>The lights of a tileset (its letter, <c>L</c>) in a graphics mode.</summary>
    public static SceneLights Read(string tileset, IMapGameFiles files, string mode)
    {
        var data = files.File("UI/WorldEditData.txt", mode);
        var text = data == null ? "" : Encoding.UTF8.GetString(data);
        SceneLight Light(string section)
        {
            var path = Entry(text, section, tileset);
            var model = path == null ? null : files.File(path, mode);
            return (model == null ? null : Noon(model)) ?? Default;
        }

        float[] sky = [];
        if (mode != "sd")
        {
            var path = Entry(text, "DayEnvironmentMap", tileset) ?? Entry(text, "DayEnvironmentMap", "L");
            var map = path == null ? null : files.File(Path.ChangeExtension(path, ".dds"), mode);
            sky = map == null ? [] : SkyColours(map) ?? [];
        }

        return new SceneLights(Light("TerrainLights"), Light("UnitLights"), sky);
    }

    // A tileset's entry in a section of World Editor's data, skipping the ones put out with //.
    private static string? Entry(string text, string section, string tileset)
    {
        var inSection = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inSection = line == "[" + section + "]";
                continue;
            }

            if (inSection && line.StartsWith(tileset + "=", StringComparison.Ordinal))
            {
                return line[(tileset.Length + 1)..].Trim().Trim('"');
            }
        }

        return null;
    }

    /// <summary>
    /// The first directional light of a model file at noon, the middle of its first sequence; null for a file that is
    /// not a model or has no directional light. A light's direction is its node's turn of straight down (the light's
    /// shadows fall that way, as World Editor draws them). Keys are read as linear, their tangents left out.
    /// </summary>
    public static SceneLight? Noon(byte[] mdx)
    {
        if (mdx.Length < 4 || Encoding.ASCII.GetString(mdx, 0, 4) != "MDLX")
        {
            return null;
        }

        try
        {
            var version = 800;
            var noon = 0f;
            for (var at = 4; at + 8 <= mdx.Length;)
            {
                var tag = Encoding.ASCII.GetString(mdx, at, 4);
                var size = BitConverter.ToInt32(mdx, at + 4);
                var data = at + 8;
                if (size < 0 || size > mdx.Length - data)
                {
                    return null;
                }

                switch (tag)
                {
                    case "VERS":
                        version = BitConverter.ToInt32(mdx, data);
                        break;
                    case "SEQS" when size >= 88:
                        noon = (BitConverter.ToUInt32(mdx, data + 80) + BitConverter.ToUInt32(mdx, data + 84)) / 2f;
                        break;
                    case "LITE":
                        return Lights(mdx, data, data + size, version, noon);
                }

                at = data + size;
            }
        }
        catch (ArgumentException)
        {
            // a chunk that runs past the end
        }

        return null;
    }

    // The first directional light in a LITE chunk at a frame.
    private static SceneLight? Lights(byte[] mdx, int start, int end, int version, float frame)
    {
        for (var at = start; at < end;)
        {
            var size = BitConverter.ToInt32(mdx, at);
            var node = at + 4;
            if (size <= 0 || size > end - at)
            {
                return null;
            }

            var nodeSize = BitConverter.ToInt32(mdx, node);
            if (nodeSize < 96 || nodeSize > size - 4)
            {
                return null;
            }
            var turn = Quaternion.Identity;
            foreach (var (tag, keys) in Tracks(mdx, node + 96, node + nodeSize))
            {
                if (tag == "KGRT")
                {
                    var q = At(keys, frame);
                    turn = Quaternion.Normalize(new Quaternion(q[0], q[1], q[2], q[3]));
                }
            }

            var statics = node + nodeSize;
            var type = BitConverter.ToInt32(mdx, statics);
            // Version 1800 has one number more before the attenuation and six after the ambient intensity.
            var colourAt = statics + (version > 800 ? 16 : 12);
            float Static(int index) => BitConverter.ToSingle(mdx, colourAt + index * 4);
            float[] colour = [Static(0), Static(1), Static(2)];
            var intensity = Static(3);
            float[] ambient = [Static(4), Static(5), Static(6)];
            var ambientIntensity = Static(7);
            foreach (var (tag, keys) in Tracks(mdx, colourAt + (version > 800 ? 14 : 8) * 4, at + size))
            {
                var value = At(keys, frame);
                switch (tag)
                {
                    case "KLAC":
                        colour = value;
                        break;
                    case "KLAI":
                        intensity = value[0];
                        break;
                    case "KLBC":
                        ambient = value;
                        break;
                    case "KLBI":
                        ambientIntensity = value[0];
                        break;
                }
            }

            if (type == 1)
            {
                var toward = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, turn));
                return new SceneLight(
                    [toward.X, toward.Y, toward.Z],
                    colour.Select(channel => channel * intensity).ToArray(),
                    ambient.Select(channel => channel * ambientIntensity).ToArray());
            }

            at += size;
        }

        return null;
    }

    // The tracks a node or light holds, each its tag and keys (frame and value); stops at a tag it does not know.
    private static List<(string Tag, List<(int Frame, float[] Value)> Keys)> Tracks(byte[] mdx, int at, int end)
    {
        var sizes = new Dictionary<string, int>
        {
            ["KGTR"] = 3, ["KGRT"] = 4, ["KGSC"] = 3, ["KLAS"] = 1, ["KLAE"] = 1, ["KLAC"] = 3, ["KLAI"] = 1,
            ["KLBC"] = 3, ["KLBI"] = 1, ["KLAV"] = 1,
        };
        var tracks = new List<(string, List<(int, float[])>)>();
        while (at + 16 <= end)
        {
            var tag = Encoding.ASCII.GetString(mdx, at, 4);
            if (!sizes.TryGetValue(tag, out var width))
            {
                break;
            }

            var count = BitConverter.ToInt32(mdx, at + 4);
            var tangents = BitConverter.ToInt32(mdx, at + 8) > 1;
            var key = at + 16;
            var keyBytes = 4 + (width * 4 * (tangents ? 3 : 1));
            if (count < 0 || count > (end - key) / keyBytes)
            {
                break;
            }

            var keys = new List<(int, float[])>();
            for (var i = 0; i < count; i++)
            {
                var value = new float[width];
                for (var j = 0; j < width; j++)
                {
                    value[j] = BitConverter.ToSingle(mdx, key + 4 + j * 4);
                }

                keys.Add((BitConverter.ToInt32(mdx, key), value));
                key += 4 + width * 4 * (tangents ? 3 : 1);
            }

            tracks.Add((tag, keys));
            at = key;
        }

        return tracks;
    }

    // A track's value at a frame: linear between the keys either side, the first or last key outside them.
    private static float[] At(List<(int Frame, float[] Value)> keys, float frame)
    {
        if (keys.Count == 0)
        {
            return [0f, 0f, 0f, 1f];
        }

        var after = keys.FindIndex(key => key.Frame >= frame);
        if (after <= 0)
        {
            return after == 0 ? keys[0].Value : keys[^1].Value;
        }

        var (from, to) = (keys[after - 1], keys[after]);
        var t = (frame - from.Frame) / Math.Max(to.Frame - from.Frame, 1);
        return from.Value.Select((value, index) => value + (to.Value[index] - value) * t).ToArray();
    }

    /// <summary>
    /// The colour of each face of an environment map's second cube (its diffuse light, *claim*), from the face's
    /// smallest mipmap: a DDS cube array of BC1 blocks with a DX10 header. Null for any other file.
    /// </summary>
    public static float[]? SkyColours(byte[] dds)
    {
        if (dds.Length < 148 || Encoding.ASCII.GetString(dds, 0, 4) != "DDS "
            || Encoding.ASCII.GetString(dds, 84, 4) != "DX10")
        {
            return null;
        }

        var format = BitConverter.ToInt32(dds, 128);
        var width = BitConverter.ToInt32(dds, 16);
        var mips = Math.Max(BitConverter.ToInt32(dds, 28), 1);
        var cubes = BitConverter.ToInt32(dds, 140);
        // A map of a size no file of this one's length can hold is not read.
        if (format is not (71 or 72) || (BitConverter.ToInt32(dds, 136) & 4) == 0 || cubes < 1
            || width is < 1 or > 16384 || mips > 16)
        {
            return null;
        }

        var cube = Math.Min(cubes - 1, 1);
        long faceBytes = 0;
        for (int level = 0, side = width; level < mips; level++, side = Math.Max(side / 2, 1))
        {
            faceBytes += (long)Math.Max((side + 3) / 4, 1) * Math.Max((side + 3) / 4, 1) * 8;
        }

        if (148 + ((cube + 1) * 6 * faceBytes) > dds.Length)
        {
            return null;
        }

        var colours = new float[18];
        for (var face = 0; face < 6; face++)
        {
            var block = (int)(148 + ((cube * 6 + face + 1) * faceBytes) - 8);
            var texel = Bc1First(dds, block);
            texel.CopyTo(colours, face * 3);
        }

        return colours;
    }

    // The first texel of a BC1 block, sRGB from 0 to 1.
    private static float[] Bc1First(byte[] dds, int at)
    {
        int c0 = BitConverter.ToUInt16(dds, at), c1 = BitConverter.ToUInt16(dds, at + 2);
        float[] Rgb(int c) => [(c >> 11) / 31f, ((c >> 5) & 63) / 63f, (c & 31) / 31f];
        var (a, b) = (Rgb(c0), Rgb(c1));
        var index = dds[at + 4] & 3;
        return Enumerable.Range(0, 3).Select(i => index switch
        {
            0 => a[i],
            1 => b[i],
            2 => c0 > c1 ? (2 * a[i] + b[i]) / 3 : (a[i] + b[i]) / 2,
            _ => c0 > c1 ? (a[i] + 2 * b[i]) / 3 : 0f,
        }).ToArray();
    }
}
