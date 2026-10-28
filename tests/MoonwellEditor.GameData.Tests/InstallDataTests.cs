using System.Text.Json;
using Moonwell.GameData;
using MoonwellEditor.Files;
using Xunit.Abstractions;

namespace MoonwellEditor.GameData.Tests;

/// <summary>The game data is the install's, and only an install's (Sven, 2026-10-28).</summary>
public sealed class InstallDataTests : IClassFixture<GameDataFixture>
{
    private readonly GameDataFixture _fixture;
    private readonly ITestOutputHelper _output;

    public InstallDataTests(GameDataFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void The_install_names_its_folder_and_game_version()
    {
        if (_fixture.Data is not { } data)
        {
            _output.WriteLine(_fixture.Reason);
            return;
        }

        Assert.Equal(_fixture.Folder, data.Source);
        Assert.Equal(GameInstall.Build(_fixture.Folder).Version, data.GameVersion);
        Assert.Contains(data.Kind(ObjectKind.Unit).Objects, unit => unit.Id == "hfoo" && unit.Name == "Footman");
    }

    [Fact]
    public void The_cache_is_kept_for_the_build()
    {
        if (_fixture.Data is not { } data)
        {
            _output.WriteLine(_fixture.Reason);
            return;
        }

        var cacheFile = Assert.Single(Directory.GetFiles(_fixture.Cache, "*.json"));
        using var cached = JsonDocument.Parse(File.ReadAllBytes(cacheFile));
        Assert.Equal("build " + GameInstall.Build(_fixture.Folder).Key, cached.RootElement.GetProperty("Stamp").GetString());
        Assert.Equal(data.GameVersion, cached.RootElement.GetProperty("GameVersion").GetString());
    }

    [Fact]
    public void An_export_is_not_an_install()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MoonwellEditor.GameData.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "units"));
        File.WriteAllText(Path.Combine(folder, "units", "unitdata.slk"), "ID;PWXL;N;E\r\nE\r\n");
        try
        {
            var error = Assert.Throws<DirectoryNotFoundException>(() => GameData.Load(folder, Path.Combine(folder, "cache")));

            Assert.Contains(".build.info", error.Message);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
