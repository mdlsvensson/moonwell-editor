using System.Text;
using MoonwellEditor.Files;
using MoonwellEditor.Triggers;
using War3Net.Build.Extensions;
using War3Net.Build.Import;
using War3Net.Build.Info;
using War3Net.Build.Widget;

namespace MoonwellEditor.Model;

/// <summary>
/// The map's triggers. They are read when the trigger editor first asks, not when the map opens, so a map opens as fast
/// as before. An edit is one undo step with the object editor's, and a save writes the trigger files with the object
/// files.
/// </summary>
public sealed partial class Session : ITriggerNames
{
    private const string ScriptName = "war3map.j";
    private const string ScriptNameInFolder = @"scripts\war3map.j";

    // The triggers as edited; null until the trigger editor asks, and after a reload.
    private TriggerDocument? _triggers;

    // Why the last Changes left the script as it is; "" when it did not.
    private string _scriptNote = "";

    /// <summary>The map's triggers, read with <paramref name="catalog"/> the first time.</summary>
    public TriggerDocument Triggers(TriggerCatalog catalog)
    {
        if (_triggers == null)
        {
            var triggers = _store.Read(TriggerDocument.TriggersFile);
            var customText = _store.Read(TriggerDocument.CustomTextFile);
            var layout = _store.Read(TriggerDocument.LayoutFile);
            _baseline[TriggerDocument.TriggersFile] = triggers;
            _baseline[TriggerDocument.CustomTextFile] = customText;
            _baseline[TriggerDocument.LayoutFile] = layout;
            _baseline[ImportedFiles.MapFileName] = _store.Read(ImportedFiles.MapFileName);
            _triggers = TriggerDocument.Read(
                triggers,
                customText,
                catalog,
                _store.WriteProblem(TriggerDocument.TriggersFile) ?? _store.WriteProblem(TriggerDocument.CustomTextFile),
                layout);
        }

        return _triggers;
    }

    /// <summary>
    /// Makes an edit of the triggers as one undo step and gives its result. An edit that throws leaves them as they were.
    /// </summary>
    public T EditTriggers<T>(TriggerCatalog catalog, Func<TriggerDocument, T> change)
    {
        var document = Triggers(catalog);
        var before = document.Snapshot();
        T result;
        try
        {
            result = change(document);
        }
        catch (Exception)
        {
            document.Restore(before);
            throw;
        }

        var after = document.Snapshot();
        var edit = new Edit();
        edit.Done(new Step(() => document.Restore(after), () => document.Restore(before)));
        Commit(edit);
        return result;
    }

    /// <inheritdoc />
    public string? ObjectName(string type, string id)
    {
        ObjectKind? kind = type switch
        {
            "unitcode" => ObjectKind.Unit,
            "itemcode" => ObjectKind.Item,
            "destructablecode" => ObjectKind.Destructable,
            "doodadcode" => ObjectKind.Doodad,
            "abilcode" or "heroskillcode" => ObjectKind.Ability,
            "buffcode" => ObjectKind.Buff,
            "techcode" => ObjectKind.Upgrade,
            _ => null,
        };
        if (kind == null)
        {
            return null;
        }

        var map = _kinds[kind.Value];
        var data = _game.Kind(kind.Value);
        var found = map.Find(id);
        var standard = found is { Custom: true } ? data.Object(found.BaseId) : data.Object(id);
        return standard == null && found is not { Custom: true }
            ? null
            : ResolverOf(kind.Value, data).Name(standard, found, id);
    }

    // True when the trigger files differ from the ones read or last saved.
    private bool TriggersChanged() => TriggerFilesChanged() || LayoutChanged();

    private bool TriggerFilesChanged() =>
        _triggers != null
        && (Differs(TriggerDocument.TriggersFile, _triggers.TriggerBytes())
            || Differs(TriggerDocument.CustomTextFile, _triggers.CustomTextBytes()));

    private bool LayoutChanged() =>
        _triggers != null && Differs(TriggerDocument.LayoutFile, _triggers.LayoutBytes());

    private bool Differs(string name, byte[]? bytes) =>
        bytes != null && !SameBytes(bytes, _baseline.GetValueOrDefault(name));

    // The trigger files, added to what a save writes, and the script when they changed. Why a script could not be
    // written is the save's note: the triggers are saved all the same, as World Editor saves a map with script errors.
    private void AddTriggerChanges(List<(string Name, byte[] Bytes)> changes)
    {
        _scriptNote = "";
        if (_triggers == null)
        {
            return;
        }

        var script = TriggerFilesChanged();
        AddChange(changes, TriggerDocument.TriggersFile, _triggers.TriggerBytes());
        AddChange(changes, TriggerDocument.CustomTextFile, _triggers.CustomTextBytes());
        AddChange(changes, TriggerDocument.LayoutFile, _triggers.LayoutBytes());
        if (changes.Exists(change => change.Name == TriggerDocument.LayoutFile))
        {
            AddChange(
                changes,
                ImportedFiles.MapFileName,
                TriggerDocument.ImportsWithLayout(_baseline.GetValueOrDefault(ImportedFiles.MapFileName)));
        }

        if (!script)
        {
            return;
        }

        var info = ReadInfo();
        if (info == null)
        {
            _scriptNote = Texts.Get("app.triggers.noInfo");
        }
        else if (info.ScriptLanguage == ScriptLanguage.Lua)
        {
            _scriptNote = Texts.Get("app.triggers.luaScript");
        }
        else
        {
            AddScript(changes, info);
        }
    }

    private MapInfo? ReadInfo()
    {
        if (_store.Read(MapInfo.FileName) is not { } bytes)
        {
            return null;
        }

        using var reader = new BinaryReader(new MemoryStream(bytes));
        return reader.ReadMapInfo();
    }

    private MapUnits? ReadUnits()
    {
        try
        {
            if (_store.Read(MapUnits.FileName) is { } bytes)
            {
                using var reader = new BinaryReader(new MemoryStream(bytes));
                return reader.ReadMapUnits();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Without them a unit named for the first time cannot be found: the script is then left as it is, and says so.
        }

        return null;
    }

    private void AddScript(List<(string Name, byte[] Bytes)> changes, MapInfo info)
    {
        // An archive may hold its script in a folder; a folder map keeps its files at the top.
        var name = !_store.IsArchive || _store.Read(ScriptName) != null ? ScriptName : ScriptNameInFolder;
        if (_store.Read(name) is not { } bytes)
        {
            _scriptNote = Texts.Get("app.triggers.noScript");
            return;
        }

        string script;
        try
        {
            script = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            _scriptNote = Texts.Get("app.triggers.scriptEncoding");
            return;
        }

        var written = TriggerScript.Write(script, _triggers!, info, ReadUnits(), out var problem);
        if (written == null)
        {
            _scriptNote = problem ?? "";
            return;
        }

        _baseline[name] = bytes;
        AddChange(changes, name, StrictUtf8.GetBytes(written));
    }
}
