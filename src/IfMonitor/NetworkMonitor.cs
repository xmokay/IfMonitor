using System.Net.NetworkInformation;

namespace IfMonitor;

public enum AdapterHealth
{
    Unknown,
    Up,
    Down,
    Missing,
}

public sealed class AdapterStatusChangedEventArgs : EventArgs
{
    public required string AdapterId { get; init; }
    public required string AdapterName { get; init; }
    public required AdapterHealth Previous { get; init; }
    public required AdapterHealth Current { get; init; }
}

/// <summary>
/// Watches specific adapters via iphlpapi.
/// Change detection is push-based (NotifyIpInterfaceChange, which reports per-interface
/// changes unlike NetworkChange's machine-wide availability), with a slow poll as a safety net.
/// Each check queries only the interfaces we care about (GetIfEntry2 by LUID) instead of
/// enumerating every adapter; full enumeration happens only to confirm a suspected loss.
/// </summary>
public sealed class NetworkMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MonitoredAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AdapterHealth> _health = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Timer _fallbackTimer;
    private readonly System.Threading.Timer _debounceTimer;
    private readonly int _fallbackIntervalMs;
    private readonly int _debounceMs;
    private readonly NativeMethods.IpInterfaceChangeCallback _changeCallback;
    private SynchronizationContext? _uiContext;
    private IntPtr _notifyHandle;
    private int _checkBusy;
    private int _pendingRecheck;
    private bool _running;
    private bool _disposed;

    /// <param name="fallbackIntervalMs">Slow safety-net poll in case a notification is missed.</param>
    /// <param name="debounceMs">Collapse bursts of notifications into a single check.</param>
    public NetworkMonitor(int fallbackIntervalMs = 15_000, int debounceMs = 300)
    {
        _fallbackIntervalMs = Math.Max(5_000, fallbackIntervalMs);
        _debounceMs = Math.Max(50, debounceMs);
        _uiContext = SynchronizationContext.Current;

        // Keep a field reference so the native side never calls into a collected delegate.
        _changeCallback = OnIpInterfaceChanged;

        _fallbackTimer = new System.Threading.Timer(_ => QueueCheck(), null, Timeout.Infinite, Timeout.Infinite);
        _debounceTimer = new System.Threading.Timer(_ => QueueCheck(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler<AdapterStatusChangedEventArgs>? StatusChanged;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    public bool NotifyOnRecover { get; set; } = true;

    public IReadOnlyCollection<MonitoredAdapter> Adapters
    {
        get
        {
            lock (_gate)
            {
                return _adapters.Values.ToList();
            }
        }
    }

    public void Configure(IEnumerable<MonitoredAdapter> adapters, bool notifyOnRecover)
    {
        NotifyOnRecover = notifyOnRecover;
        lock (_gate)
        {
            _adapters.Clear();
            _health.Clear();

            foreach (MonitoredAdapter adapter in adapters)
            {
                if (string.IsNullOrWhiteSpace(adapter.Id))
                {
                    continue;
                }

                _adapters[adapter.Id] = new MonitoredAdapter
                {
                    Id = adapter.Id,
                    Name = string.IsNullOrWhiteSpace(adapter.Name) ? adapter.Id : adapter.Name,
                };
                _health[adapter.Id] = AdapterHealth.Unknown;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_adapters.Count == 0)
            {
                return;
            }

            foreach (string id in _health.Keys.ToList())
            {
                _health[id] = AdapterHealth.Unknown;
            }
        }

        StartWatching();
    }

    /// <summary>Stops watching without resetting health (used while modal UI is open).</summary>
    public void Pause()
    {
        lock (_gate)
        {
            _running = false;
        }

        _fallbackTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
        StopNotifications();
    }

    public void Resume() => StartWatching();

    public void Stop()
    {
        Pause();
        lock (_gate)
        {
            foreach (string id in _health.Keys.ToList())
            {
                _health[id] = AdapterHealth.Unknown;
            }
        }
    }

    public bool AnyUnhealthy()
    {
        lock (_gate)
        {
            return _health.Values.Any(h => h is AdapterHealth.Down or AdapterHealth.Missing);
        }
    }

    public int UnhealthyCount()
    {
        lock (_gate)
        {
            return _health.Values.Count(h => h is AdapterHealth.Down or AdapterHealth.Missing);
        }
    }

    private void StartWatching()
    {
        lock (_gate)
        {
            if (_adapters.Count == 0)
            {
                return;
            }

            _uiContext = SynchronizationContext.Current ?? _uiContext;
            _running = true;
        }

        bool pushEnabled = StartNotifications();

        // Without notifications the timer is the only trigger, so poll far more often.
        int interval = pushEnabled ? _fallbackIntervalMs : 2_000;
        _fallbackTimer.Change(interval, interval);
        QueueCheck();
    }

    private bool StartNotifications()
    {
        StopNotifications();

        IntPtr handle = IntPtr.Zero;
        uint rc = NativeMethods.NotifyIpInterfaceChange(
            NativeMethods.AF_UNSPEC,
            _changeCallback,
            IntPtr.Zero,
            initialNotification: false,
            ref handle);

        if (rc != NativeMethods.NO_ERROR)
        {
            return false;
        }

        _notifyHandle = handle;
        return true;
    }

    private void StopNotifications()
    {
        IntPtr handle = Interlocked.Exchange(ref _notifyHandle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            NativeMethods.CancelMibChangeNotify2(handle);
        }
    }

    private void OnIpInterfaceChanged(IntPtr context, IntPtr row, NativeMethods.MibNotificationType type)
    {
        // Called on an OS worker thread; only touch thread-safe state here.
        _debounceTimer.Change(_debounceMs, Timeout.Infinite);
    }

    private void QueueCheck()
    {
        if (Interlocked.CompareExchange(ref _checkBusy, 1, 0) != 0)
        {
            Interlocked.Exchange(ref _pendingRecheck, 1);
            return;
        }

        List<MonitoredAdapter> adapters;
        lock (_gate)
        {
            if (!_running || _adapters.Count == 0)
            {
                Interlocked.Exchange(ref _checkBusy, 0);
                return;
            }

            adapters = _adapters.Values.ToList();
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Check(adapters);
            }
            finally
            {
                Interlocked.Exchange(ref _checkBusy, 0);
                if (Interlocked.Exchange(ref _pendingRecheck, 0) == 1)
                {
                    QueueCheck();
                }
            }
        });
    }

    private void Check(List<MonitoredAdapter> adapters)
    {
        var probed = new List<(MonitoredAdapter Adapter, AdapterHealth Health)>(adapters.Count);
        Dictionary<string, NetworkInterface>? enumerated = null;

        foreach (MonitoredAdapter adapter in adapters)
        {
            AdapterHealth health = ProbeSingle(adapter.Id);

            // Native lookup could not resolve the interface: confirm with a full
            // enumeration so we don't report a spurious loss.
            if (health is AdapterHealth.Missing or AdapterHealth.Unknown)
            {
                enumerated ??= EnumerateById();
                health = ProbeEnumerated(enumerated, adapter.Id);
            }

            probed.Add((adapter, health));
        }

        var changes = new List<AdapterStatusChangedEventArgs>();

        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            foreach ((MonitoredAdapter adapter, AdapterHealth current) in probed)
            {
                if (!_adapters.ContainsKey(adapter.Id))
                {
                    continue;
                }

                if (!_health.TryGetValue(adapter.Id, out AdapterHealth previous))
                {
                    previous = AdapterHealth.Unknown;
                }

                if (current == previous)
                {
                    continue;
                }

                _health[adapter.Id] = current;
                changes.Add(new AdapterStatusChangedEventArgs
                {
                    AdapterId = adapter.Id,
                    AdapterName = adapter.Name,
                    Previous = previous,
                    Current = current,
                });
            }
        }

        foreach (AdapterStatusChangedEventArgs change in changes)
        {
            RaiseStatusChanged(change);
        }
    }

    /// <summary>
    /// Queries one interface by LUID. Returns Unknown when the native path is unusable.
    /// </summary>
    private static AdapterHealth ProbeSingle(string adapterId)
    {
        if (!Guid.TryParse(adapterId, out Guid guid))
        {
            return AdapterHealth.Unknown;
        }

        try
        {
            if (NativeMethods.ConvertInterfaceGuidToLuid(guid, out ulong luid) != NativeMethods.NO_ERROR)
            {
                return AdapterHealth.Missing;
            }

            var row = new NativeMethods.MibIfRow2 { InterfaceLuid = luid };
            if (NativeMethods.GetIfEntry2(ref row) != NativeMethods.NO_ERROR)
            {
                return AdapterHealth.Missing;
            }

            if (row.OperStatus != NativeMethods.IfOperStatus.Up)
            {
                return AdapterHealth.Down;
            }

            // Cable unplugged while the adapter itself is still enabled.
            return row.MediaConnectState == NativeMethods.NetIfMediaConnectState.Disconnected
                ? AdapterHealth.Down
                : AdapterHealth.Up;
        }
        catch (DllNotFoundException)
        {
            return AdapterHealth.Unknown;
        }
        catch (EntryPointNotFoundException)
        {
            return AdapterHealth.Unknown;
        }
    }

    private static Dictionary<string, NetworkInterface> EnumerateById()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .GroupBy(ni => ni.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, NetworkInterface>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static AdapterHealth ProbeEnumerated(Dictionary<string, NetworkInterface> byId, string adapterId)
    {
        if (!byId.TryGetValue(adapterId, out NetworkInterface? ni))
        {
            return AdapterHealth.Missing;
        }

        return ni.OperationalStatus == OperationalStatus.Up
            ? AdapterHealth.Up
            : AdapterHealth.Down;
    }

    private void RaiseStatusChanged(AdapterStatusChangedEventArgs args)
    {
        void Invoke() => StatusChanged?.Invoke(this, args);

        SynchronizationContext? ctx = _uiContext;
        if (ctx is null)
        {
            Invoke();
            return;
        }

        ctx.Post(_ => Invoke(), null);
    }

    public static IReadOnlyList<NetworkInterface> ListAdapters()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                and not NetworkInterfaceType.Tunnel)
            .OrderBy(ni => ni.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _fallbackTimer.Dispose();
        _debounceTimer.Dispose();
    }
}
