using System.Text;

namespace MoonwellEditor.Files;

/// <summary>
/// A map's <c>war3map.wts</c> strings file. The file is kept as the bytes that were read, plus where each entry's text
/// sits in them; <see cref="ToBytes"/> writes every byte back as read except the text of entries that were set. That
/// keeps the byte order mark, the line ends, comments and any spacing World Editor wrote (War3Net loses the first two).
/// </summary>
public sealed class WtsFile
{
    private const string TriggerPrefix = "TRIGSTR_";
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly byte[] _original;
    private readonly byte[] _lineEnd;
    private readonly List<Entry> _entries;

    private WtsFile(byte[] original, byte[] lineEnd, List<Entry> entries)
    {
        _original = original;
        _lineEnd = lineEnd;
        _entries = entries;
    }

    /// <summary>
    /// Reads a strings file. An entry is a <c>STRING n</c> line, optional <c>//</c> comment lines, a <c>{</c> line, the
    /// text, and a <c>}</c> line. Anything else is kept as it is but is not an entry.
    /// </summary>
    public static WtsFile Read(byte[] bytes)
    {
        var original = (byte[])bytes.Clone();
        var lines = SplitLines(original);

        // The first line's own line end is the file's; a file with none uses CRLF.
        var lineEnd = lines.Count > 0 && lines[0].End < lines[0].Next
            ? original[lines[0].End..lines[0].Next]
            : "\r\n"u8.ToArray();

        var entries = new List<Entry>();
        var index = 0;
        while (index < lines.Count)
        {
            var number = EntryNumber(original, lines[index]);
            var found = number is null ? null : FindBody(original, lines, index, number.Value);
            if (found is null)
            {
                index++;
                continue;
            }

            entries.Add(found.Value.Entry);
            index = found.Value.NextLine;
        }

        return new WtsFile(original, lineEnd, entries);
    }

    /// <summary>
    /// The text of entry <paramref name="number"/>, with its lines joined by <c>\n</c>; null when there is no such
    /// entry. When a number appears twice, the first is the one used.
    /// </summary>
    public string? Get(int number)
    {
        var entry = Find(number);
        if (entry is null)
        {
            return null;
        }

        if (entry.NewLines is not null)
        {
            return string.Join("\n", entry.NewLines);
        }

        return Utf8.GetString(TextOf(entry)).Replace("\r\n", "\n");
    }

    /// <summary>
    /// Replaces the text of entry <paramref name="number"/>; lines in <paramref name="text"/> (split on <c>\n</c> or
    /// <c>\r\n</c>) are joined with the file's line end. Returns false, and changes nothing, when there is no such
    /// entry or when a line of the text is a lone <c>}</c>, which the format cannot hold.
    /// </summary>
    public bool Set(int number, string text)
    {
        var entry = Find(number);
        if (entry is null)
        {
            return false;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Any(line => line.Trim() == "}"))
        {
            return false;
        }

        entry.NewLines = lines;
        return true;
    }

    /// <summary>The file's bytes: as read, except for the text of entries that were set.</summary>
    public byte[] ToBytes()
    {
        var output = new List<byte>(_original.Length);
        var position = 0;
        foreach (var entry in _entries)
        {
            if (entry.NewLines is null)
            {
                continue;
            }

            output.AddRange(_original.AsSpan(position, entry.Start - position).ToArray());
            foreach (var line in entry.NewLines)
            {
                output.AddRange(Utf8.GetBytes(line));
                output.AddRange(_lineEnd);
            }

            position = entry.End;
        }

        output.AddRange(_original.AsSpan(position).ToArray());
        return output.ToArray();
    }

    /// <summary>
    /// The number in a trigger string such as <c>TRIGSTR_016</c> (16); null when <paramref name="value"/> is not one.
    /// </summary>
    public static int? TriggerString(string value)
    {
        if (!value.StartsWith(TriggerPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var digits = value[TriggerPrefix.Length..];
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
        {
            return null;
        }

        return int.TryParse(digits, out var number) ? number : null;
    }

    private Entry? Find(int number) => _entries.FirstOrDefault(entry => entry.Number == number);

    /// <summary>The entry's text as read: the body lines without the last line end.</summary>
    private byte[] TextOf(Entry entry)
    {
        var span = _original.AsSpan(entry.Start, entry.End - entry.Start);
        if (span.EndsWith("\r\n"u8))
        {
            span = span[..^2];
        }
        else if (span.EndsWith("\n"u8))
        {
            span = span[..^1];
        }

        return span.ToArray();
    }

    private static int? EntryNumber(byte[] bytes, Line line)
    {
        var text = LineText(bytes, line);
        if (!text.StartsWith("STRING ", StringComparison.Ordinal))
        {
            return null;
        }

        var digits = text["STRING ".Length..].Trim();
        return digits.Length > 0 && digits.All(char.IsAsciiDigit) && int.TryParse(digits, out var number)
            ? number
            : null;
    }

    private static (Entry Entry, int NextLine)? FindBody(byte[] bytes, List<Line> lines, int stringLine, int number)
    {
        var brace = stringLine + 1;
        while (brace < lines.Count && LineText(bytes, lines[brace]).StartsWith("//", StringComparison.Ordinal))
        {
            brace++;
        }

        if (brace >= lines.Count || LineText(bytes, lines[brace]) != "{")
        {
            return null;
        }

        for (var close = brace + 1; close < lines.Count; close++)
        {
            if (LineText(bytes, lines[close]) == "}")
            {
                return (new Entry(number, lines[brace].Next, lines[close].Start), close + 1);
            }
        }

        return null;
    }

    /// <summary>A line's text without surrounding spaces; a byte order mark at the file's start is not text.</summary>
    private static string LineText(byte[] bytes, Line line) =>
        Utf8.GetString(bytes, line.Start, line.End - line.Start).Trim().TrimStart('﻿');

    private static List<Line> SplitLines(byte[] bytes)
    {
        var lines = new List<Line>();
        var start = 0;
        while (start < bytes.Length)
        {
            var newline = Array.IndexOf(bytes, (byte)'\n', start);
            if (newline < 0)
            {
                lines.Add(new Line(start, bytes.Length, bytes.Length));
                break;
            }

            var end = newline > start && bytes[newline - 1] == (byte)'\r' ? newline - 1 : newline;
            lines.Add(new Line(start, end, newline + 1));
            start = newline + 1;
        }

        return lines;
    }

    /// <summary>A line: where its text starts and ends, and where the next line starts (after the line end).</summary>
    private readonly record struct Line(int Start, int End, int Next);

    private sealed class Entry(int number, int start, int end)
    {
        public int Number { get; } = number;

        /// <summary>Where the body starts: the first byte after the <c>{</c> line.</summary>
        public int Start { get; } = start;

        /// <summary>Where the body ends: the first byte of the <c>}</c> line.</summary>
        public int End { get; } = end;

        /// <summary>The lines set with <see cref="Set"/>; null while the entry is as read.</summary>
        public string[]? NewLines { get; set; }
    }
}
