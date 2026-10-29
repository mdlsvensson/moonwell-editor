namespace MoonwellEditor.Files.Tests;

/// <summary>The map files in tests/fixtures, copied next to the test assembly.</summary>
internal static class Fixtures
{
    /// <summary>The folder that holds one folder per fixture.</summary>
    public static string Root => Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>The bytes of a fixture file, named "folder/file".</summary>
    public static byte[] Bytes(string file) => File.ReadAllBytes(Path.Combine(Root, file));
}
