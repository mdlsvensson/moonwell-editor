using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ObjectEditor.App;

/// <summary>The app's one window: a WebView2 that shows the Svelte front end and talks to the <see cref="Bridge"/>.</summary>
public sealed class MainForm : Form
{
    private const string Host = "app.objecteditor";

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(0x12, 0x12, 0x12) };
    private readonly Commands _commands;
    private readonly Bridge _bridge;

    // The page's requests are answered one at a time, in the order they came.
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <summary>Makes the window. The page loads once the window is shown.</summary>
    public MainForm()
    {
        Text = "Object Editor";
        ClientSize = new Size(1400, 860);
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(0x12, 0x12, 0x12);
        Controls.Add(_web);

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ObjectEditor");
        var cache = Path.Combine(appData, "gamedata");
        _commands = new Commands(new Settings(appData), AskForFolder, folder => GameData.GameData.Load(folder, cache));
        _bridge = new Bridge(_commands.Run);
        _commands.GameDataChanged += state => Notify("gameData", state);

        Load += async (_, _) =>
        {
            _ = _commands.StartLoading();
            await StartPage();
        };
    }

    /// <summary>Sends a message (for example from <see cref="Bridge.Event"/>) to the page. Call it from the UI thread.</summary>
    /// <param name="message">The message, as JSON text.</param>
    public void Post(string message) => _web.CoreWebView2.PostWebMessageAsString(message);

    // Commands run on a background task, but a dialog must open on the window's own thread.
    private string? AskForFolder() =>
        Invoke(() =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose the game data folder (a war3.w3mod export)",
                UseDescriptionForTitle = true,
            };
            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
        });

    // Tells the page about something that happened on a background thread.
    private void Notify(string name, object payload)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (_web.CoreWebView2 != null)
            {
                Post(Bridge.Event(name, payload));
            }
        });
    }

    private async Task StartPage()
    {
        var dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ObjectEditor", "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
        await _web.EnsureCoreWebView2Async(environment);

        var core = _web.CoreWebView2;
        core.WebMessageReceived += (_, e) => _ = Answer(e.TryGetWebMessageAsString());

        var devUrl = Environment.GetEnvironmentVariable("OBJECT_EDITOR_DEV_URL");
#if DEBUG
        if (!string.IsNullOrEmpty(devUrl))
        {
            core.Navigate(devUrl);
            return;
        }
#endif
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        core.SetVirtualHostNameToFolderMapping(Host, wwwroot, CoreWebView2HostResourceAccessKind.Allow);
        core.Navigate($"https://{Host}/index.html");
    }

    private async Task Answer(string request)
    {
        await _oneAtATime.WaitAsync();
        try
        {
            var reply = await Task.Run(() => _bridge.Handle(request));
            if (!IsDisposed)
            {
                BeginInvoke(() => Post(reply));
            }
        }
        finally
        {
            _oneAtATime.Release();
        }
    }
}
