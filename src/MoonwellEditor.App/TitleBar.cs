using System.Runtime.InteropServices;

namespace MoonwellEditor.App;

/// <summary>
/// Colours the window's own title bar like the page (Vitesse Dark): Windows draws it, with its minimize, maximize and
/// close buttons, in dark mode with the page's surface colour, so snapping and resizing work as in any window
/// (Sven, 2026-10-27). Windows 11 takes the colours; Windows 10 takes dark mode only; older ones ignore both.
/// </summary>
internal static class TitleBar
{
    // DWMWINDOWATTRIBUTE values (dwmapi.h).
    private const int UseImmersiveDarkMode = 20;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    /// <summary>Colours the title bar of the window with this handle.</summary>
    public static void Style(IntPtr window)
    {
        Set(window, UseImmersiveDarkMode, 1);
        Set(window, CaptionColor, ColorRef(0x18, 0x18, 0x18));
        Set(window, TextColor, ColorRef(0xdb, 0xd7, 0xca));
        Set(window, BorderColor, ColorRef(0x26, 0x26, 0x26));
    }

    // A COLORREF is 0x00BBGGRR.
    private static int ColorRef(int red, int green, int blue) => red | green << 8 | blue << 16;

    // A Windows version without the attribute answers with an error code, which changes nothing.
    private static void Set(IntPtr window, int attribute, int value) =>
        _ = DwmSetWindowAttribute(window, attribute, ref value, sizeof(int));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
