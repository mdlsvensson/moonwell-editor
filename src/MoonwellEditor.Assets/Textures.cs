using System.Buffers.Binary;
using System.IO.Compression;
using Pfim;
using War3Net.Drawing.Blp;

namespace MoonwellEditor.Assets;

/// <summary>An image as rows of RGBA bytes, top row first.</summary>
public sealed record Rgba(int Width, int Height, byte[] Pixels);

/// <summary>
/// Turns the game's textures into images: BLP (through War3Net's reader) and DDS (through Pfim's), the two kinds its
/// icons are stored as, and writes them as PNG for the page.
/// </summary>
public static class Textures
{
    /// <summary>The texture's largest image; null when it is neither a BLP nor a DDS the editor reads.</summary>
    public static Rgba? Decode(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 'B' && bytes[1] == 'L' && bytes[2] == 'P')
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                var blp = new BlpFile(stream);
                var pixels = blp.GetPixels(0, out var width, out var height, bgra: false);
                return new Rgba(width, height, pixels);
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException
                                                  or EndOfStreamException or ArgumentException or IndexOutOfRangeException)
            {
                return null;
            }
        }

        return bytes.Length >= 128 && bytes[0] == 'D' && bytes[1] == 'D' && bytes[2] == 'S' && bytes[3] == ' '
            ? Dds(bytes)
            : null;
    }

    // A DDS file, read with Pfim (Sven, 2026-10-28): its largest image, as RGBA.
    private static Rgba? Dds(byte[] bytes)
    {
        try
        {
            using var image = Pfim.Dds.Create(bytes, new PfimConfig());
            if (image.Compressed)
            {
                image.Decompress();
            }

            var (width, height) = (image.Width, image.Height);
            var size = image.Format switch
            {
                ImageFormat.Rgba32 => 4,
                ImageFormat.Rgb24 => 3,
                _ => 0,
            };
            if (size == 0 || width <= 0 || height <= 0 || image.DataLen < image.Stride * height)
            {
                return null;
            }

            // Pfim gives BGRA or BGR rows, each Stride bytes long.
            var pixels = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var from = (y * image.Stride) + (x * size);
                    var to = ((y * width) + x) * 4;
                    pixels[to] = image.Data[from + 2];
                    pixels[to + 1] = image.Data[from + 1];
                    pixels[to + 2] = image.Data[from];
                    pixels[to + 3] = size == 4 ? image.Data[from + 3] : (byte)255;
                }
            }

            return new Rgba(width, height, pixels);
        }
        catch (Exception)
        {
            // Pfim says a file is damaged with a plain Exception ("Not a valid header size"), so any is caught.
            return null;
        }
    }

    /// <summary>The image as a PNG file (8-bit RGBA, no filtering).</summary>
    public static byte[] Png(Rgba image)
    {
        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8; // bits per channel
        header[9] = 6; // RGBA
        Chunk(output, "IHDR", header);

        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = image.Width * 4;
            for (var y = 0; y < image.Height; y++)
            {
                zlib.WriteByte(0); // no filter
                zlib.Write(image.Pixels, y * row, row);
            }
        }

        Chunk(output, "IDAT", raw.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        var typed = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, typed);
        data.CopyTo(typed, 4);
        output.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typed));
        output.Write(number);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }

        return c;
    }).ToArray();

    // PNG's CRC-32 of a chunk's type and data.
    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFF;
        foreach (var b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }
}
