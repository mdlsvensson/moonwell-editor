namespace MoonwellEditor.Files.Tests;

public sealed class MapFolderTests
{
    [Fact]
    public void Map_folder_reads_writes_deletes_and_stamps()
    {
        var path = Path.Combine(Path.GetTempPath(), "MoonwellEditor.Files.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var stamp = new DateTime(2001, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            foreach (var file in Directory.GetFiles(Path.Combine(Fixtures.Root, "we-3.0")))
            {
                var copy = Path.Combine(path, Path.GetFileName(file));
                File.Copy(file, copy);
                File.SetLastWriteTimeUtc(copy, stamp.AddHours(-1));
            }

            File.SetLastWriteTimeUtc(Path.Combine(path, "war3map.wts"), stamp);
            File.WriteAllText(Path.Combine(path, "notes.txt"), "not a map file");
            var map = MapFolder.Open(path);

            Assert.Equal(path, map.Path);
            Assert.Equal(Fixtures.Bytes("we-3.0/war3map.w3u"), map.Read("war3map.w3u"));
            Assert.Null(map.Read("war3map.w3o"));
            Assert.False(map.IsArchive);
            var before = map.Stamp();
            Assert.StartsWith(stamp.Ticks + ":", before);
            Assert.Equal(before, map.Stamp());

            map.Write([("war3map.w3u", [1, 2, 3]), ("war3mapSkin.w3u", null), ("war3map.w3o", null)]);

            Assert.Equal([1, 2, 3], map.Read("war3map.w3u"));
            Assert.Null(map.Read("war3mapSkin.w3u"));
            Assert.False(File.Exists(Path.Combine(path, "war3mapSkin.w3u")));
            Assert.NotEqual(before, map.Stamp());
            Assert.Throws<ArgumentException>(() => map.Read("..\\war3map.w3u"));
            Assert.Throws<DirectoryNotFoundException>(() => MapFolder.Open(Path.Combine(path, "missing")));
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }
}
