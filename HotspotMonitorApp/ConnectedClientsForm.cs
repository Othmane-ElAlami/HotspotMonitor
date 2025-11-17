using HotspotMonitorService;

namespace HotspotMonitorApp
{
    public class ConnectedClientsForm : Form
    {
        private readonly DataGridView _grid;
        private readonly Button _btnRefresh;
        private readonly Button _btnCopy;
        private readonly Button _btnClose;
        private List<ConnectedClient> _clients;
        private readonly Func<List<ConnectedClient>> _refreshFunc;

        public ConnectedClientsForm(List<ConnectedClient> clients, Func<List<ConnectedClient>> refreshFunc)
        {
            Text = "Connected Clients";
            Size = new Size(600, 350);
            StartPosition = FormStartPosition.CenterScreen;
            _clients = clients ?? new List<ConnectedClient>();
            _refreshFunc = refreshFunc;

            _grid = new DataGridView
            {
                Dock = DockStyle.Top,
                Height = 260,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Device", HeaderText = "Device Name" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ip", HeaderText = "IP Address(es)" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mac", HeaderText = "MAC" });

            _btnRefresh = new Button { Text = "Refresh", Width = 80, Left = 8, Top = 270 };
            _btnCopy = new Button { Text = "Copy", Width = 80, Left = 96, Top = 270 };
            _btnClose = new Button { Text = "Close", Width = 80, Left = 184, Top = 270 };

            _btnRefresh.Click += BtnRefresh_Click;
            _btnCopy.Click += BtnCopy_Click;
            _btnClose.Click += (s, e) => Close();

            Controls.Add(_grid);
            Controls.Add(_btnRefresh);
            Controls.Add(_btnCopy);
            Controls.Add(_btnClose);

            PopulateGrid();
        }

        private void PopulateGrid()
        {
            _grid.Rows.Clear();
            foreach (var c in _clients)
            {
                _grid.Rows.Add(c.DeviceName, c.IpAddresses, c.MacAddress);
            }
        }

        private void BtnRefresh_Click(object? sender, EventArgs e)
        {
            try
            {
                if (_refreshFunc != null)
                {
                    var refreshed = _refreshFunc();
                    if (refreshed != null)
                    {
                        _clients = refreshed;
                        PopulateGrid();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to refresh clients: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnCopy_Click(object? sender, EventArgs e)
        {
            if (_grid.SelectedRows.Count == 0)
            {
                // copy all
                var all = string.Join(Environment.NewLine, _clients.Select(c => $"{c.DeviceName}\t{c.IpAddresses}\t{c.MacAddress}"));
                Clipboard.SetText(all);
                MessageBox.Show("All rows copied to clipboard", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                var row = _grid.SelectedRows[0];
                var line = string.Join('\t', row.Cells.Cast<DataGridViewCell>().Select(cell => cell.Value?.ToString() ?? string.Empty));
                Clipboard.SetText(line);
                MessageBox.Show("Selected row copied to clipboard", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
