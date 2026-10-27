namespace MoonwellEditor.Files.Tests;

public sealed class ReadImportTests
{
    [Fact]
    public void A_folder_map_reads_an_import_in_a_folder_by_either_slash_and_nothing_outside_it()
    {
        var root = Directory.CreateTempSubdirectory("moonwell-editor-imports").FullName;
        try
        {
            var map = Path.Combine(root, "Map");
            Directory.CreateDirectory(Path.Combine(map, "war3mapImported"));
            File.WriteAllBytes(Path.Combine(map, "war3mapImported", "BTNSword.blp"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(root, "secret.txt"), [9]);
            var store = MapFolder.Open(map);

            Assert.Equal(new byte[] { 1, 2, 3 }, store.ReadImport(@"war3mapImported\BTNSword.blp"));
            Assert.Equal(new byte[] { 1, 2, 3 }, store.ReadImport("war3mapImported/BTNSword.blp"));
            Assert.Null(store.ReadImport(@"war3mapImported\Missing.blp"));
            Assert.Null(store.ReadImport(@"..\secret.txt"));
            Assert.Null(store.ReadImport(""));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
