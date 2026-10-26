using MoonwellEditor.Files;

namespace MoonwellEditor.Files.Tests;

/// <summary>The map files in tests/fixtures, copied next to the test assembly.</summary>
internal static class Fixtures
{
    /// <summary>The folder that holds one folder per fixture.</summary>
    public static string Root => Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>The fixture folders: World Editor 3.0's two saves and Moonwell's MyMap build.</summary>
    public static readonly string[] Folders = ["we-3.0", "we-heavy", "moonwell-mymap"];

    /// <summary>Every object file of every fixture, as "folder/file".</summary>
    public static IEnumerable<string> ObjectFiles()
    {
        foreach (var folder in Folders)
        {
            var files = Directory.GetFiles(Path.Combine(Root, folder))
                .Where(path => KindOf(path) is not null)
                .Select(path => folder + "/" + Path.GetFileName(path))
                .Order(StringComparer.Ordinal);
            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    /// <summary>Every object file of every fixture, as theory data.</summary>
    public static TheoryData<string> ObjectFileData()
    {
        var data = new TheoryData<string>();
        foreach (var file in ObjectFiles())
        {
            data.Add(file);
        }

        return data;
    }

    /// <summary>The bytes of a fixture file, named "folder/file".</summary>
    public static byte[] Bytes(string file) => File.ReadAllBytes(Path.Combine(Root, file));

    /// <summary>The object kind of a file, from its extension; null for other files.</summary>
    public static ObjectKind? KindOf(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.');
        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            if (ObjectKinds.Extension(kind) == extension)
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>Reads a fixture file with <see cref="ObjectFile.Read"/>.</summary>
    public static ObjectFile Read(string file) => ObjectFile.Read(KindOf(file)!.Value, Bytes(file));
}
