namespace ObjectEditor.Files;

/// <summary>
/// An MPQ archive's hash table as the file holds it, read and written without War3Net, and looked up the way the
/// game's own MPQ code (Storm) looks a file up. War3Net's table and its hashing are internal, and its lookup does not
/// stop at an empty slot as the game's does.
/// </summary>
internal static class StormHashTable
{
    /// <summary>What an empty slot's block index is: the game's lookup stops at one.</summary>
    public const uint EmptyBlock = 0xFFFFFFFF;

    /// <summary>What a deleted entry's block index is: the game's lookup steps over one.</summary>
    public const uint DeletedBlock = 0xFFFFFFFE;

    private static readonly uint[] CryptTable = MakeCryptTable();

    private static readonly uint TableKey = Hash("(hash table)", 0x300);

    /// <summary>
    /// The MPQ format's hash of a file name or key: type 0 gives the name's home slot, 0x100 and 0x200 the two hashes
    /// a slot keeps of the name, 0x300 a table's key. Letter case is ignored, as War3Net ignores it.
    /// </summary>
    public static uint Hash(string text, uint type)
    {
        uint seed1 = 0x7FED7FED;
        uint seed2 = 0xEEEEEEEE;
        foreach (var letter in text.ToUpperInvariant())
        {
            if (letter >= 0x200)
            {
                throw new ArgumentException($"\"{text}\" has a letter an MPQ file name cannot hash.", nameof(text));
            }

            seed1 = CryptTable[type + letter] ^ (seed1 + seed2);
            seed2 = letter + seed1 + seed2 + (seed2 << 5) + 3;
        }

        return seed1;
    }

    /// <summary>
    /// Reads the hash table of the archive whose MPQ header is at <paramref name="header"/> in
    /// <paramref name="stream"/> (its position and size from the header, as War3Net reads them), decrypted.
    /// </summary>
    public static Slot[] Read(Stream stream, long header)
    {
        var (position, size) = Where(stream, header);
        var bytes = new byte[size * 16];
        stream.Position = position;
        stream.ReadExactly(bytes);
        var data = new uint[size * 4];
        Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
        Decrypt(data, TableKey);
        var slots = new Slot[size];
        for (var i = 0; i < size; i++)
        {
            slots[i] = new Slot(data[i * 4], data[i * 4 + 1], data[i * 4 + 2], data[i * 4 + 3]);
        }

        return slots;
    }

    /// <summary>Writes <paramref name="slots"/>, encrypted, over the archive's hash table (the same size).</summary>
    public static void Write(Stream stream, long header, Slot[] slots)
    {
        var (position, size) = Where(stream, header);
        if (size != slots.Length)
        {
            throw new ArgumentException($"The table has {size} slots, not {slots.Length}.", nameof(slots));
        }

        var data = new uint[size * 4];
        for (var i = 0; i < size; i++)
        {
            (data[i * 4], data[i * 4 + 1], data[i * 4 + 2], data[i * 4 + 3]) =
                (slots[i].Name1, slots[i].Name2, slots[i].Locale, slots[i].Block);
        }

        Encrypt(data, TableKey);
        var bytes = new byte[data.Length * 4];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        stream.Position = position;
        stream.Write(bytes);
    }

    /// <summary>
    /// The slot the game finds a named file in: from the slot its name hashes to, forward to the first empty slot, the
    /// first entry with the name's two hashes and <paramref name="locale"/> that points at one of the
    /// <paramref name="blocks"/> blocks. -1 when there is none.
    /// </summary>
    public static int Find(Slot[] slots, string name, uint locale, int blocks)
    {
        var (name1, name2) = (Hash(name, 0x100), Hash(name, 0x200));
        var home = (int)(Hash(name, 0) & (uint)(slots.Length - 1));
        return Walk(slots, home, name1, name2, locale, blocks).Slot;
    }

    /// <summary>
    /// The slot the game finds a file with no known name in, in <paramref name="now"/>, where <paramref name="old"/>
    /// had it at <paramref name="slot"/>; -1 when it might not be found. Its home slot (where its name hashes to) is
    /// not known, but the game found it in the old table, so the home is in the run of non-empty slots that ends at
    /// its old slot. From the start of that run the new table must lead over non-empty slots to the same name and
    /// locale, at its old slot or after it, with no entry of that name and locale before it: then the game finds it
    /// from whichever slot of the run its home is.
    /// </summary>
    public static int FindUnnamed(Slot[] old, Slot[] now, int slot, int blocks)
    {
        var size = old.Length;
        var back = 0;
        while (back < size - 1 && !old[(slot - back - 1 + size) % size].IsEmpty)
        {
            back++;
        }

        var entry = old[slot];
        var (found, steps) = Walk(now, (slot - back + size) % size, entry.Name1, entry.Name2, entry.Locale, blocks);
        return found >= 0 && steps >= back ? found : -1;
    }

    // From start, forward to the first empty slot: the first entry with these hashes and locale that points at a
    // block, and how many slots after start it is; (-1, -1) when there is none.
    private static (int Slot, int Steps) Walk(Slot[] slots, int start, uint name1, uint name2, uint locale, int blocks)
    {
        for (var step = 0; step < slots.Length; step++)
        {
            var i = (start + step) % slots.Length;
            var slot = slots[i];
            if (slot.IsEmpty)
            {
                break;
            }

            if (slot.Name1 == name1 && slot.Name2 == name2 && slot.Locale == locale && slot.Block < (uint)blocks)
            {
                return (i, step);
            }
        }

        return (-1, -1);
    }

    // The hash table's position in the stream and its number of slots, from the MPQ header at header.
    private static (long Position, int Size) Where(Stream stream, long header)
    {
        var fields = new byte[12];
        stream.Position = header + 16;
        stream.ReadExactly(fields);
        var offset = BitConverter.ToUInt32(fields, 0);
        var size = BitConverter.ToUInt32(fields, 8);
        if (size == 0 || size > 1 << 20 || (size & (size - 1)) != 0)
        {
            throw new InvalidDataException($"The hash table's size, {size}, is not a power of two.");
        }

        return (header + offset, (int)size);
    }

    private static void Decrypt(uint[] data, uint key)
    {
        uint seed = 0xEEEEEEEE;
        for (var i = 0; i < data.Length; i++)
        {
            seed += CryptTable[0x400 + (key & 0xFF)];
            data[i] ^= key + seed;
            key = ((~key << 0x15) + 0x11111111) | (key >> 0x0B);
            seed = data[i] + seed + (seed << 5) + 3;
        }
    }

    private static void Encrypt(uint[] data, uint key)
    {
        uint seed = 0xEEEEEEEE;
        for (var i = 0; i < data.Length; i++)
        {
            seed += CryptTable[0x400 + (key & 0xFF)];
            var plain = data[i];
            data[i] ^= key + seed;
            key = ((~key << 0x15) + 0x11111111) | (key >> 0x0B);
            seed = plain + seed + (seed << 5) + 3;
        }
    }

    private static uint[] MakeCryptTable()
    {
        var table = new uint[0x500];
        uint seed = 0x00100001;
        for (var first = 0; first < 0x100; first++)
        {
            for (var i = 0; i < 5; i++)
            {
                seed = (seed * 125 + 3) % 0x2AAAAB;
                var high = (seed & 0xFFFF) << 0x10;
                seed = (seed * 125 + 3) % 0x2AAAAB;
                table[first + i * 0x100] = high | (seed & 0xFFFF);
            }
        }

        return table;
    }

    /// <summary>One slot of the hash table.</summary>
    /// <param name="Name1">The name's first hash (type 0x100).</param>
    /// <param name="Name2">The name's second hash (type 0x200).</param>
    /// <param name="Locale">The locale (low 16 bits) and platform (high 16 bits); 0 for neutral.</param>
    /// <param name="Block">The block index, or <see cref="EmptyBlock"/> or <see cref="DeletedBlock"/>.</param>
    public readonly record struct Slot(uint Name1, uint Name2, uint Locale, uint Block)
    {
        /// <summary>A deleted entry, as Storm writes one.</summary>
        public static Slot Deleted => new(0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, DeletedBlock);

        /// <summary>An empty slot, as Storm writes one.</summary>
        public static Slot Empty => new(0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, EmptyBlock);

        /// <summary>True for an empty slot, where the game's lookup stops.</summary>
        public bool IsEmpty => Block == EmptyBlock;

        /// <summary>True for a slot that holds an entry, not a deleted one.</summary>
        public bool IsLive => Block < DeletedBlock;

        /// <summary>The two name hashes as War3Net's <c>MpqHash.Name</c> combines them.</summary>
        public ulong Name => Name1 | ((ulong)Name2 << 32);
    }
}
