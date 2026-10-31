using System.Collections.Immutable;
using MoonwellEditor.Files;
using War3Net.Build.Extensions;
using War3Net.Build.Script;

namespace MoonwellEditor.Triggers;

/// <summary>One line of the trigger tree.</summary>
/// <param name="Id">The item's id; a list of items is in tree order among the items of one parent.</param>
/// <param name="Parent">The id of the category it is in; -1 for the map itself.</param>
/// <param name="Kind">
/// <c>root</c>, <c>category</c>, <c>trigger</c>, <c>custom</c> (a trigger converted to text), <c>script</c> (free code
/// with the map's own), <c>comment</c> or <c>variable</c>.
/// </param>
/// <param name="Name">Its name.</param>
/// <param name="Enabled">False for a trigger that is switched off.</param>
/// <param name="Expanded">True for a category shown open.</param>
/// <param name="Ref">The name a parameter holds for a trigger (<c>gg_trg_Wave_1</c>); empty for the other kinds.</param>
public sealed record TriggerTreeItem(int Id, int Parent, string Kind, string Name, bool Enabled, bool Expanded, string Ref);

/// <summary>
/// A map's triggers: <c>war3map.wtg</c> and <c>war3map.wct</c> as War3Net reads them, with the edits made to them. A
/// file that War3Net cannot read, or does not write back to the same bytes, makes the document read only, with the
/// reason in <see cref="Problem"/>, and it never gives bytes for that file. An edit never changes an item in place: the
/// item is copied and the copy put in its place, so a <see cref="Snapshot"/> taken before stays as it was.
/// </summary>
public sealed partial class TriggerDocument
{
    /// <summary>The triggers' file.</summary>
    public const string TriggersFile = "war3map.wtg";

    /// <summary>The custom text's file.</summary>
    public const string CustomTextFile = "war3map.wct";

    // The id of the tree's root in a file that has none: an older format, shown read only, or a map with no trigger file.
    private const int NoRoot = -2;

    // World Editor ends a custom text with this, and counts it in the text's length. The page never sees it.
    private const char Terminator = '\0';

    private readonly TriggerCatalog _catalog;
    private readonly bool _hadTriggers;
    private readonly bool _hadCustomText;
    private readonly MapTriggers _triggers;
    private readonly MapCustomTextTriggers _customText;

    // The custom text of each trigger that is no comment, by the item's id. Kept as War3Net read it, as an empty one
    // may be null or empty; a trigger with no entry here is written with an empty one.
    private ImmutableDictionary<int, CustomTextTrigger> _code = ImmutableDictionary<int, CustomTextTrigger>.Empty;

    private TriggerDocument(
        TriggerCatalog catalog,
        MapTriggers triggers,
        MapCustomTextTriggers customText,
        bool hadTriggers,
        bool hadCustomText,
        string? problem,
        bool readOnly)
    {
        _catalog = catalog;
        _triggers = triggers;
        _customText = customText;
        _hadTriggers = hadTriggers;
        _hadCustomText = hadCustomText;
        Problem = problem;
        ReadOnly = readOnly;
    }

    /// <summary>Why the triggers cannot be read or edited; null when they can.</summary>
    public string? Problem { get; }

    /// <summary>True when the triggers may not be edited, so no file is ever written from them.</summary>
    public bool ReadOnly { get; }

    /// <summary>The catalog the document was read with.</summary>
    public TriggerCatalog Catalog => _catalog;

    /// <summary>The triggers as War3Net holds them, for the script.</summary>
    internal MapTriggers Triggers => _triggers;

    /// <summary>
    /// Reads a map's two files, either of which may be null when the map has none. It always gives a document: one that
    /// could not be read has <see cref="Problem"/> set and is empty. With <paramref name="writeProblem"/>, why the map
    /// may not write these files, the document is read only.
    /// </summary>
    public static TriggerDocument Read(
        byte[]? triggers, byte[]? customText, TriggerCatalog catalog, string? writeProblem = null)
    {
        var problem = (string?)null;
        var parsedTriggers = Empty();
        var parsedCustom = EmptyCustomText();
        try
        {
            if (triggers != null)
            {
                using var reader = new BinaryReader(new MemoryStream(triggers));
                parsedTriggers = reader.ReadMapTriggers(catalog.Data);
            }

            if (customText != null)
            {
                using var reader = new BinaryReader(new MemoryStream(customText));
                parsedCustom = reader.ReadMapCustomTextTriggers();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            problem = Texts.Get("app.triggers.unreadable", ("reason", exception.Message));
            return new TriggerDocument(
                catalog, Empty(), EmptyCustomText(), triggers != null, customText != null, problem, true);
        }

        var document = new TriggerDocument(
            catalog, parsedTriggers, parsedCustom, triggers != null, customText != null, null, false);
        problem = document.Pair(customText != null) ?? document.Exact(triggers, customText);
        if (problem == null && parsedTriggers.SubVersion == null && triggers != null)
        {
            problem = Texts.Get("app.triggers.oldFormat");
            document.NumberItems();
        }

        problem ??= writeProblem;

        return problem == null ? document : new TriggerDocument(
            catalog, parsedTriggers, parsedCustom, triggers != null, customText != null, problem, true) { _code = document._code };
    }

    /// <summary>The tree: the map, then every item in the file's order.</summary>
    public IReadOnlyList<TriggerTreeItem> Tree()
    {
        var items = new List<TriggerTreeItem>();
        var ids = Live().Select(item => item.Id).ToHashSet();
        var rooted = Live().Any(item => item.Type == TriggerItemType.RootCategory);
        if (!rooted)
        {
            items.Add(new TriggerTreeItem(NoRoot, -1, "root", "", true, true, ""));
        }

        foreach (var item in Live())
        {
            var (kind, enabled, expanded) = item switch
            {
                TriggerCategoryDefinition category => (
                    item.Type == TriggerItemType.RootCategory ? "root" : "category", true, category.IsExpanded),
                TriggerVariableDefinition => ("variable", true, false),
                TriggerDefinition definition => (KindOf(definition), definition.IsEnabled, false),
                _ => ("", false, false),
            };
            var parent = rooted || ids.Contains(item.ParentId) ? item.ParentId : NoRoot;
            var name = kind is "trigger" or "custom" ? ScriptName(item.Name) : "";
            items.Add(new TriggerTreeItem(item.Id, parent, kind, item.Name, enabled, expanded, name));
        }

        return items;
    }

    /// <summary>The custom text of the trigger with this id; null when it has none.</summary>
    public string? CodeOf(int id) => _code.GetValueOrDefault(id)?.Code?.TrimEnd(Terminator);

    /// <summary>The map's own script text, shown at the top of the tree.</summary>
    public string HeaderCode => _customText.GlobalCustomScriptCode?.Code?.TrimEnd(Terminator) ?? "";

    /// <summary>The comment shown with the map's own script text.</summary>
    public string HeaderComment => _customText.GlobalCustomScriptComment ?? "";

    /// <summary>
    /// The bytes of <c>war3map.wtg</c>; null when nothing may be written: the file could not be read, or the map has
    /// none and nothing was added.
    /// </summary>
    public byte[]? TriggerBytes()
    {
        if (!Writable(TriggersFile))
        {
            return null;
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(_triggers);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>The bytes of <c>war3map.wct</c>; null as for <see cref="TriggerBytes"/>.</summary>
    public byte[]? CustomTextBytes()
    {
        if (!Writable(CustomTextFile))
        {
            return null;
        }

        _customText.CustomTextTriggers.Clear();
        foreach (var definition in Definitions())
        {
            _customText.CustomTextTriggers.Add(_code.GetValueOrDefault(definition.Id) ?? new CustomTextTrigger { Code = "" });
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(_customText);
        writer.Flush();
        return stream.ToArray();
    }

    // An older format gives only categories an id: the view needs one on every item, and this document is never written.
    private void NumberItems()
    {
        var number = 0;
        foreach (var definition in _triggers.TriggerItems.OfType<TriggerDefinition>())
        {
            definition.Id = ((int)TriggerItemTypeId.Gui << 24) | number++;
        }
    }

    private static MapTriggers Empty()
    {
        var triggers = new MapTriggers(MapTriggersFormatVersion.v7, MapTriggersSubVersion.v4) { GameVersion = 2 };
        foreach (var type in Enum.GetValues<TriggerItemType>())
        {
            triggers.TriggerItemCounts[type] = 0;
        }

        return triggers;
    }

    private static MapCustomTextTriggers EmptyCustomText() =>
        new(MapCustomTextTriggersFormatVersion.v1, MapCustomTextTriggersSubVersion.v4)
        {
            GlobalCustomScriptComment = "",
            GlobalCustomScriptCode = new CustomTextTrigger { Code = "" },
        };

    private static string KindOf(TriggerDefinition definition) => definition switch
    {
        { Type: TriggerItemType.Comment } => "comment",
        { Type: TriggerItemType.Script } => "script",
        { IsCustomTextTrigger: true } => "custom",
        _ => "trigger",
    };

    // The items that are in the tree.
    private IEnumerable<TriggerItem> Live() => _triggers.TriggerItems.Where(item => item is not DeletedTriggerItem);

    // The triggers with an entry in war3map.wct: every one that is not a comment, in tree order.
    private IEnumerable<TriggerDefinition> Definitions() =>
        _triggers.TriggerItems.OfType<TriggerDefinition>().Where(definition => definition.Type != TriggerItemType.Comment);

    // Pairs the custom text with its triggers. The file holds them by place, which every map of the install keeps.
    private string? Pair(bool hadFile)
    {
        var definitions = Definitions().ToList();
        var entries = _customText.CustomTextTriggers;
        if (hadFile && entries.Count != definitions.Count)
        {
            return Texts.Get("app.triggers.unpaired", ("triggers", definitions.Count), ("texts", entries.Count));
        }

        var pairs = ImmutableDictionary.CreateBuilder<int, CustomTextTrigger>();
        for (var i = 0; i < entries.Count && i < definitions.Count; i++)
        {
            pairs[definitions[i].Id] = entries[i];
        }

        _code = pairs.ToImmutable();
        return null;
    }

    // War3Net's bytes for what it read: a file that does not come back the same is not written.
    private string? Exact(byte[]? triggers, byte[]? customText)
    {
        var same = (triggers == null || TriggerBytes()!.AsSpan().SequenceEqual(triggers))
            && (customText == null || CustomTextBytes()!.AsSpan().SequenceEqual(customText));
        return same ? null : Texts.Get("app.triggers.notExact");
    }

    private bool Writable(string name)
    {
        if (ReadOnly)
        {
            return false;
        }

        var had = name == TriggersFile ? _hadTriggers : _hadCustomText;
        return had || _touched;
    }
}
