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
    public class MonitorCardItem : Control
    {
        private MonitorItemData data;
        private bool isSelected;
        private bool isHovered = false;

        public bool IsSelected
        {
            get { return isSelected; }
            set
            {
                if (isSelected != value)
                {
                    isSelected = value;
                    this.Invalidate();
                }
            }
        }

        public MonitorCardItem(MonitorItemData data, bool selected, int x, int y, int w, int h)
        {
            this.data = data;
            this.isSelected = selected;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.BackColor = MainForm.ColorCard;
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; Invalidate(); };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush cb = new SolidBrush(MainForm.ColorCard))
            {
                g.FillRectangle(cb, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            Color bg = isSelected ? Color.FromArgb(26, 34, 52) : (isHovered ? Color.FromArgb(20, 26, 40) : Color.FromArgb(14, 18, 28));
            Color border = isSelected ? MainForm.ColorPrimary : (isHovered ? Color.FromArgb(46, 58, 86) : MainForm.ColorCardBorder);

            using (GraphicsPath p = RoundedPanel.CreateRoundedRect(rect, 8))
            {
                using (SolidBrush b = new SolidBrush(bg))
                {
                    g.FillPath(b, p);
                }
                using (Pen pen = new Pen(border, isSelected ? 1.6f : 1.0f))
                {
                    g.DrawPath(pen, p);
                }
            }

            // Sol Seçim Çubuğu
            if (isSelected)
            {
                using (SolidBrush barB = new SolidBrush(MainForm.ColorPrimary))
                {
                    g.FillRectangle(barB, 0, 8, 4, this.Height - 16);
                }
            }

            // Monitör Vektör Simgesi
            int monY = (this.Height - 15) / 2 - 1;
            Rectangle monR = new Rectangle(12, monY, 20, 15);
            using (Pen pMon = new Pen(isSelected ? MainForm.ColorAccent : MainForm.ColorTextMuted, 1.4f))
            {
                g.DrawRectangle(pMon, monR);
                g.DrawLine(pMon, monR.Left + 5, monR.Bottom + 4, monR.Right - 5, monR.Bottom + 4);
                g.DrawLine(pMon, monR.Left + 10, monR.Bottom, monR.Left + 10, monR.Bottom + 4);
            }

            // Ekran Adı ve Çözünürlük
            string devLabel = data.DeviceName;
            if (devLabel.Length > 16) devLabel = devLabel.Substring(0, 16);
            string line1 = string.Format("{0}  •  {1}", devLabel, data.Resolution);

            int textY = (this.Height <= 38) ? 2 : ((this.Height <= 46) ? 4 : 7);
            using (Font f1 = new Font("Segoe UI", (this.Height <= 38) ? 8.0f : 8.5f, FontStyle.Bold))
            using (SolidBrush b1 = new SolidBrush(isSelected ? Color.White : MainForm.ColorText))
            {
                g.DrawString(line1, f1, b1, 40, textY);
            }

            // Alt Rozet Metni
            int tagY = (this.Height <= 38) ? 18 : ((this.Height <= 46) ? 22 : 26);
            int tagH = (this.Height <= 38) ? 13 : 14;
            using (Font f2 = new Font("Segoe UI", 6.8f, FontStyle.Bold))
            {
                Color tagBg = data.IsVirtual ? Color.FromArgb(46, 16, 70) : (data.Screen.Primary ? Color.FromArgb(16, 36, 68) : Color.FromArgb(26, 34, 48));
                Color tagFg = data.IsVirtual ? Color.FromArgb(216, 180, 254) : (data.Screen.Primary ? Color.FromArgb(147, 197, 253) : MainForm.ColorTextMuted);

                int tagW = (int)g.MeasureString(data.Tag, f2).Width + 10;
                Rectangle tagR = new Rectangle(40, tagY, tagW, tagH);
                using (GraphicsPath tp = RoundedPanel.CreateRoundedRect(tagR, 3))
                {
                    using (SolidBrush tb = new SolidBrush(tagBg))
                    {
                        g.FillPath(tb, tp);
                    }
                }
                TextRenderer.DrawText(g, data.Tag, f2, tagR, tagFg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // Sağ Seçim Göstergesi (Radio / Check)
            int dotX = this.Width - 22;
            int dotY = (this.Height - 14) / 2;
            Rectangle dotR = new Rectangle(dotX, dotY, 14, 14);

            using (Pen dotPen = new Pen(isSelected ? MainForm.ColorPrimary : Color.FromArgb(50, 60, 84), 1.5f))
            {
                g.DrawEllipse(dotPen, dotR);
            }
            if (isSelected)
            {
                using (SolidBrush dotFill = new SolidBrush(MainForm.ColorPrimary))
                {
                    g.FillEllipse(dotFill, dotX + 3, dotY + 3, 8, 8);
                }
            }
        }
    }


}