using HotspotMonitorService;
using Microsoft.Extensions.Logging;

namespace HotspotMonitorApp
{
    public partial class MainForm : Form
    {
        private const string NotifyIconTitle = "Hotspot Monitor";
        private const string NotifyIconRunningText = NotifyIconTitle + ": Running";
        private const string NotifyIconStoppedText = NotifyIconTitle + ": Stopped";
        private readonly Worker _worker;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenuStrip;
        private readonly ToolStripMenuItem _startWorkerMenuItem;
        private readonly ToolStripMenuItem _stopWorkerMenuItem;
        private readonly ToolStripMenuItem _showConnectedClientsMenuItem;
        private bool _isWorkerRunning;

        public MainForm(ILogger<Worker> logger)
        {
            InitializeComponent();

            _worker = new Worker(logger);
            _contextMenuStrip = new ContextMenuStrip();
            // Show tool tips for context menu items
            _contextMenuStrip.ShowItemToolTips = true;
            _startWorkerMenuItem = new ToolStripMenuItem("Start Worker", null, StartWorkerToolStripMenuItem_Click);
            _startWorkerMenuItem.ToolTipText = "Start the Hotspot Monitor service";
            _contextMenuStrip.Items.Add(_startWorkerMenuItem);

            _stopWorkerMenuItem = new ToolStripMenuItem("Stop Worker", null, StopWorkerToolStripMenuItem_Click) { Visible = false };
            _stopWorkerMenuItem.ToolTipText = "Stop the Hotspot Monitor service";

            _contextMenuStrip.Items.Add(_stopWorkerMenuItem);
            _showConnectedClientsMenuItem = new ToolStripMenuItem("Connected Clients", null, ShowConnectedClientsToolStripMenuItem_Click) { Enabled = false };
            _showConnectedClientsMenuItem.ToolTipText = "List connected clients (MAC - Hostnames)";
            _contextMenuStrip.Items.Add(_showConnectedClientsMenuItem);
            _contextMenuStrip.Items.Add("Exit", null, ExitToolStripMenuItem_Click);
            _contextMenuStrip.Items[2].ToolTipText = "Exit the application";

            _notifyIcon = new NotifyIcon
            {
                Icon = ConvertPngToIcon(Properties.Resources.Off, new Size(16, 16)),
                ContextMenuStrip = _contextMenuStrip,
                Text = NotifyIconStoppedText,
                Visible = true
            };
            // Subscribe to Worker client count change events (event-driven updates)
            _worker.ClientCountChanged += (count) =>
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => { UpdateNotifyIconStatus(_isWorkerRunning, null, count); _showConnectedClientsMenuItem.Enabled = (count > 0); }));
                }
                else
                {
                    UpdateNotifyIconStatus(_isWorkerRunning, null, count);
                    _showConnectedClientsMenuItem.Enabled = (count > 0);
                }
            };
            // Set an initial tooltip text which also appears on hover
            UpdateNotifyIconStatus(false);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Hide();
            ShowInTaskbar = false;
            StartWorkerToolStripMenuItem_Click(e, e);
        }

        private void StartWorkerToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            _worker.StartWork();
            _notifyIcon.Icon = ConvertPngToIcon(Properties.Resources.On, new Size(16, 16));
            _startWorkerMenuItem.Visible = false; // Hide the Start Worker menu item
            _stopWorkerMenuItem.Visible = true; // Show the Stop Worker menu item
            _isWorkerRunning = true;
            // Update the icon tooltip and show a brief balloon
            UpdateNotifyIconStatus(true, "Service started");
        }

        private void StopWorkerToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            _worker.StopWork();
            _notifyIcon.Icon = ConvertPngToIcon(Properties.Resources.Off, new Size(16, 16));
            _startWorkerMenuItem.Visible = true; // Show the Start Worker menu item
            _stopWorkerMenuItem.Visible = false; // Hide the Stop Worker menu item
            _isWorkerRunning = false;
            // Update the icon tooltip and show a brief balloon
            UpdateNotifyIconStatus(false, "Service stopped");
        }

        private void ExitToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            _notifyIcon.Visible = false;
            Application.Exit();
        }

        private async void ShowConnectedClientsToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            try
            {
                var clients = await Task.Run(() => _worker.GetConnectedClients());
                if (clients == null || clients.Count == 0)
                {
                    MessageBox.Show("No connected clients found.", "Connected Clients", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Open a non-modal dialog showing clients in a user-friendly grid
                var dlg = new ConnectedClientsForm(clients, () => _worker.GetConnectedClients());
                dlg.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to retrieve connected clients: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static Icon ConvertPngToIcon(Image pngImage, Size iconSize)
        {
            Bitmap bmp = new(pngImage, iconSize);
            IntPtr hIcon = bmp.GetHicon();
            return Icon.FromHandle(hIcon);
        }

        private void UpdateNotifyIconStatus(bool running, string? balloonText = null, int? deviceCount = null)
        {
            try
            {
                var baseText = running ? NotifyIconRunningText : NotifyIconStoppedText;
                var countText = deviceCount.HasValue && deviceCount.Value >= 0 ? $" ({deviceCount.Value} device{(deviceCount.Value == 1 ? "" : "s")})" : string.Empty;
                _notifyIcon.Text = baseText + countText;
                if (!string.IsNullOrWhiteSpace(balloonText))
                {
                    _notifyIcon.BalloonTipTitle = NotifyIconTitle;
                    _notifyIcon.BalloonTipText = balloonText;
                    _notifyIcon.ShowBalloonTip(1500);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // Tooltip text too long, truncate to fit
                _notifyIcon.Text = (_notifyIcon.Text.Length > 63) ? _notifyIcon.Text.Substring(0, 63) : _notifyIcon.Text;
            }
        }
    }
}

