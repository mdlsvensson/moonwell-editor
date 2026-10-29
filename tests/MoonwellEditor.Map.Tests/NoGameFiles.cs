using Moonwell.GameData;

namespace MoonwellEditor.Map.Tests;

/// <summary>An install with no files at all: no models, no textures, no tables.</summary>
public sealed class NoGameFiles : IMapGameFiles
{
    /// <inheritdoc />
    public byte[]? File(string path, string mode) => null;

    /// <inheritdoc />
    public IReadOnlyList<string> List(string folder, string mode, string extension) => [];

    /// <inheritdoc />
    public IReadOnlyList<MetaRow>? Table(string relative) => null;
}
