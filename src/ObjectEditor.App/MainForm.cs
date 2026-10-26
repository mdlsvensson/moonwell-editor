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

    // The origins (https://app.objecteditor, and the dev server's in a Debug build) whose pages may use the bridge
    // and be navigated to; set when the page starts.
    private readonly HashSet<string> _origins = new(StringComparer.OrdinalIgnoreCase);

    // Closing with unsaved edits asks the page first; a page that does not answer within the timer's wait is skipped.
    private readonly CloseGuard _closeGuard = new();
    private readonly System.Windows.Forms.Timer _pageWait = new() { Interval = 3000 };

    /// <summary>Makes the window. The page loads once the window is shown.</summary>
    /// <param name="startupMap">A map folder for the page to open once the game data is ready; null for none.</param>
    public MainForm(string? startupMap = null)
    {
        Text = "Object Editor";
        ClientSize = new Size(1400, 860);
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(0x12, 0x12, 0x12);
        Controls.Add(_web);

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ObjectEditor");
        var cache = Path.Combine(appData, "gamedata");
        _commands = new Commands(
            new Settings(appData),
            AskForFolder,
            folder => GameData.GameData.Load(folder, cache),
            startupMap);
        _bridge = new Bridge(_commands.Run);
        _commands.GameDataChanged += state => Notify("gameData", state);
        _commands.SessionChanged += state => Notify("sessionChanged", state);
        _commands.CloseAsking += () => OnWindowThread(_closeGuard.PageAsking);
        _commands.CloseApproved += () => OnWindowThread(() =>
        {
            _closeGuard.Approve();
            Close();
        });
        _pageWait.Tick += (_, _) => AskIfPageSilent();
        FormClosing += OnClosing;

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
    private string? AskForFolder(string description) =>
        Invoke(() =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = description,
                UseDescriptionForTitle = true,
            };
            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
        });

    // With unsaved edits, the window stays open and asks the page, which asks the user to save, discard or cancel;
    // the page closes the window with the closeWindow command. A shutdown of Windows is held up the same way (Windows
    // then shows that the app is keeping it from shutting down); the Task Manager always closes.
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason is not (CloseReason.UserClosing or CloseReason.WindowsShutDown))
        {
            return;
        }

        if (_closeGuard.Closing(_commands.Dirty) == CloseStep.Close)
        {
            _pageWait.Stop();
            return;
        }

        e.Cancel = true;
        if (_web.CoreWebView2 == null)
        {
            AskIfPageSilent();
            return;
        }

        Post(Bridge.Event("closeRequested", new { }));
        _pageWait.Stop();
        _pageWait.Start();
    }

    // The page did not say it is asking the user (it failed to load, or is broken): the window asks instead.
    private void AskIfPageSilent()
    {
        _pageWait.Stop();
        if (!_closeGuard.PageSilent())
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            "The map has unsaved changes, and the page did not answer. Close the window and lose them?",
            "Object Editor",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes)
        {
            _closeGuard.Approve();
            Close();
        }
    }

    // Runs an action on the window's thread, from a command's background thread.
    private void OnWindowThread(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(action);
    }

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

        // Only the app's own pages may use the bridge, and the window never leaves them (a link to the web is ignored).
        _origins.Add(Origin($"https://{Host}/"));
        core.WebMessageReceived += (_, e) =>
        {
            if (_origins.Contains(Origin(e.Source)))
            {
                _ = Answer(e.TryGetWebMessageAsString());
            }
        };
        core.NavigationStarting += (_, e) =>
        {
            if (!_origins.Contains(Origin(e.Uri)))
            {
                e.Cancel = true;
            }
        };

#if DEBUG
        var devUrl = Environment.GetEnvironmentVariable("OBJECT_EDITOR_DEV_URL");
        if (!string.IsNullOrEmpty(devUrl))
        {
            _origins.Add(Origin(devUrl));
            core.Navigate(devUrl);
            return;
        }
#endif
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        core.SetVirtualHostNameToFolderMapping(Host, wwwroot, CoreWebView2HostResourceAccessKind.Allow);
        core.Navigate($"https://{Host}/index.html");
    }

    // A URL's scheme, host and port (https://app.objecteditor); "" for a URL that cannot be read, which matches none.
    private static string Origin(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : "";

    private async Task Answer(string request)
    {
        await _oneAtATime.WaitAsync();
        try
        {
            var reply = await Task.Run(() => _bridge.Handle(request));
            OnWindowThread(() =>
            {
                // The closeWindow command's reply comes after the window started closing.
                if (!IsDisposed && _web.CoreWebView2 != null)
                {
                    Post(reply);
                }
            });
        }
        finally
        {
            _oneAtATime.Release();
        }
    }
}
