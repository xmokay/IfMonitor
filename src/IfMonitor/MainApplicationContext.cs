namespace IfMonitor;

public sealed class MainApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly NetworkMonitor _monitor;
    private readonly ToolStripMenuItem _currentItem;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _recoverItem;
    private readonly Icon _okIcon;
    private readonly Icon _alertIcon;
    private readonly System.Windows.Forms.Timer _blinkTimer;
    private readonly TimeSpan _balloonCooldown = TimeSpan.FromSeconds(30);
    private readonly Dictionary<string, (AdapterHealth Health, DateTime Utc)> _lastBalloon = new(StringComparer.OrdinalIgnoreCase);
    private AppConfig _config;
    private bool _unhealthy;
    private bool _blinkPhase;

    public MainApplicationContext()
    {
        _config = ConfigStore.Load();
        _okIcon = TrayIconFactory.CreateOk();
        _alertIcon = TrayIconFactory.CreateAlert();

        _blinkTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _blinkTimer.Tick += OnBlinkTick;

        _monitor = new NetworkMonitor();
        _monitor.StatusChanged += OnStatusChanged;

        _currentItem = new ToolStripMenuItem("Current: none") { Enabled = false };
        _startItem = new ToolStripMenuItem("Start monitoring", null, (_, _) => StartMonitoring(save: true));
        _stopItem = new ToolStripMenuItem("Stop monitoring", null, (_, _) => StopMonitoring(save: true));
        _startupItem = new ToolStripMenuItem("Run at startup", null, OnToggleStartup) { CheckOnClick = true };
        _recoverItem = new ToolStripMenuItem("Notify on recover", null, OnToggleRecover) { CheckOnClick = true };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_currentItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Select adapters…", null, OnPickAdapter));
        menu.Items.Add(_startItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_recoverItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));

        _tray = new NotifyIcon
        {
            Icon = _okIcon,
            Visible = true,
            Text = "IfMonitor",
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => OnPickAdapter(null, EventArgs.Empty);

        SyncMenuFromConfig();

        _monitor.Configure(_config.Adapters, _config.NotifyOnRecover);

        if (_config.HasAdapters && _config.IsMonitoring)
        {
            StartMonitoring(save: false);
        }
        else
        {
            UpdateMenuState();
            SetTrayIdle();
        }
    }

    private void SyncMenuFromConfig()
    {
        _recoverItem.Checked = _config.NotifyOnRecover;
        _startupItem.Checked = _config.RunAtStartup || StartupHelper.IsEnabled();
        if (_startupItem.Checked != _config.RunAtStartup)
        {
            _config.RunAtStartup = _startupItem.Checked;
            ConfigStore.Save(_config);
        }

        UpdateCurrentLabel();
        UpdateMenuState();
    }

    private void UpdateCurrentLabel()
    {
        _currentItem.Text = !_config.HasAdapters
            ? "Current: none"
            : _config.Adapters.Count == 1
                ? $"Current: {_config.Adapters[0].Name}"
                : $"Current: {_config.Adapters.Count} adapters";

        RefreshTrayTooltip();
    }

    private void RefreshTrayTooltip()
    {
        if (!_config.HasAdapters)
        {
            _tray.Text = "IfMonitor";
            return;
        }

        string summary = _config.Adapters.Count == 1
            ? _config.Adapters[0].Name
            : $"{_config.Adapters.Count} adapters";

        string status = !_monitor.IsRunning
            ? "idle"
            : _unhealthy
                ? $"down {_monitor.UnhealthyCount()}/{_config.Adapters.Count}"
                : "all up";

        _tray.Text = Truncate($"IfMonitor — {summary} [{status}]", 63);
    }

    private void UpdateMenuState()
    {
        bool hasAdapter = _config.HasAdapters;
        bool running = _monitor.IsRunning;
        _startItem.Enabled = hasAdapter && !running;
        _stopItem.Enabled = running;
    }

    private void OnPickAdapter(object? sender, EventArgs e)
    {
        bool wasRunning = _monitor.IsRunning;
        if (wasRunning)
        {
            // Avoid UI hitching while the modal dialog is open / dragged.
            _monitor.Pause();
        }

        DialogResult result;
        IReadOnlyList<MonitoredAdapter> selected;
        using (var form = new AdapterPickerForm(_config.Adapters))
        {
            result = form.ShowDialog();
            selected = form.SelectedAdapters;
        }

        if (result != DialogResult.OK || selected.Count == 0)
        {
            if (wasRunning)
            {
                _monitor.Resume();
            }

            return;
        }

        StopAlertBlink();
        _config.Adapters = selected.ToList();
        _config.IsMonitoring = true;
        ConfigStore.Save(_config);

        _monitor.Configure(_config.Adapters, _config.NotifyOnRecover);
        UpdateCurrentLabel();
        StartMonitoring(save: true);
    }

    private void StartMonitoring(bool save)
    {
        if (!_config.HasAdapters)
        {
            MessageBox.Show(
                "Select one or more adapters to monitor first.",
                "IfMonitor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _lastBalloon.Clear();
        _monitor.Configure(_config.Adapters, _config.NotifyOnRecover);
        _monitor.Start();
        _config.IsMonitoring = true;
        if (save)
        {
            ConfigStore.Save(_config);
        }

        UpdateMenuState();
        if (!_unhealthy)
        {
            _tray.Icon = _okIcon;
        }

        RefreshTrayTooltip();

        string names = _config.Adapters.Count == 1
            ? _config.Adapters[0].Name
            : string.Join(", ", _config.Adapters.Select(a => a.Name));
        _tray.ShowBalloonTip(
            2000,
            "IfMonitor",
            Truncate($"Monitoring {_config.Adapters.Count}: {names}", 120),
            ToolTipIcon.None);
    }

    private void StopMonitoring(bool save)
    {
        _monitor.Stop();
        _config.IsMonitoring = false;
        if (save)
        {
            ConfigStore.Save(_config);
        }

        StopAlertBlink();
        SetTrayIdle();
        UpdateMenuState();
        RefreshTrayTooltip();
    }

    private void OnToggleStartup(object? sender, EventArgs e)
    {
        _config.RunAtStartup = _startupItem.Checked;
        try
        {
            StartupHelper.SetEnabled(_config.RunAtStartup);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to update startup setting: {ex.Message}",
                "IfMonitor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _startupItem.Checked = !_startupItem.Checked;
            _config.RunAtStartup = _startupItem.Checked;
            return;
        }

        ConfigStore.Save(_config);
    }

    private void OnToggleRecover(object? sender, EventArgs e)
    {
        _config.NotifyOnRecover = _recoverItem.Checked;
        _monitor.NotifyOnRecover = _config.NotifyOnRecover;
        ConfigStore.Save(_config);
    }

    private void OnStatusChanged(object? sender, AdapterStatusChangedEventArgs e)
    {
        void Apply()
        {
            SyncAlertFromHealth();
            RefreshTrayTooltip();
            MaybeShowBalloon(e);
        }

        if (_tray.ContextMenuStrip?.InvokeRequired == true)
        {
            _tray.ContextMenuStrip.BeginInvoke(Apply);
        }
        else
        {
            Apply();
        }
    }

    private void SyncAlertFromHealth()
    {
        if (_monitor.IsRunning && _monitor.AnyUnhealthy())
        {
            StartAlertBlink();
        }
        else
        {
            StopAlertBlink();
            if (_monitor.IsRunning)
            {
                _tray.Icon = _okIcon;
            }
        }
    }

    private void MaybeShowBalloon(AdapterStatusChangedEventArgs e)
    {
        if (e.Previous == AdapterHealth.Unknown && e.Current == AdapterHealth.Up)
        {
            return;
        }

        bool shouldBalloon = e.Current switch
        {
            AdapterHealth.Missing or AdapterHealth.Down => true,
            AdapterHealth.Up => _config.NotifyOnRecover
                && e.Previous is AdapterHealth.Down or AdapterHealth.Missing,
            _ => false,
        };

        if (!shouldBalloon)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (_lastBalloon.TryGetValue(e.AdapterId, out var last)
            && last.Health == e.Current
            && now - last.Utc < _balloonCooldown)
        {
            return;
        }

        _lastBalloon[e.AdapterId] = (e.Current, now);

        (string title, string text) = e.Current switch
        {
            AdapterHealth.Missing => (
                "Adapter missing",
                $"Interface \"{e.AdapterName}\" disappeared from the system (unplugged or driver removed)."),
            AdapterHealth.Down => (
                "Adapter down",
                $"Interface \"{e.AdapterName}\" is down (not Up)."),
            AdapterHealth.Up => (
                "Adapter recovered",
                $"Interface \"{e.AdapterName}\" is up again."),
            _ => ("IfMonitor", $"Interface \"{e.AdapterName}\" status changed."),
        };

        _tray.ShowBalloonTip(5000, title, text, ToolTipIcon.None);
    }

    private void StartAlertBlink()
    {
        _unhealthy = true;
        _blinkPhase = false;
        _tray.Icon = _alertIcon;
        if (!_blinkTimer.Enabled)
        {
            _blinkTimer.Start();
        }
    }

    private void StopAlertBlink()
    {
        _unhealthy = false;
        _blinkTimer.Stop();
        _blinkPhase = false;
    }

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        if (!_unhealthy)
        {
            _blinkTimer.Stop();
            return;
        }

        _blinkPhase = !_blinkPhase;
        _tray.Icon = _blinkPhase ? _okIcon : _alertIcon;
    }

    private void SetTrayIdle()
    {
        StopAlertBlink();
        _tray.Icon = _okIcon;
    }

    protected override void ExitThreadCore()
    {
        _blinkTimer.Stop();
        _blinkTimer.Dispose();
        _monitor.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _okIcon.Dispose();
        _alertIcon.Dispose();
        base.ExitThreadCore();
    }

    private static string Truncate(string value, int max)
    {
        if (value.Length <= max)
        {
            return value;
        }

        return value[..(max - 1)] + "…";
    }
}
