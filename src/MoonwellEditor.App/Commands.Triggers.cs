using System.Text.Json;
using MoonwellEditor.Files;
using MoonwellEditor.Map;
using MoonwellEditor.Triggers;
using War3Net.Build.Audio;
using War3Net.Build.Extensions;

namespace MoonwellEditor.App;

/// <summary>The game's trigger functions, for the page's lists.</summary>
/// <param name="Categories">The function categories.</param>
/// <param name="Types">The value types.</param>
/// <param name="Presets">The named values.</param>
/// <param name="Functions">Every function.</param>
public sealed record TriggerCatalogView(
    IReadOnlyList<CatalogCategory> Categories,
    IReadOnlyList<CatalogType> Types,
    IReadOnlyList<CatalogPreset> Presets,
    IReadOnlyList<CatalogFunction> Functions);

/// <summary>A thing of the map that a trigger names: a region, a camera or a sound.</summary>
/// <param name="Type">The value type it fills (<c>rect</c>).</param>
/// <param name="Ref">The name a parameter holds (<c>gg_rct_Spawn</c>).</param>
/// <param name="Text">The name as shown (<c>Spawn &lt;gen&gt;</c>).</param>
public sealed record MapReference(string Type, string Ref, string Text);

/// <summary>The trigger editor's tree.</summary>
/// <param name="Items">The items.</param>
/// <param name="Variables">The variables.</param>
/// <param name="ReadOnly">True when nothing may be edited.</param>
/// <param name="Problem">Why it is read only; null when it is not, or for no reason the user need see.</param>
/// <param name="HeaderComment">The comment on the map's own script text.</param>
/// <param name="HeaderCode">The map's own script text.</param>
/// <param name="References">The regions, cameras and sounds the map has.</param>
public sealed record TriggerTreeView(
    IReadOnlyList<TriggerTreeItem> Items,
    IReadOnlyList<VariableView> Variables,
    bool ReadOnly,
    string? Problem,
    string HeaderComment,
    string HeaderCode,
    IReadOnlyList<MapReference> References);

/// <summary>A new item's id.</summary>
/// <param name="Id">The id.</param>
public sealed record NewTriggerItem(int Id);

/// <summary>A function's path after an edit.</summary>
/// <param name="Path">The path.</param>
public sealed record FunctionPlace(string Path);

/// <summary>The trigger editor's commands.</summary>
public sealed partial class Commands
{
    // The catalog of the install it was read from; another install reads it again.
    private (string Folder, TriggerCatalog Catalog)? _triggerCatalog;

    private TriggerCatalog Catalog()
    {
        var assets = Assets ?? throw new CommandException("noAssets", Texts.Get("app.assets.none"));
        if (_triggerCatalog is { } known && known.Folder == assets.Folder)
        {
            return known.Catalog;
        }

        var catalog = TriggerCatalog.Read(path => assets.GameFile(path, "sd"));
        _triggerCatalog = (assets.Folder, catalog);
        return catalog;
    }

    private TriggerCatalogView CatalogView()
    {
        var catalog = Catalog();
        return new TriggerCatalogView(catalog.Categories, catalog.Types, catalog.Presets, catalog.Functions);
    }

    private TriggerTreeView TriggerTree()
    {
        var document = Map().Triggers(Catalog());
        return new TriggerTreeView(
            document.Tree(),
            document.Variables(),
            document.ReadOnly,
            document.Problem,
            document.HeaderComment,
            document.HeaderCode,
            References());
    }

    // The regions, cameras and sounds of the map; a file that cannot be read adds nothing.
    private List<MapReference> References()
    {
        var store = _store!;
        var files = MapReader.Read(store);
        var references = new List<MapReference>();
        foreach (var region in files.Regions?.Regions ?? [])
        {
            references.Add(new MapReference("rect", TriggerNames.Region(region.Name), region.Name + " <gen>"));
        }

        foreach (var camera in files.Cameras?.Cameras ?? [])
        {
            references.Add(new MapReference("camerasetup", TriggerNames.Camera(camera.Name), camera.Name + " <gen>"));
        }

        try
        {
            if (store.Read(MapSounds.FileName) is { } bytes)
            {
                using var reader = new BinaryReader(new MemoryStream(bytes));
                foreach (var sound in reader.ReadMapSounds().Sounds)
                {
                    references.Add(new MapReference("sound", sound.Name, sound.Name.Replace("gg_snd_", "", StringComparison.Ordinal) + " <gen>"));
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Sounds are only offered in a list; the rest of the editor does not need them.
        }

        return references;
    }

    private TriggerView TriggerOf(JsonElement args) => Map().Triggers(Catalog()).Trigger(Number(args, "id"), Map());

    private T EditTriggers<T>(Func<TriggerDocument, T> change) => Changed(Map().EditTriggers(Catalog(), change));

    private object? EditTriggers(Action<TriggerDocument> change) => EditTriggers<object?>(document =>
    {
        change(document);
        return null;
    });

    private object? RunTrigger(string command, JsonElement args) => command switch
    {
        "triggerCatalog" => CatalogView(),
        "triggerTree" => TriggerTree(),
        "trigger" => TriggerOf(args),
        "triggerAdd" => EditTriggers(document =>
            new NewTriggerItem(document.Add(Text(args, "kind"), Number(args, "parent")))),
        "triggerRename" => EditTriggers(document => document.Rename(Number(args, "id"), Text(args, "name"))),
        "triggerOptions" => EditTriggers(document => document.SetOptions(
            Number(args, "id"),
            OptionalFlag(args, "enabled"),
            OptionalFlag(args, "initiallyOn"),
            OptionalFlag(args, "runOnInit"),
            OptionalText(args, "description"))),
        "triggerCode" => EditTriggers(document => document.SetCode(Number(args, "id"), Text(args, "code"))),
        "triggerHeader" => EditTriggers(document => document.SetHeader(Text(args, "comment"), Text(args, "code"))),
        "triggerDelete" => EditTriggers(document => document.Delete(Number(args, "id"))),
        "triggerMove" => EditTriggers(document =>
            document.Move(Number(args, "id"), Number(args, "parent"), Number(args, "index"))),
        "triggerVariableAdd" => EditTriggers(document =>
            new NewTriggerItem(document.AddVariable(VariableOf(args), Number(args, "parent")))),
        "triggerVariableSet" => EditTriggers(document => document.SetVariable(Number(args, "id"), VariableOf(args))),
        "triggerFunctionAdd" => EditTriggers(document => new FunctionPlace(document.AddFunction(
            Number(args, "id"),
            OptionalText(args, "parent"),
            Number(args, "branch"),
            Number(args, "index"),
            Text(args, "kind"),
            Text(args, "name")))),
        "triggerFunctionCopy" => EditTriggers(document => new FunctionPlace(document.CopyFunction(
            Number(args, "from"),
            Text(args, "path"),
            Number(args, "id"),
            OptionalText(args, "parent"),
            Number(args, "branch"),
            Number(args, "index")))),
        "triggerFunctionChange" => EditTriggers(document =>
            document.ChangeFunction(Number(args, "id"), Text(args, "path"), Text(args, "name"))),
        "triggerFunctionDelete" => EditTriggers(document => document.DeleteFunction(Number(args, "id"), Text(args, "path"))),
        "triggerFunctionEnable" => EditTriggers(document =>
            document.EnableFunction(Number(args, "id"), Text(args, "path"), Flag(args, "enabled"))),
        "triggerFunctionMove" => EditTriggers(document => new FunctionPlace(
            document.MoveFunction(Number(args, "id"), Text(args, "path"), Number(args, "index")))),
        "triggerParameter" => EditTriggers(document => document.SetParameter(
            Number(args, "id"), Text(args, "path"), new ParamInput(Text(args, "kind"), Text(args, "value")))),
        _ => throw new UnknownCommandException(command),
    };

    private static VariableInput VariableOf(JsonElement args) => new(
        Text(args, "name"),
        Text(args, "type"),
        Flag(args, "isArray"),
        Number(args, "size"),
        OptionalText(args, "initial") ?? "");

    // A yes/no argument; null when it is missing or null.
    private static bool? OptionalFlag(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object
        && args.TryGetProperty(name, out var value)
        && value.ValueKind != JsonValueKind.Null
            ? Flag(args, name)
            : null;
}
