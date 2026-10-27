using System.Runtime.InteropServices;

namespace MoonwellEditor.App;

/// <summary>
/// The window's frame. The page draws the title bar itself, thin (as Windows draws a maximized window's), with its
/// minimize, maximize and close buttons (Sven, 2026-10-27): Windows draws the caption of a window that is not maximized
/// 8 px taller and cannot be made thinner, so the window keeps its frame (the resize borders, snapping, the shadow and
/// the rounded corners) but not its caption. The page marks its bar as a drag region, which WebView2 turns into the
/// window's caption (dragging, snapping, a double click to maximize, the system menu on a right click).
/// </summary>
internal static class TitleBar
{
    // DWMWINDOWATTRIBUTE values (dwmapi.h).
    private const int UseImmersiveDarkMode = 20;
    private const int BorderColor = 34;

    private const int FrameMetric = 33; // SM_CYFRAME
    private const int PaddedBorderMetric = 92; // SM_CXPADDEDBORDER

    private const uint NoSize = 0x0001;
    private const uint NoMove = 0x0002;
    private const uint NoZOrder = 0x0004;
    private const uint FrameChanged = 0x0020;

    /// <summary>The window message that asks for the client area (WM_NCCALCSIZE).</summary>
    public const int CalcSizeMessage = 0x0083;

    /// <summary>The window message that asks what is under the pointer (WM_NCHITTEST).</summary>
    public const int HitTestMessage = 0x0084;

    /// <summary>The height of the strip at the top where the window's top edge resizes it, before DPI scaling.</summary>
    public const int ResizeStrip = 4;

    private const int Client = 1; // HTCLIENT
    private const int Top = 12; // HTTOP
    private const int TopLeft = 13; // HTTOPLEFT
    private const int TopRight = 14; // HTTOPRIGHT

    /// <summary>
    /// Gives the window a dark frame with the page's border colour, and has Windows work out its client area again
    /// (see <see cref="ClientTop"/>). Windows 11 takes the colour; Windows 10 takes dark mode only.
    /// </summary>
    public static void Style(IntPtr window)
    {
        Set(window, UseImmersiveDarkMode, 1);
        Set(window, BorderColor, 0x26 | 0x26 << 8 | 0x26 << 16);
        SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, NoMove | NoSize | NoZOrder | FrameChanged);
    }

    /// <summary>Gives the window's border a colour (the page's theme's); Windows 11 takes it, older ones ignore it.</summary>
    public static void SetBorder(IntPtr window, Color color) =>
        Set(window, BorderColor, color.R | color.G << 8 | color.B << 16);

    /// <summary>
    /// Where the client area starts, given the window's top before Windows took out its caption: at the window's top,
    /// or below the frame a maximized window has above the screen's edge.
    /// </summary>
    public static int ClientTop(IntPtr window, int windowTop) =>
        IsZoomed(window) ? windowTop + FrameHeight(window) : windowTop;

    /// <summary>
    /// What a point of the client area is: the top edge (or a top corner) within <paramref name="strip"/> pixels of the
    /// top, else the client area. The page starts below the strip while the window is not maximized, so the strip
    /// is the window's own; a maximized window has none.
    /// </summary>
    public static int HitTest(Point client, int width, int strip, int corner) =>
        client.Y >= strip ? Client
        : client.X < corner ? TopLeft
        : client.X >= width - corner ? TopRight
        : Top;

    /// <summary>The height of the frame's top border, in the window's pixels.</summary>
    public static int FrameHeight(IntPtr window)
    {
        var dpi = GetDpiForWindow(window);
        return GetSystemMetricsForDpi(FrameMetric, dpi) + GetSystemMetricsForDpi(PaddedBorderMetric, dpi);
    }

    // A Windows version without the attribute answers with an error code, which changes nothing.
    private static void Set(IntPtr window, int attribute, int value) =>
        _ = DwmSetWindowAttribute(window, attribute, ref value, sizeof(int));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
