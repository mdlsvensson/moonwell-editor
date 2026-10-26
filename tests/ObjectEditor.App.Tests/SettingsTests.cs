using ObjectEditor.App;

namespace ObjectEditor.App.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ObjectEditor.App.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    [Fact]
    public void Settings_round_trip()
    {
        var settings = new Settings(_folder)
        {
            GameDataFolder = @"C:\Games\war3.w3mod",
        };
        settings.RecentMaps.Add(@"C:\Maps\arena.w3x");
        settings.RecentMaps.Add(@"C:\Maps\duel.w3x");
        settings.Save();

        var again = new Settings(_folder);

        Assert.Equal(@"C:\Games\war3.w3mod", again.GameDataFolder);
        Assert.Equal([@"C:\Maps\arena.w3x", @"C:\Maps\duel.w3x"], again.RecentMaps);
    }

    [Fact]
    public void Missing_settings_file_gives_defaults()
    {
        var settings = new Settings(_folder);

        Assert.Null(settings.GameDataFolder);
        Assert.Empty(settings.RecentMaps);
    }

    [Fact]
    public void Corrupt_settings_file_gives_defaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{ this is not json");

        var settings = new Settings(_folder);

        Assert.Null(settings.GameDataFolder);
        Assert.Empty(settings.RecentMaps);
    }

    [Fact]
    public void Save_creates_the_folder_and_writes_camel_case_json()
    {
        var settings = new Settings(Path.Combine(_folder, "deeper")) { GameDataFolder = "x" };

        settings.Save();

        var text = File.ReadAllText(Path.Combine(_folder, "deeper", "settings.json"));
        Assert.Contains("\"gameDataFolder\"", text);
    }
}
