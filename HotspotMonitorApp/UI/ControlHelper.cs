using System.Drawing.Drawing2D;

namespace HotspotMonitorApp.UI
{
    public static class ControlHelper
    {
        static bool _invert = false;
        static float _scale = 1;

        public static float Scale => _scale;

        public static void Adjust(RForm container, bool invert = false)
        {
            container.BackColor = RForm.formBack;
            container.ForeColor = RForm.foreMain;

            _invert = invert;
            AdjustControls(container.Controls);
            _invert = false;
        }

        public static void Resize(RForm container, float baseScale = 2)
        {
            _scale = GetDpiScale(container).Value / baseScale;
            if (Math.Abs(_scale - 1) > 0.2) ResizeControls(container.Controls);
        }

        private static void ResizeControls(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                var button = control as RButton;
                if (button != null && button.Image is not null)
                    button.Image = ResizeImage(button.Image);

                ResizeControls(control.Controls);
            }
        }

        private static void AdjustControls(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                AdjustControls(control.Controls);

                var button = control as RButton;
                if (button != null)
                {
                    button.BackColor = button.Secondary ? RForm.buttonSecond : RForm.buttonMain;
                    button.ForeColor = RForm.foreMain;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = RForm.borderMain;

                    if (button.Image is not null)
                        button.Image = AdjustImage(button.Image);
                }

                // Standard Button support
                var stdButton = control as Button;
                if (stdButton != null && !(stdButton is RButton))
                {
                    stdButton.BackColor = RForm.buttonMain;
                    stdButton.ForeColor = RForm.foreMain;
                    stdButton.FlatStyle = FlatStyle.Flat;
                    stdButton.FlatAppearance.BorderColor = RForm.borderMain;
                }

                var pictureBox = control as PictureBox;
                if (pictureBox != null && pictureBox.BackgroundImage is not null)
                    pictureBox.BackgroundImage = AdjustImage(pictureBox.BackgroundImage);

                var comboBox = control as ComboBox;
                if (comboBox != null)
                {
                    comboBox.BackColor = RForm.buttonMain;
                    comboBox.ForeColor = RForm.foreMain;
                }

                var numericUpDown = control as NumericUpDown;
                if (numericUpDown is not null)
                {
                    numericUpDown.ForeColor = RForm.foreMain;
                    numericUpDown.BackColor = RForm.buttonMain;
                }

                var groupBox = control as GroupBox;
                if (groupBox != null)
                {
                    groupBox.ForeColor = RForm.foreMain;
                }

                var panel = control as Panel;
                if (panel != null && panel.Name.Contains("Header"))
                {
                    panel.BackColor = RForm.buttonSecond;
                }

                var checkBox = control as CheckBox;
                if (checkBox != null && checkBox.BackColor != RForm.formBack)
                {
                    checkBox.BackColor = RForm.buttonSecond;
                }

                var label = control as Label;
                if (label != null)
                {
                    label.ForeColor = RForm.foreMain;
                }

                var textBox = control as TextBox;
                if (textBox != null)
                {
                    textBox.BackColor = RForm.buttonMain;
                    textBox.ForeColor = RForm.foreMain;
                }

                var dataGridView = control as DataGridView;
                if (dataGridView != null)
                {
                    dataGridView.BackgroundColor = RForm.formBack;
                    dataGridView.ForeColor = RForm.foreMain;
                    dataGridView.GridColor = RForm.borderMain;
                    dataGridView.DefaultCellStyle.BackColor = RForm.buttonMain;
                    dataGridView.DefaultCellStyle.ForeColor = RForm.foreMain;
                    dataGridView.DefaultCellStyle.SelectionBackColor = RForm.colorAccent;
                    dataGridView.DefaultCellStyle.SelectionForeColor = Color.White;
                    dataGridView.ColumnHeadersDefaultCellStyle.BackColor = RForm.buttonSecond;
                    dataGridView.ColumnHeadersDefaultCellStyle.ForeColor = RForm.foreMain;
                    dataGridView.EnableHeadersVisualStyles = false;
                    dataGridView.RowHeadersDefaultCellStyle.BackColor = RForm.buttonSecond;
                    dataGridView.RowHeadersDefaultCellStyle.ForeColor = RForm.foreMain;
                }

                var menuStrip = control as MenuStrip;
                if (menuStrip != null)
                {
                    menuStrip.BackColor = RForm.buttonSecond;
                    menuStrip.ForeColor = RForm.foreMain;
                }
            }
        }

        public static void AdjustContextMenuStrip(ContextMenuStrip contextMenuStrip)
        {
            if (contextMenuStrip == null) return;

            contextMenuStrip.BackColor = RForm.buttonMain;
            contextMenuStrip.ForeColor = RForm.foreMain;
            contextMenuStrip.RenderMode = ToolStripRenderMode.Professional;
            contextMenuStrip.Renderer = new ThemedToolStripRenderer();

            foreach (ToolStripItem item in contextMenuStrip.Items)
            {
                item.BackColor = RForm.buttonMain;
                item.ForeColor = RForm.foreMain;
            }
        }

        public static Lazy<float> GetDpiScale(Control control)
        {
            return new Lazy<float>(() =>
            {
                using (var graphics = control.CreateGraphics())
                    return graphics.DpiX / 96.0f;
            });
        }

        private static Image ResizeImage(Image image)
        {
            return ResizeImage(image, _scale);
        }

        public static Image ResizeImage(Image image, float scale)
        {
            if (Math.Abs(scale - 1) < 0.1) return image;

            var newSize = new Size((int)(image.Width * scale), (int)(image.Height * scale));
            var pic = new Bitmap(newSize.Width, newSize.Height);

            using (var g = Graphics.FromImage(pic))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, new Rectangle(new Point(), newSize));
            }
            return pic;
        }

        private static Image AdjustImage(Image image)
        {
            var pic = new Bitmap(image);

            if (_invert)
            {
                for (int y = 0; y <= pic.Height - 1; y++)
                {
                    for (int x = 0; x <= pic.Width - 1; x++)
                    {
                        Color col = pic.GetPixel(x, y);
                        pic.SetPixel(x, y, Color.FromArgb(col.A, 255 - col.R, 255 - col.G, 255 - col.B));
                    }
                }
            }

            return pic;
        }
    }

    public class ThemedToolStripRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected)
            {
                using var brush = new SolidBrush(RForm.colorAccent);
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }
            else
            {
                using var brush = new SolidBrush(RForm.buttonMain);
                e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(RForm.buttonMain);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(RForm.borderMain);
            e.Graphics.DrawRectangle(pen, new Rectangle(0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1));
        }
    }
}
