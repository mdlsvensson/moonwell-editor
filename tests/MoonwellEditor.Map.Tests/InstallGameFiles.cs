using Moonwell.GameData;

namespace MoonwellEditor.Map.Tests;

/// <summary>The install's files and tables for the map view's tests, one reader at a time; null without an install.</summary>
public sealed class InstallGameFiles : IMapGameFiles
{
    private static readonly Lazy<InstallGameFiles?> Shared = new(
        () => InstallMaps.Game is { } files ? new InstallGameFiles(files) : null);

    private readonly GameFiles _files;
    private readonly Lazy<GameTables> _tables;

    private InstallGameFiles(GameFiles files)
    {
        _files = files;
        _tables = new Lazy<GameTables>(() => GameTables.OpenInstall(files));
    }

    /// <summary>The install's files, or null without an install.</summary>
    public static InstallGameFiles? Open() => Shared.Value;

    /// <inheritdoc />
    public byte[]? File(string path, string mode)
    {
        lock (InstallMaps.Reading)
        {
            return _files.Read(path, mode);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> List(string folder, string mode, string extension)
    {
        lock (InstallMaps.Reading)
        {
            return _files.List(folder, mode, extension);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<MetaRow>? Table(string relative)
    {
        lock (InstallMaps.Reading)
        {
            return _tables.Value.Table(relative);
        }
    }
}
