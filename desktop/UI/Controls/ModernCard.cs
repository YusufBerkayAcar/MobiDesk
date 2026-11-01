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
    public class ModernCard : Panel
    {
        private string headerTitle;

        public string Title
        {
            get { return headerTitle; }
            set { headerTitle = value; this.Invalidate(); }
        }

        public ModernCard(string title, int x, int y, int w, int h)
        {
            this.headerTitle = title;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.BackColor = MainForm.ColorCard;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN
                return cp;
            }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            this.Invalidate(true);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorBg))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush clearBrush = new SolidBrush(MainForm.ColorBg))
            {
                g.FillRectangle(clearBrush, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath path = RoundedPanel.CreateRoundedRect(rect, 10))
            {
                using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
                {
                    g.FillPath(b, path);
                }
                using (Pen p = new Pen(MainForm.ColorCardBorder, 1.0f))
                {
                    g.DrawPath(p, path);
                }
            }

            if (!string.IsNullOrEmpty(headerTitle))
            {
                using (Font hFont = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                using (SolidBrush hBrush = new SolidBrush(MainForm.ColorTextMuted))
                {
                    g.DrawString(headerTitle, hFont, hBrush, 12, 10);
                }
            }
        }
    }


}