using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenMonitoringApp
{
    public class ModernStudioTabs : Panel
    {
        private int selectedIndex = 0;
        public event Action<int> SelectedTabChanged;

        private int flowBadge = 0;
        private int notificationBadge = 0;

        public int FlowBadge
        {
            get { return flowBadge; }
            set { flowBadge = value; this.Invalidate(); }
        }

        public int NotificationBadge
        {
            get { return notificationBadge; }
            set { notificationBadge = value; this.Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                if (selectedIndex != value)
                {
                    selectedIndex = value;
                    this.Invalidate();
                    if (SelectedTabChanged != null) SelectedTabChanged(selectedIndex);
                }
            }
        }

        private string[] tabTitles = new string[]
        {
            "🖥️ İkinci Ekran",
            "💬 MobiFlow",
            "🔔 Bildirimler"
        };

        public void UpdateLanguage()
        {
            tabTitles = new string[]
            {
                L10n.T("🖥️ İkinci Ekran", "🖥️ Second Screen"),
                "💬 MobiFlow",
                L10n.T("🔔 Bildirimler", "🔔 Notifications")
            };
            this.Invalidate();
        }

        private int hoveredIndex = -1;

        public ModernStudioTabs(int x, int y, int width, int height)
        {
            this.Location = new Point(x, y);
            this.Size = new Size(width, height);
            this.BackColor = MainForm.ColorBg;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            UpdateLanguage();

            this.MouseMove += delegate(object s, MouseEventArgs e)
            {
                int tabW = this.Width / 3;
                int hIdx = Math.Min(2, Math.Max(0, e.X / tabW));
                if (hoveredIndex != hIdx)
                {
                    hoveredIndex = hIdx;
                    this.Cursor = Cursors.Hand;
                    this.Invalidate();
                }
            };

            this.MouseLeave += delegate(object s, EventArgs e)
            {
                hoveredIndex = -1;
                this.Cursor = Cursors.Default;
                this.Invalidate();
            };

            this.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    int tabW = this.Width / 3;
                    SelectedIndex = Math.Min(2, Math.Max(0, e.X / tabW));
                }
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int tabW = this.Width / 3;
            int tabH = this.Height;

            // Arka plan rayı (Track)
            using (GraphicsPath trackPath = CreateRoundedRectanglePath(new Rectangle(0, 0, this.Width - 1, tabH - 1), 8))
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(18, 24, 38)))
            using (Pen trackPen = new Pen(MainForm.ColorCardBorder, 1.0f))
            {
                g.FillPath(trackBrush, trackPath);
                g.DrawPath(trackPen, trackPath);
            }

            for (int i = 0; i < 3; i++)
            {
                Rectangle tabRect = new Rectangle(i * tabW + 2, 2, tabW - 4, tabH - 4);
                bool isSelected = (i == selectedIndex);
                bool isHovered = (i == hoveredIndex && !isSelected);

                if (isSelected)
                {
                    using (GraphicsPath selPath = CreateRoundedRectanglePath(tabRect, 6))
                    using (LinearGradientBrush selBrush = new LinearGradientBrush(tabRect, MainForm.ColorPrimary, MainForm.ColorPrimaryDark, 90f))
                    {
                        g.FillPath(selBrush, selPath);
                    }
                }
                else if (isHovered)
                {
                    using (GraphicsPath hovPath = CreateRoundedRectanglePath(tabRect, 6))
                    using (SolidBrush hovBrush = new SolidBrush(Color.FromArgb(28, 38, 58)))
                    {
                        g.FillPath(hovBrush, hovPath);
                    }
                }

                // Metin ve Rozetler
                string title = tabTitles[i];
                if (i == 1 && flowBadge > 0)
                {
                    title += " (" + flowBadge + ")";
                }
                else if (i == 2 && notificationBadge > 0)
                {
                    title += " (" + notificationBadge + ")";
                }

                Color textColor = isSelected ? Color.White : (isHovered ? MainForm.ColorText : MainForm.ColorTextMuted);
                using (Font font = new Font("Segoe UI Emoji", 9.0f, isSelected ? FontStyle.Bold : FontStyle.Regular))
                using (SolidBrush textBrush = new SolidBrush(textColor))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(title, font, textBrush, tabRect, sf);
                }
            }
        }

        private GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

}