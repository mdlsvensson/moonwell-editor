namespace ObjectEditor.App;

/// <summary>The app's entry point.</summary>
public static class Program
{
    /// <summary>Opens the window.</summary>
    [STAThread]
    public static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
