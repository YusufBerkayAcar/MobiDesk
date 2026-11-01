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
    public class ModernSwitch : Control
    {
        private string title;
        private string subtitle;
        private bool isChecked = false;
        private bool isHovered = false;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked != value)
                {
                    isChecked = value;
                    Invalidate();
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                }
            }
        }

        public void UpdateText(string t, string sub)
        {
            this.title = t;
            this.subtitle = sub;
            Invalidate();
        }

        public ModernSwitch(string title, string subtitle, int x, int y, int w, int h)
        {
            this.title = title;
            this.subtitle = subtitle;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.BackColor = MainForm.ColorCard;
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; Invalidate(); };
            this.Click += delegate(object s, EventArgs e) { this.Checked = !this.Checked; };
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

            // iOS / Fluent tarzı anahtar hapı
            int swW = 38;
            int swH = 20;
            int swY = (this.Height - swH) / 2;
            Rectangle swRect = new Rectangle(0, swY, swW, swH);

            Color swBg = isChecked ? MainForm.ColorPrimary : Color.FromArgb(28, 36, 52);
            using (GraphicsPath p = RoundedPanel.CreateRoundedRect(swRect, swH / 2))
            {
                using (SolidBrush b = new SolidBrush(swBg))
                {
                    g.FillPath(b, p);
                }
                using (Pen pen = new Pen(isChecked ? Color.FromArgb(129, 140, 248) : Color.FromArgb(46, 58, 80), 1.0f))
                {
                    g.DrawPath(pen, p);
                }
            }

            // Kayar Düğme
            int knobD = 14;
            int knobX = isChecked ? (swW - knobD - 3) : 3;
            int knobY = swY + (swH - knobD) / 2;
            using (SolidBrush kb = new SolidBrush(Color.White))
            {
                g.FillEllipse(kb, knobX, knobY, knobD, knobD);
            }

            // Başlık ve Alt Bilgi (Dikeyde Ortalanmış)
            int txtX = swW + 12;
            int txtY = Math.Max(2, (this.Height - 34) / 2);
            Rectangle titleR = new Rectangle(txtX, txtY, this.Width - txtX, 17);
            using (Font fTitle = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            {
                Color fg = isHovered ? Color.White : MainForm.ColorText;
                TextRenderer.DrawText(g, title, fTitle, titleR, fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            Rectangle subR = new Rectangle(txtX, txtY + 17, this.Width - txtX, 15);
            using (Font fSub = new Font("Segoe UI", 7.5f))
            {
                TextRenderer.DrawText(g, subtitle, fSub, subR, MainForm.ColorTextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }


}