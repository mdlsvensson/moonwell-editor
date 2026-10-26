using Microsoft.Web.WebView2.Core;

namespace MoonwellEditor.App;

/// <summary>
/// The origins of the app's own pages (<c>https://app.moonwelleditor</c>, and the dev server's in a Debug build): only
/// they may use the bridge, be navigated to, and read the clipboard. An origin is a URL's scheme, host and port, with
/// the case of the host ignored.
/// </summary>
public sealed class PageOrigins
{
    private readonly HashSet<string> _origins = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds the origin of <paramref name="url"/>; a URL that cannot be read adds nothing.</summary>
    /// <param name="url">A page of the app, such as <c>https://app.moonwelleditor/</c>.</param>
    public void Add(string url)
    {
        var origin = Of(url);
        if (origin != "")
        {
            _origins.Add(origin);
        }
    }

    /// <summary>True when <paramref name="url"/> is on one of the app's origins.</summary>
    /// <param name="url">The URL to check; one that cannot be read is on none.</param>
    public bool Contains(string url)
    {
        var origin = Of(url);
        return origin != "" && _origins.Contains(origin);
    }

    /// <summary>
    /// Whether a page's request for a permission is granted without asking: reading the clipboard (for Ctrl V in the
    /// table) by one of the app's own pages. Every other permission, and every other origin, is left to WebView2's
    /// default.
    /// </summary>
    /// <param name="kind">The permission the page asked for.</param>
    /// <param name="url">The URL of the page that asked.</param>
    public bool Grants(CoreWebView2PermissionKind kind, string url) =>
        kind == CoreWebView2PermissionKind.ClipboardRead && Contains(url);

    // A URL's scheme, host and port (https://app.moonwelleditor); "" for a URL that cannot be read.
    private static string Of(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : "";
}
