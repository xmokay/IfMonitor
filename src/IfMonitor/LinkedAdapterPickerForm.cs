using System.Net.NetworkInformation;

namespace IfMonitor;

public sealed class LinkedAdapterPickerForm : Form
{
    private readonly ComboBox _combo;
    private readonly List<NetworkInterface> _adapters;

    public MonitoredAdapter? SelectedAdapter { get; private set; }

    public LinkedAdapterPickerForm(string? currentLinkedId, IEnumerable<MonitoredAdapter> monitored)
    {
        HashSet<string> monitoredIds = monitored
            .Select(a => a.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _adapters = NetworkMonitor.ListAdapters()
            .Where(ni => !monitoredIds.Contains(ni.Id))
            .ToList();

        Text = "Select linked adapter";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 140);
        ShowInTaskbar = true;

        var hint = new Label
        {
            AutoSize = false,
            Text = "Disable this adapter when any monitored NIC goes down:",
            Location = new Point(16, 16),
            Size = new Size(448, 24),
        };

        _combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(16, 48),
            Size = new Size(448, 28),
        };

        foreach (NetworkInterface ni in _adapters)
        {
            string item = $"{ni.Name} — {ni.Description} [{ni.OperationalStatus}]";
            _combo.Items.Add(item);
            if (string.Equals(ni.Id, currentLinkedId, StringComparison.OrdinalIgnoreCase))
            {
                _combo.SelectedIndex = _combo.Items.Count - 1;
            }
        }

        if (_combo.Items.Count > 0 && _combo.SelectedIndex < 0)
        {
            _combo.SelectedIndex = 0;
        }

        var clear = new Button
        {
            Text = "Clear",
            Location = new Point(200, 96),
            Size = new Size(80, 28),
        };

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(288, 96),
            Size = new Size(80, 28),
        };

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(384, 96),
            Size = new Size(80, 28),
        };

        AcceptButton = ok;
        CancelButton = cancel;

        clear.Click += (_, _) =>
        {
            SelectedAdapter = null;
            DialogResult = DialogResult.OK;
            Close();
        };

        ok.Click += (_, _) =>
        {
            if (_adapters.Count == 0)
            {
                SelectedAdapter = null;
                return;
            }

            if (_combo.SelectedIndex < 0 || _combo.SelectedIndex >= _adapters.Count)
            {
                DialogResult = DialogResult.None;
                MessageBox.Show(this, "Please select a linked adapter.", "IfMonitor",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            NetworkInterface ni = _adapters[_combo.SelectedIndex];
            SelectedAdapter = new MonitoredAdapter { Id = ni.Id, Name = ni.Name };
        };

        Controls.Add(hint);
        Controls.Add(_combo);
        Controls.Add(clear);
        Controls.Add(ok);
        Controls.Add(cancel);

        if (_adapters.Count == 0)
        {
            ok.Enabled = false;
            _combo.Items.Add("(No available adapters — all are monitored)");
            _combo.SelectedIndex = 0;
        }
    }
}
