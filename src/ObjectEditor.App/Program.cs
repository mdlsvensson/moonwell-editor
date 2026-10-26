namespace ObjectEditor.App;

/// <summary>The app's entry point.</summary>
public static class Program
{
    /// <summary>Opens the window.</summary>
    /// <param name="args">
    /// Optionally a map: a folder (<c>ObjectEditor.App.exe C:\Maps\Arena</c>) or an archive file
    /// (<c>C:\Maps\Arena.w3x</c>), which the page opens once the game data is ready, as if it had been picked with
    /// "Open map…".
    /// </param>
    [STAThread]
    public static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var map = args.Length > 0 && args[0].Length > 0 ? Path.GetFullPath(args[0]) : null;
        Application.Run(new MainForm(map));
    }
}
