namespace IfMonitor;

/// <summary>Tray icons rendered from <see cref="IconArtwork"/> (same artwork as the .exe icon).</summary>
public static class TrayIconFactory
{
    public static Icon CreateOk() => IconArtwork.ToIcon(16);

    public static Icon CreateAlert() => IconArtwork.ToIcon(16, IconArtwork.AlertBarColor);

    public static Icon CreateAlertAlt() => IconArtwork.ToIcon(16, IconArtwork.AlertAltBarColor);
}
