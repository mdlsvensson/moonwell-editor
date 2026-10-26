using System.Text;

namespace MoonwellEditor.Files.Tests;

public class WtsFileTests
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    public static TheoryData<string> WtsFixtures() =>
    [
        "we-3.0/war3map.wts",
        "moonwell-mymap/war3map.wts",
        "we-heavy/war3map.wts",
    ];

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    [Theory]
    [MemberData(nameof(WtsFixtures))]
    public void Round_trips_every_fixture(string file)
    {
        var bytes = Fixtures.Bytes(file);

        Assert.Equal(bytes, WtsFile.Read(bytes).ToBytes());
    }

    [Fact]
    public void Reads_entries()
    {
        var we = WtsFile.Read(Fixtures.Bytes("we-3.0/war3map.wts"));
        Assert.Equal("TypeScript Template", we.Get(1));
        Assert.Equal("Ability", we.Get(16));
        Assert.Null(we.Get(9999));

        var moonwell = WtsFile.Read(Fixtures.Bytes("moonwell-mymap/war3map.wts"));
        Assert.Equal("Moonwell Template", moonwell.Get(1));
    }

    [Fact]
    public void Set_keeps_bom_and_crlf()
    {
        var original = Fixtures.Bytes("we-3.0/war3map.wts");
        var file = WtsFile.Read(original);

        Assert.True(file.Set(16, "Line one\nLine two"));
        var bytes = file.ToBytes();

        Assert.Equal(Bom, bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        Assert.DoesNotContain("\r", text.Replace("\r\n", ""));

        var expected = Encoding.UTF8.GetString(original, 3, original.Length - 3)
            .Replace("STRING 16\r\n// Abilities: A000 (Ability), Name (Name)\r\n{\r\nAbility\r\n}",
                "STRING 16\r\n// Abilities: A000 (Ability), Name (Name)\r\n{\r\nLine one\r\nLine two\r\n}");
        Assert.NotEqual(Encoding.UTF8.GetString(original, 3, original.Length - 3), expected);
        Assert.Equal(Bom.Concat(Utf8(expected)).ToArray(), bytes);

        var reread = WtsFile.Read(bytes);
        Assert.Equal("Line one\nLine two", reread.Get(16));
        Assert.Equal("TypeScript Template", reread.Get(1));
    }

    [Fact]
    public void Set_returns_false_for_a_missing_entry_and_for_a_closing_brace_line()
    {
        var bytes = Fixtures.Bytes("we-3.0/war3map.wts");
        var file = WtsFile.Read(bytes);

        Assert.False(file.Set(9999, "x"));
        Assert.False(file.Set(16, "before\n}\nafter"));
        Assert.Equal(bytes, file.ToBytes());
        Assert.True(file.Set(16, "a } b\n{"));
    }

    [Fact]
    public void Comments_and_padding_kept()
    {
        var source = "junk before\nSTRING 3\n  // a comment\n// another\n{\nold\ntext\n}\n\n\nodd   spacing\nSTRING 4\n{\nfour\n}";
        var bytes = Utf8(source);
        var file = WtsFile.Read(bytes);

        Assert.Equal(bytes, file.ToBytes());
        Assert.Equal("old\ntext", file.Get(3));
        Assert.Equal("four", file.Get(4));

        Assert.True(file.Set(3, "new\nlines"));
        Assert.True(file.Set(4, "FOUR"));

        // No final line end stays missing; the file's line end here is LF, and everything else is untouched.
        Assert.Equal(
            "junk before\nSTRING 3\n  // a comment\n// another\n{\nnew\nlines\n}\n\n\nodd   spacing\nSTRING 4\n{\nFOUR\n}",
            Encoding.UTF8.GetString(file.ToBytes()));
    }

    [Fact]
    public void A_file_without_any_line_end_uses_crlf_when_set()
    {
        var file = WtsFile.Read(Utf8("STRING 1\r\n{\r\nx\r\n}"));
        Assert.Equal(Utf8("STRING 1\r\n{\r\nx\r\n}"), file.ToBytes());
        Assert.True(file.Set(1, "a\nb"));
        Assert.Equal(Utf8("STRING 1\r\n{\r\na\r\nb\r\n}"), file.ToBytes());
    }

    [Fact]
    public void Empty_file_reads_with_no_entries()
    {
        var file = WtsFile.Read([]);
        Assert.Null(file.Get(1));
        Assert.Empty(file.ToBytes());
    }

    [Theory]
    [InlineData("TRIGSTR_016", 16)]
    [InlineData("TRIGSTR_1", 1)]
    [InlineData("TRIGSTR_000", 0)]
    [InlineData("TRIGSTR_", null)]
    [InlineData("trigstr_016", null)]
    [InlineData("TRIGSTR_1x", null)]
    [InlineData("TRIGSTR_-1", null)]
    [InlineData("TRIGSTR_99999999999", null)]
    [InlineData("", null)]
    [InlineData("Footman", null)]
    public void Trigger_string_parses(string value, int? expected)
    {
        Assert.Equal(expected, WtsFile.TriggerString(value));
    }
}
