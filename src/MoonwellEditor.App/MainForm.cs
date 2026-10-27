using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MoonwellEditor.App;

/// <summary>The app's one window: a WebView2 that shows the Svelte front end and talks to the <see cref="Bridge"/>.</summary>
public sealed class MainForm : Form
{
    private const string Host = "app.moonwelleditor";

    // Made again when the browser process ends (see OnProcessFailed).
    private WebView2 _web = NewWeb();
    private readonly Commands _commands;
    private readonly Bridge _bridge;

    // The page's requests are answered one at a time, in the order they came.
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    // The origins (https://app.moonwelleditor, and the dev server's in a Debug build) whose pages may use the bridge,
    // be navigated to and read the clipboard; set when the page starts.
    private readonly PageOrigins _origins = new();

    // Closing asks the page first; a page that does not answer within the timer's wait is skipped.
    private readonly CloseGuard _closeGuard = new();
    private readonly System.Windows.Forms.Timer _pageWait = new() { Interval = 3000 };

    // True once the page has loaded (it may then hold a typed value), until it navigates again or its process fails.
    private bool _pageLoaded;

    /// <summary>Makes the window. The page loads once the window is shown.</summary>
    /// <param name="startupMap">
    /// A map (a folder or an archive file) for the page to open once the game data is ready; null for none.
    /// </param>
    public MainForm(string? startupMap = null)
    {
        Text = "Moonwell Editor";
        ClientSize = new Size(1400, 860);
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(0x12, 0x12, 0x12);
        Controls.Add(_web);

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoonwellEditor");
        var cache = Path.Combine(appData, "gamedata");
        _commands = new Commands(
            new Settings(appData),
            AskForFolder,
            folder => GameData.GameData.Load(folder, cache),
            startupMap,
            AskForArchive);
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

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TitleBar.Style(Handle);
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

    // The Open File dialog for a map archive, on the window's own thread too.
    private string? AskForArchive(string title) =>
        Invoke(() =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = "Map archives (*.w3x, *.w3m)|*.w3x;*.w3m",
                CheckFileExists = true,
                RestoreDirectory = true,
            };
            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
        });

    // While a page is loaded, the window stays open and asks it: the page sends what is still typed, closes the window
    // at once when nothing is unsaved, and otherwise asks the user to save, discard or cancel; it closes the window
    // with the closeWindow command. With no page, only unsaved edits keep the window open (the window then asks). A
    // shutdown of Windows is held up the same way (Windows then shows that the app is keeping it from shutting down);
    // the Task Manager always closes.
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason is not (CloseReason.UserClosing or CloseReason.WindowsShutDown))
        {
            return;
        }

        var pageLoaded = _pageLoaded && _web.CoreWebView2 != null;
        if (_closeGuard.Closing(_commands.Dirty, pageLoaded) == CloseStep.Close)
        {
            _pageWait.Stop();
            return;
        }

        e.Cancel = true;
        if (!pageLoaded)
        {
            AskIfPageSilent();
            return;
        }

        Post(Bridge.Event("closeRequested", new { dirty = _commands.Dirty }));
        _pageWait.Stop();
        _pageWait.Start();
    }

    // The page did not say it is handling the close (it failed to load, or is broken): the window closes, or asks the
    // user itself when the map has unsaved edits.
    private void AskIfPageSilent()
    {
        _pageWait.Stop();
        var step = _closeGuard.PageSilent(_commands.Dirty);
        if (step == CloseStep.Close)
        {
            _closeGuard.Approve();
            Close();
            return;
        }

        if (step != CloseStep.AskUser)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            "The map has unsaved changes, and the page did not answer. Close the window and lose them?",
            "Moonwell Editor",
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

    private static WebView2 NewWeb() =>
        new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(0x12, 0x12, 0x12) };

    private async Task StartPage()
    {
        var dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MoonwellEditor", "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
        await _web.EnsureCoreWebView2Async(environment);

        var core = _web.CoreWebView2;

        // Only the app's own pages may use the bridge, and the window never leaves them (a link to the web is ignored).
        _origins.Add($"https://{Host}/");
        core.WebMessageReceived += (_, e) =>
        {
            if (_origins.Contains(e.Source))
            {
                _ = Answer(e.TryGetWebMessageAsString());
            }
        };
        core.NavigationStarting += (_, e) =>
        {
            if (!_origins.Contains(e.Uri))
            {
                e.Cancel = true;
                return;
            }

            _pageLoaded = false;
        };
        core.NavigationCompleted += (_, e) => _pageLoaded = e.IsSuccess;

        // The page names the window: the open map's path, then the app's name.
        core.DocumentTitleChanged += (_, _) =>
        {
            if (core.DocumentTitle.Length > 0)
            {
                Text = core.DocumentTitle;
            }
        };

        // The table's Ctrl V reads the clipboard: the app's own pages may, without a prompt. Any other permission or
        // page is left to WebView2's default.
        core.PermissionRequested += (_, e) =>
        {
            if (_origins.Grants(e.PermissionKind, e.Uri))
            {
                e.State = CoreWebView2PermissionState.Allow;
            }
        };
        core.ProcessFailed += OnProcessFailed;

#if !DEBUG
        // The page is not a web page to reload or go back from: a reload would drop what is typed, and the page would
        // start over. A Debug build keeps them, for live development; there, the openMap and reload commands still
        // refuse to drop unsaved edits (the "unsaved" code), and a page loaded again shows the open map (the
        // currentMap command).
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.ContextMenuRequested += (_, e) =>
        {
            var items = e.MenuItems;
            for (var i = items.Count - 1; i >= 0; i--)
            {
                if (items[i].Name is "back" or "forward" or "reload")
                {
                    items.RemoveAt(i);
                }
            }
        };
#endif

#if DEBUG
        var devUrl = Environment.GetEnvironmentVariable("MOONWELL_EDITOR_DEV_URL");
        if (!string.IsNullOrEmpty(devUrl))
        {
            _origins.Add(devUrl);
            core.Navigate(devUrl);
            return;
        }
#endif
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        core.SetVirtualHostNameToFolderMapping(Host, wwwroot, CoreWebView2HostResourceAccessKind.Allow);
        core.Navigate($"https://{Host}/index.html");
    }

    // The page's process ended or hung: the page is loaded again, and shows the open map through the currentMap
    // command (the edits live in this process, so none are lost; a value being typed is). When the browser process
    // itself ended, its WebView2 cannot be used again, so a new one is made.
    private async void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        try
        {
            switch (e.ProcessFailedKind)
            {
                case CoreWebView2ProcessFailedKind.RenderProcessExited:
                case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                    _pageLoaded = false;
                    _web.CoreWebView2?.Reload();
                    break;
                case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                    _pageLoaded = false;
                    await RestartPage();
                    break;
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"The page stopped and could not be loaded again: {exception.Message}",
                "Moonwell Editor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    // Puts a new WebView2 in place of one whose browser process ended, and loads the page in it.
    private async Task RestartPage()
    {
        var old = _web;
        _web = NewWeb();
        Controls.Remove(old);
        old.Dispose();
        Controls.Add(_web);
        await StartPage();
    }

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
