using System.Net.NetworkInformation;
using System.Reflection;

namespace IfMonitor;

public sealed class AdapterPickerForm : Form
{
    private readonly ListView _list;
    private readonly List<NetworkInterface> _adapters;

    public IReadOnlyList<MonitoredAdapter> SelectedAdapters { get; private set; } = [];

    public AdapterPickerForm(IEnumerable<MonitoredAdapter> currentlySelected)
    {
        HashSet<string> selectedIds = currentlySelected
            .Select(a => a.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _adapters = NetworkMonitor.ListAdapters().ToList();

        Text = "Select adapters to monitor (multi-select)";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 360);
        ShowInTaskbar = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        UpdateStyles();

        var hint = new Label
        {
            AutoSize = false,
            Text = "Check the network interfaces to monitor:",
            Location = new Point(16, 12),
            Size = new Size(480, 24),
        };

        _list = new ListView
        {
            Location = new Point(16, 40),
            Size = new Size(488, 260),
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            MultiSelect = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            HideSelection = false,
            ShowItemToolTips = true,
            BorderStyle = BorderStyle.FixedSingle,
        };
        EnableDoubleBuffer(_list);

        _list.Columns.Add("Name", 150);
        _list.Columns.Add("Status", 80);
        _list.Columns.Add("Description", 230);

        _list.BeginUpdate();
        try
        {
            foreach (NetworkInterface ni in _adapters)
            {
                var item = new ListViewItem(ni.Name)
                {
                    Tag = ni,
                    ToolTipText = ni.Description,
                    Checked = selectedIds.Contains(ni.Id),
                };
                item.SubItems.Add(ni.OperationalStatus.ToString());
                item.SubItems.Add(ni.Description);
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(320, 316),
            Size = new Size(80, 28),
        };

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(416, 316),
            Size = new Size(80, 28),
        };

        AcceptButton = ok;
        CancelButton = cancel;

        ok.Click += (_, _) =>
        {
            var selected = new List<MonitoredAdapter>();
            foreach (ListViewItem item in _list.CheckedItems)
            {
                if (item.Tag is not NetworkInterface ni)
                {
                    continue;
                }

                selected.Add(new MonitoredAdapter { Id = ni.Id, Name = ni.Name });
            }

            if (selected.Count == 0)
            {
                DialogResult = DialogResult.None;
                MessageBox.Show(
                    this,
                    "Please select at least one adapter.",
                    "IfMonitor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            SelectedAdapters = selected;
        };

        Controls.Add(hint);
        Controls.Add(_list);
        Controls.Add(ok);
        Controls.Add(cancel);

        if (_adapters.Count == 0)
        {
            ok.Enabled = false;
            _list.Items.Add(new ListViewItem("(No adapters found)"));
        }
    }

    private static void EnableDoubleBuffer(Control control)
    {
        typeof(Control).InvokeMember(
            "DoubleBuffered",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.SetProperty,
            binder: null,
            target: control,
            args: [true]);
    }
}
