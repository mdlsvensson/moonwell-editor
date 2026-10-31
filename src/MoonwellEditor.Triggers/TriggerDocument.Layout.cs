using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using War3Net.Build.Extensions;
using War3Net.Build.Import;
using War3Net.Build.Script;

namespace MoonwellEditor.Triggers;

/// <summary>Where a card sits on its category's grid.</summary>
/// <param name="X">Its left edge, in the grid's units.</param>
/// <param name="Y">Its top edge.</param>
/// <param name="Folded">True when it shows only its title bar.</param>
public sealed record CardPlace(int X, int Y, bool Folded);

/// <summary>A card put at a place.</summary>
/// <param name="Id">The item's id.</param>
/// <param name="X">Its left edge.</param>
/// <param name="Y">Its top edge.</param>
public sealed record CardMove(int Id, int X, int Y);

/// <summary>One card of a category's grid.</summary>
/// <param name="Id">The item's id.</param>
/// <param name="Kind">As in <see cref="TriggerTreeItem.Kind"/>.</param>
/// <param name="Name">Its name.</param>
/// <param name="Place">Where it was put; null for a card never moved, which the page places.</param>
/// <param name="Trigger">A trigger, script or comment; null for the other kinds.</param>
/// <param name="Variable">A variable; null for the other kinds.</param>
/// <param name="Count">How many items a category holds, at any depth; 0 for the other kinds.</param>
public sealed record CardView(
    int Id,
    string Kind,
    string Name,
    CardPlace? Place,
    TriggerView? Trigger,
    VariableView? Variable,
    int Count);

/// <summary>
/// The grid of a category. The map's trigger file holds no places, so a moved card's is kept in a file of its own in
/// the map, listed in its imports. Nothing else depends on that file: without it the cards are laid out anew.
/// </summary>
public sealed partial class TriggerDocument
{
    /// <summary>The file in the map that holds the cards' places.</summary>
    public const string LayoutFile = "war3map.triggerlayout.json";

    // An import at its own path in the archive, not under war3mapImported: 13, as official maps have at the archive's top.
    private const ImportedFileFlags OwnPath = ImportedFileFlags.UNK1 | ImportedFileFlags.UNK4 | ImportedFileFlags.UNK8;

    private ImmutableDictionary<int, CardPlace> _places = ImmutableDictionary<int, CardPlace>.Empty;

    // True once a place was changed, so the file is written.
    private bool _layoutTouched;

    /// <summary>A card for everything directly in the category <paramref name="parent"/>, in the tree's order.</summary>
    public IReadOnlyList<CardView> Cards(int parent, ITriggerNames names)
    {
        var tree = Tree();
        var cards = new List<CardView>();
        foreach (var item in tree.Where(each => each.Parent == parent && each.Kind != "root"))
        {
            cards.Add(new CardView(
                item.Id,
                item.Kind,
                item.Name,
                _places.GetValueOrDefault(item.Id),
                item.Kind is "trigger" or "custom" or "script" or "comment" ? Trigger(item.Id, names) : null,
                item.Kind == "variable" ? Variables().FirstOrDefault(variable => variable.Id == item.Id) : null,
                item.Kind == "category" ? CountIn(tree, item.Id) : 0));
        }

        return cards;
    }

    /// <summary>Puts cards at places.</summary>
    public void SetPlaces(IReadOnlyList<CardMove> moves)
    {
        Editable();
        foreach (var move in moves)
        {
            _ = Item(move.Id);
            var folded = _places.GetValueOrDefault(move.Id)?.Folded ?? false;
            _places = _places.SetItem(move.Id, new CardPlace(move.X, move.Y, folded));
        }

        _layoutTouched = true;
    }

    /// <summary>Folds a card to its title bar, or unfolds it.</summary>
    public void SetFolded(int id, bool folded)
    {
        Editable();
        _ = Item(id);
        var place = _places.GetValueOrDefault(id) ?? new CardPlace(0, 0, false);
        _places = _places.SetItem(id, place with { Folded = folded });
        _layoutTouched = true;
    }

    /// <summary>The bytes of the layout file; null when no place was changed, or nothing may be written.</summary>
    public byte[]? LayoutBytes()
    {
        if (ReadOnly || !_layoutTouched)
        {
            return null;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteStartObject("cards");
            foreach (var (id, place) in _places.OrderBy(pair => pair.Key))
            {
                writer.WriteStartObject(id.ToString(CultureInfo.InvariantCulture));
                writer.WriteNumber("x", place.X);
                writer.WriteNumber("y", place.Y);
                writer.WriteBoolean("folded", place.Folded);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// The bytes of <c>war3map.imp</c> with the layout file named in it, as World Editor's save drops a file no import
    /// names. Null when it is named already, or when <paramref name="imports"/> cannot be read and written back to the
    /// same bytes: the map works without the places.
    /// </summary>
    public static byte[]? ImportsWithLayout(byte[]? imports)
    {
        var files = imports == null ? new ImportedFiles(ImportedFilesFormatVersion.v1) : ReadImports(imports);
        if (files == null || files.Files.Exists(file => file.FullPath.Equals(LayoutFile, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        files.Files.Add(new ImportedFile { Flags = OwnPath, FullPath = LayoutFile });
        return ImportBytes(files);
    }

    // Null for bytes that are not imports, or that would not come back the same.
    private static ImportedFiles? ReadImports(byte[] bytes)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            var files = reader.ReadImportedFiles();
            return ImportBytes(files).AsSpan().SequenceEqual(bytes) ? files : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static byte[] ImportBytes(ImportedFiles files)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(files);
        }

        return stream.ToArray();
    }

    private static int CountIn(IReadOnlyList<TriggerTreeItem> tree, int parent) =>
        tree.Where(item => item.Parent == parent).Sum(item => 1 + (item.Kind == "category" ? CountIn(tree, item.Id) : 0));

    // A file that cannot be read means no places, as the map works without them.
    private static ImmutableDictionary<int, CardPlace> ReadPlaces(byte[]? bytes)
    {
        var places = ImmutableDictionary<int, CardPlace>.Empty;
        if (bytes == null)
        {
            return places;
        }

        try
        {
            using var json = JsonDocument.Parse(bytes);
            foreach (var card in json.RootElement.GetProperty("cards").EnumerateObject())
            {
                if (int.TryParse(card.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    places = places.SetItem(id, new CardPlace(
                        card.Value.GetProperty("x").GetInt32(),
                        card.Value.GetProperty("y").GetInt32(),
                        card.Value.GetProperty("folded").GetBoolean()));
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return ImmutableDictionary<int, CardPlace>.Empty;
        }

        return places;
    }
}
