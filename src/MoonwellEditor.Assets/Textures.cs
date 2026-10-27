using System.Buffers.Binary;
using System.IO.Compression;
using War3Net.Drawing.Blp;

namespace MoonwellEditor.Assets;

/// <summary>An image as rows of RGBA bytes, top row first.</summary>
public sealed record Rgba(int Width, int Height, byte[] Pixels);

/// <summary>
/// Turns the game's textures into images: BLP (through War3Net's reader) and DDS (DXT1, DXT3, DXT5 or plain 32-bit),
/// the two kinds its icons are stored as, and writes them as PNG for the page.
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

    // A DDS file: a 128-byte header (its pixel format at 76), then the largest image first.
    private static Rgba? Dds(byte[] bytes)
    {
        var height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16));
        var flags = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(80));
        var fourCc = System.Text.Encoding.ASCII.GetString(bytes, 84, 4);
        var bits = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(88));
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
        {
            return null;
        }

        var data = bytes.AsSpan(128);
        var pixels = new byte[width * height * 4];
        const int FourCcFlag = 0x4;
        if ((flags & FourCcFlag) == 0)
        {
            if (bits != 32 || data.Length < pixels.Length)
            {
                return null;
            }

            // 32-bit, by its masks (the game's are BGRA).
            var red = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(92));
            var redShift = red == 0xFF ? 0 : 16;
            for (var i = 0; i < width * height; i++)
            {
                var pixel = BinaryPrimitives.ReadUInt32LittleEndian(data[(i * 4)..]);
                pixels[i * 4] = (byte)(pixel >> redShift);
                pixels[(i * 4) + 1] = (byte)(pixel >> 8);
                pixels[(i * 4) + 2] = (byte)(pixel >> (16 - redShift));
                pixels[(i * 4) + 3] = (byte)(pixel >> 24);
            }

            return new Rgba(width, height, pixels);
        }

        var blockSize = fourCc switch
        {
            "DXT1" => 8,
            "DXT3" or "DXT5" => 16,
            _ => 0,
        };
        var blocksWide = (width + 3) / 4;
        var blocksHigh = (height + 3) / 4;
        if (blockSize == 0 || data.Length < blocksWide * blocksHigh * blockSize)
        {
            return null;
        }

        Span<byte> alphas = stackalloc byte[16];
        for (var by = 0; by < blocksHigh; by++)
        {
            for (var bx = 0; bx < blocksWide; bx++)
            {
                var block = data.Slice(((by * blocksWide) + bx) * blockSize, blockSize);
                var colour = blockSize == 8 ? block : block[8..];
                alphas.Fill(255);
                if (fourCc == "DXT3")
                {
                    for (var i = 0; i < 16; i++)
                    {
                        alphas[i] = (byte)(((block[i / 2] >> ((i % 2) * 4)) & 0xF) * 17);
                    }
                }
                else if (fourCc == "DXT5")
                {
                    Dxt5Alpha(block, alphas);
                }

                var transparent = ColourBlock(colour, fourCc == "DXT1", out var palette);
                var codes = BinaryPrimitives.ReadUInt32LittleEndian(colour[4..]);
                for (var i = 0; i < 16; i++)
                {
                    int x = (bx * 4) + (i % 4), y = (by * 4) + (i / 4);
                    if (x >= width || y >= height)
                    {
                        continue;
                    }

                    var code = (int)((codes >> (i * 2)) & 3);
                    var at = ((y * width) + x) * 4;
                    pixels[at] = palette[code * 4];
                    pixels[at + 1] = palette[(code * 4) + 1];
                    pixels[at + 2] = palette[(code * 4) + 2];
                    pixels[at + 3] = transparent && code == 3 ? (byte)0 : alphas[i];
                }
            }
        }

        return new Rgba(width, height, pixels);
    }

    // The four colours of a block (as RGBA in palette); true when DXT1's fourth colour is transparent.
    private static bool ColourBlock(ReadOnlySpan<byte> block, bool dxt1, out byte[] palette)
    {
        var first = BinaryPrimitives.ReadUInt16LittleEndian(block);
        var second = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        palette = new byte[16];
        Expand(first, palette.AsSpan(0));
        Expand(second, palette.AsSpan(4));
        var fourColours = first > second || !dxt1;
        for (var channel = 0; channel < 3; channel++)
        {
            int a = palette[channel], b = palette[4 + channel];
            palette[8 + channel] = (byte)(fourColours ? ((2 * a) + b) / 3 : (a + b) / 2);
            palette[12 + channel] = (byte)(fourColours ? (a + (2 * b)) / 3 : 0);
        }

        palette[3] = palette[7] = palette[11] = palette[15] = 255;
        return !fourColours;
    }

    private static void Expand(ushort colour, Span<byte> into)
    {
        into[0] = (byte)(((colour >> 11) & 0x1F) * 255 / 31);
        into[1] = (byte)(((colour >> 5) & 0x3F) * 255 / 63);
        into[2] = (byte)((colour & 0x1F) * 255 / 31);
    }

    // DXT5: two alpha ends, then 3-bit codes for the 16 pixels.
    private static void Dxt5Alpha(ReadOnlySpan<byte> block, Span<byte> alphas)
    {
        int a0 = block[0], a1 = block[1];
        ulong codes = 0;
        for (var i = 0; i < 6; i++)
        {
            codes |= (ulong)block[2 + i] << (8 * i);
        }

        for (var i = 0; i < 16; i++)
        {
            var code = (int)((codes >> (3 * i)) & 7);
            alphas[i] = (byte)(code switch
            {
                0 => a0,
                1 => a1,
                _ when a0 > a1 => (((8 - code) * a0) + ((code - 1) * a1)) / 7,
                6 => 0,
                7 => 255,
                _ => (((6 - code) * a0) + ((code - 1) * a1)) / 5,
            });
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
