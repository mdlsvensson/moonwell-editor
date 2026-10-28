using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moonwell.GameData;
using MoonwellEditor.Files;
using Xunit.Abstractions;

namespace MoonwellEditor.GameData.Tests;

/// <summary>
/// The game data of the Warcraft III install on this computer (MOONWELL_WAR3, else the usual folder), loaded once into
/// a fresh cache folder, for every test of <see cref="InstallDataTests"/>.
/// </summary>
public sealed class InstallFixture : IDisposable
{
    public InstallFixture()
    {
        var folder = GameInstall.Find();
        if (folder == null)
        {
            Reason = $"No Warcraft III install in {string.Join(", ", GameInstall.Places())}; set {GameInstall.Variable} to run this test";
            return;
        }

        Files = GameFiles.Open(folder);
        Cache = Path.Combine(Path.GetTempPath(), "MoonwellEditor.GameData.Tests", Guid.NewGuid().ToString("N"));
        var clock = Stopwatch.StartNew();
        Data = GameData.Load(Files, Cache);
        FirstLoad = clock.Elapsed;
    }

    /// <summary>Why the tests cannot run, or null when <see cref="Data"/> is loaded.</summary>
    public string? Reason { get; }

    /// <summary>The install's files, open for the class.</summary>
    public GameFiles? Files { get; }

    /// <summary>The cache folder of this run, empty without an install.</summary>
    public string Cache { get; } = "";

    /// <summary>The data of the first load, or null when the tests cannot run.</summary>
    public GameData? Data { get; }

    /// <summary>How long the first load (the build) took.</summary>
    public TimeSpan FirstLoad { get; }

    public void Dispose()
    {
        Files?.Dispose();
        if (Cache.Length > 0 && Directory.Exists(Cache))
        {
            Directory.Delete(Cache, true);
        }
    }
}

public sealed class InstallDataTests : IClassFixture<InstallFixture>
{
    private readonly InstallFixture _fixture;
    private readonly ITestOutputHelper _output;

    public InstallDataTests(InstallFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void The_install_names_its_folder_and_game_version()
    {
        if (Skipped(out var data, out var files))
        {
            return;
        }

        Assert.Equal(files.Storage.Folder, data.Source);
        Assert.Equal(files.Build.Version, data.GameVersion);
        Assert.Contains(data.Kind(ObjectKind.Unit).Objects, unit => unit.Id == "hfoo" && unit.Name == "Footman");
        Assert.True(GameData.IsInstall(files.Storage.Folder));
    }

    [Fact]
    public void The_install_gives_the_data_an_export_does()
    {
        if (Skipped(out var data, out _))
        {
            return;
        }

        var export = Environment.GetEnvironmentVariable(GameDataFixture.Variable);
        if (string.IsNullOrEmpty(export) || !Directory.Exists(export))
        {
            _output.WriteLine($"{GameDataFixture.Variable} names no export to compare with; skipped");
            return;
        }

        var cache = Path.Combine(_fixture.Cache, "export");
        Assert.Equal(GameDataTests.Snapshot(GameData.Load(export, cache), false), GameDataTests.Snapshot(data, false));
    }

    [Fact]
    public void The_cache_is_kept_for_the_build_and_used_the_second_time()
    {
        if (Skipped(out var data, out var files))
        {
            return;
        }

        var cacheFile = Assert.Single(Directory.GetFiles(_fixture.Cache, "*.json"));
        using (var cached = JsonDocument.Parse(File.ReadAllBytes(cacheFile)))
        {
            Assert.Equal("build " + files.Build.Key, cached.RootElement.GetProperty("Stamp").GetString());
            Assert.Equal(files.Build.Version, cached.RootElement.GetProperty("GameVersion").GetString());
        }

        var clock = Stopwatch.StartNew();
        var again = GameData.Load(files, _fixture.Cache);
        _output.WriteLine($"first load {_fixture.FirstLoad.TotalMilliseconds:F0} ms, cached load {clock.Elapsed.TotalMilliseconds:F0} ms");
        Assert.True(clock.Elapsed.Ticks * 5 <= _fixture.FirstLoad.Ticks, $"first {_fixture.FirstLoad}, second {clock.Elapsed}");
        Assert.Equal(GameDataTests.Snapshot(data), GameDataTests.Snapshot(again));
        Assert.Equal(data.GameVersion, again.GameVersion);
    }

    [Fact]
    public void A_cache_of_another_build_is_rebuilt()
    {
        if (Skipped(out _, out var files))
        {
            return;
        }

        // The copy of the cache gives the footman 999 hit points: a load that uses it says 999, a build says 420.
        Assert.Equal("999", FootmanHitPoints(files, _ => { }));
        Assert.Equal("420", FootmanHitPoints(files, cache => cache["Stamp"] = "build 00000000000000000000000000000000"));
    }

    [Fact]
    public void A_folder_with_neither_an_install_nor_an_export_is_an_error()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MoonwellEditor.GameData.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
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

    // The footman's hit points after a load from a copy of the fixture's cache with them set to 999 and the change made.
    private string FootmanHitPoints(GameFiles files, Action<JsonObject> change)
    {
        var folder = Path.Combine(_fixture.Cache, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Directory.GetFiles(_fixture.Cache, "*.json").Single();
        var cache = JsonNode.Parse(File.ReadAllText(source))!.AsObject();
        var footman = cache["Kinds"]!["unit"]!["Objects"]!.AsArray().Single(unit => (string?)unit!["Id"] == "hfoo")!;
        footman["Values"]!["uhpm"] = "999";
        change(cache);
        File.WriteAllText(Path.Combine(folder, Path.GetFileName(source)), cache.ToJsonString());
        return GameData.Load(files, folder).Kind(ObjectKind.Unit).Object("hfoo")!.Values["uhpm"];
    }

    private bool Skipped(out GameData data, out GameFiles files)
    {
        if (_fixture.Data == null || _fixture.Files == null)
        {
            _output.WriteLine(_fixture.Reason);
            data = null!;
            files = null!;
            return true;
        }

        data = _fixture.Data;
        files = _fixture.Files;
        return false;
    }
}
