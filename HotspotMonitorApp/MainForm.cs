using HotspotMonitorService;
using Microsoft.Extensions.Logging;

namespace HotspotMonitorApp
{
    public partial class MainForm : Form
    {
        private readonly Worker _worker;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenuStrip;
        private readonly ToolStripMenuItem _startWorkerMenuItem;
        private readonly ToolStripMenuItem _stopWorkerMenuItem;

        public MainForm(ILogger<Worker> logger)
        {
            InitializeComponent();

            _worker = new Worker(logger);
            _contextMenuStrip = new ContextMenuStrip();
            _startWorkerMenuItem = new ToolStripMenuItem("Start Worker", null, StartWorkerToolStripMenuItem_Click);
            _contextMenuStrip.Items.Add(_startWorkerMenuItem);

            _stopWorkerMenuItem = new ToolStripMenuItem("Stop Worker", null, StopWorkerToolStripMenuItem_Click) { Visible = false};

            _contextMenuStrip.Items.Add(_stopWorkerMenuItem);
            _contextMenuStrip.Items.Add("Exit", null, ExitToolStripMenuItem_Click);

            _notifyIcon = new NotifyIcon
            {
                Icon = ConvertPngToIcon(Properties.Resources.Off, new Size(16, 16)),
                ContextMenuStrip = _contextMenuStrip,
                Visible = true
            };
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
        }

        private void StopWorkerToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            _worker.StopWork();
            _notifyIcon.Icon = ConvertPngToIcon(Properties.Resources.Off, new Size(16, 16));
            _startWorkerMenuItem.Visible = true; // Show the Start Worker menu item
            _stopWorkerMenuItem.Visible = false; // Hide the Stop Worker menu item
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
    }
}

