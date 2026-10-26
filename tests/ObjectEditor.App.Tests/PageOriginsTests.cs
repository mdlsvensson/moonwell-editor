using Microsoft.Web.WebView2.Core;
using ObjectEditor.App;

namespace ObjectEditor.App.Tests;

public sealed class PageOriginsTests
{
    private static PageOrigins AppAndDev()
    {
        var origins = new PageOrigins();
        origins.Add("https://app.objecteditor/");
        origins.Add("http://localhost:5173/");
        return origins;
    }

    [Fact]
    public void The_app_s_own_pages_are_its_origins()
    {
        var origins = AppAndDev();

        Assert.True(origins.Contains("https://app.objecteditor/index.html"));
        Assert.True(origins.Contains("https://APP.objecteditor/assets/x.js"));
        Assert.True(origins.Contains("http://localhost:5173/src/App.svelte"));
        Assert.False(origins.Contains("http://app.objecteditor/"));       // another scheme
        Assert.False(origins.Contains("https://app.objecteditor:8443/")); // another port
        Assert.False(origins.Contains("https://example.com/"));
        Assert.False(origins.Contains("http://localhost:5174/"));
        Assert.False(origins.Contains(""));
        Assert.False(origins.Contains("not a url"));
    }

    [Fact]
    public void A_url_that_cannot_be_read_adds_no_origin()
    {
        var origins = new PageOrigins();
        origins.Add("not a url");
        origins.Add("");

        Assert.False(origins.Contains("not a url"));
        Assert.False(origins.Contains(""));
    }

    [Fact]
    public void Only_reading_the_clipboard_is_granted_and_only_to_the_app_s_own_pages()
    {
        var origins = AppAndDev();

        Assert.True(origins.Grants(CoreWebView2PermissionKind.ClipboardRead, "https://app.objecteditor/index.html"));
        Assert.True(origins.Grants(CoreWebView2PermissionKind.ClipboardRead, "http://localhost:5173/"));
        Assert.False(origins.Grants(CoreWebView2PermissionKind.ClipboardRead, "https://example.com/"));
        Assert.False(origins.Grants(CoreWebView2PermissionKind.ClipboardRead, "not a url"));
        foreach (var kind in Enum.GetValues<CoreWebView2PermissionKind>())
        {
            if (kind != CoreWebView2PermissionKind.ClipboardRead)
            {
                Assert.False(origins.Grants(kind, "https://app.objecteditor/index.html"), kind.ToString());
            }
        }
    }
}
