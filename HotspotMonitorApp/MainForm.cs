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
        private readonly System.Windows.Forms.Timer _statusUpdateTimer;
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
            _contextMenuStrip.Items.Add("Exit", null, ExitToolStripMenuItem_Click);
            _contextMenuStrip.Items[2].ToolTipText = "Exit the application";

            _notifyIcon = new NotifyIcon
            {
                Icon = ConvertPngToIcon(Properties.Resources.Off, new Size(16, 16)),
                ContextMenuStrip = _contextMenuStrip,
                Text = NotifyIconStoppedText,
                Visible = true
            };
            // Periodically update tooltip with device count
            _statusUpdateTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _statusUpdateTimer.Tick += async (s, ev) =>
            {
                // Avoid overlapping calls and keep UI responsive
                _statusUpdateTimer.Stop();
                try
                {
                    var running = _isWorkerRunning;
                    var count = await Task.Run(() => _worker.GetConnectedClientCount());
                    UpdateNotifyIconStatus(running, null, count);
                }
                catch { /* Ignore exceptions from timer */ }
                finally
                {
                    _statusUpdateTimer.Start();
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
            _statusUpdateTimer.Start();
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
            _statusUpdateTimer.Stop();
        }

        private void ExitToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            _notifyIcon.Visible = false;
            Application.Exit();
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

