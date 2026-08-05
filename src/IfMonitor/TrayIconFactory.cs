namespace IfMonitor;

/// <summary>Tray icons: green NIC (ok) and red NIC (alert).</summary>
public static class TrayIconFactory
{
    public static Icon CreateOk() => IconArtwork.ToIcon(16, alert: false);

    public static Icon CreateAlert() => IconArtwork.ToIcon(16, alert: true);
}
