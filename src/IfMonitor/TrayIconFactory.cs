namespace IfMonitor;

/// <summary>
/// Tray icons: green (monitoring OK), blinking red (adapter down),
/// solid orange (linked still off), solid red (monitoring stopped).
/// </summary>
public static class TrayIconFactory
{
    public static Icon CreateOk() => IconArtwork.ToIcon(16, IconStyle.Ok);

    public static Icon CreateAlert() => IconArtwork.ToIcon(16, IconStyle.Alert);

    public static Icon CreateStopped() => IconArtwork.ToIcon(16, IconStyle.Alert);

    public static Icon CreateLinkedOff() => IconArtwork.ToIcon(16, IconStyle.Warn);
}
