# IfMonitor

A lightweight Windows system-tray app that watches one or more network adapters and alerts you when any of them go down or disappear. Zero third-party dependencies.

## Primary use case

**Multiple NICs are online, and each can reach the Internet.**

When one link fails, Windows often keeps browsing through the others—so nothing looks “offline,” and a loose USB Wi-Fi stick, flaky dock Ethernet, or quietly disabled adapter can go unnoticed.

IfMonitor watches the adapters you care about and fires a tray alert (balloon + blinking icon) the moment **any** of them drops, even if the rest of the network still works.

### Dual-NIC split routing (work vs personal)

A common setup at work: two adapters online at once—company Ethernet or VPN for work apps, and your own Wi‑Fi or hotspot for everything else. You tune the route table so only work-related destinations go out the corporate NIC; the rest stays on your personal link. That way everyday browsing, streaming, and messaging never hit the company network (or its monitoring and filtering).

That split only holds while **both** links are healthy. If one adapter drops, Windows may reroute traffic in the background—work tools can break without an obvious “no internet” moment, or personal traffic might start flowing through corporate paths. Select both NICs in IfMonitor and you’ll know right away when either side of the split fails.

## Features

- Monitor multiple adapters at once, driven by per-interface OS notifications (`NotifyIpInterfaceChange`) with a ~15s poll as a safety net
- Reads the same state `netsh interface ip show interface` reports, querying only the selected adapters (`GetIfEntry2` by LUID) instead of enumerating all of them
- Detects both a disabled/unplugged adapter and an adapter that is still enabled with the cable unplugged (`MediaConnectState`)
- Balloon tip per adapter when it goes missing or leaves the Up state
- Tray icon blinks when any monitored adapter is unhealthy
- Optional recover notifications
- Remembers selection in `%LocalAppData%\IfMonitor\config.json` and resumes monitoring on next launch
- Optional run-at-startup (current-user `Run` registry value)
- Single-instance

## Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) for framework-dependent publishes
- Or a self-contained publish (no runtime install needed)

## Run (development)

```powershell
dotnet run --project src/IfMonitor
```

Find the shield icon in the system tray, then right-click:

1. **Select adapters…** — check one or more NICs; monitoring starts after OK
2. **Start monitoring** / **Stop monitoring**
3. **Run at startup** / **Notify on recover**
4. **Exit**

## Publish

Framework-dependent single file (requires .NET 8 Desktop Runtime):

```powershell
dotnet publish src/IfMonitor -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Self-contained single file:

```powershell
dotnet publish src/IfMonitor -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Output: `publish\IfMonitor.exe`

## Icon assets

Branded icons live under [`src/IfMonitor/Assets/`](src/IfMonitor/Assets/):

| File | Purpose |
|------|---------|
| `icon.png` | Green NIC artwork (96×96) |
| `icon-alert.png` | Red NIC artwork (96×96), used when blinking alert |
| `IfMonitor.ico` | Multi-size icon for the `.exe` file |

To regenerate the `.ico` after editing `icon.png`:

```powershell
dotnet run --project tools/BuildIcon -- src/IfMonitor/Assets
```

**Tray vs notification icon**

| Where | What controls it |
|-------|------------------|
| **Tray** (taskbar overflow) | Embedded `icon.png` (NIC card) |
| **Action Center / toast** | Icon embedded in the **`.exe` you run**, plus the Start Menu shortcut (`IfMonitor.lnk`) |

Code does **not** draw a shield. A shield (with or without an old green strip) means Windows is still using a **cached or generic app identity**, or an **old `IfMonitor.exe`** (e.g. `D:\Program Files\IfMonitor\`).

On startup the app registers an AppUserModelID and refreshes `%AppData%\Microsoft\Windows\Start Menu\Programs\IfMonitor.lnk` to point at the current exe.

**Deploy steps after rebuild**

1. Tray → **Exit**
2. Publish / build, then **replace** the exe at the path you actually use (check registry Run key).
3. Start IfMonitor once from that new exe (creates/updates the Start Menu shortcut).
4. If the icon is still wrong, clear the shell icon cache (below) and trigger a new notification.

Check startup path:

```powershell
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v IfMonitor
Get-Process IfMonitor -ErrorAction SilentlyContinue | Select-Object Path
```


## Configuration

Path: `%LocalAppData%\IfMonitor\config.json`

| Field | Meaning |
|------|---------|
| `adapters` | List of `{ "id", "name" }` to monitor |
| `isMonitoring` | If true, auto-start monitoring on launch |
| `notifyOnRecover` | Notify when an adapter returns to Up |
| `runAtStartup` | Startup preference |


