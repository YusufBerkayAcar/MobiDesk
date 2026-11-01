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
    public class NotificationCardControl : DoubleBufferedPanel
    {
        private bool cardVisible = true;
        public bool CardVisible
        {
            get { return cardVisible; }
            set { cardVisible = value; }
        }
        private bool isHovered = false;
        private static readonly Color BgColor = Color.FromArgb(17, 24, 39);      // #111827
        private static readonly Color BorderNormal = Color.FromArgb(30, 41, 59); // #1E293B
        private static readonly Color BorderHover = Color.FromArgb(59, 130, 246); // #3B82F6

        public NotificationCardControl()
        {
            this.DoubleBuffered = true;
            this.BackColor = BgColor;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.MouseEnter += delegate { isHovered = true; this.Invalidate(); };
            this.MouseLeave += delegate { isHovered = false; this.Invalidate(); };
        }

        public void SetHover(bool h)
        {
            if (isHovered != h)
            {
                isHovered = h;
                this.Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath path = RoundedPanel.CreateRoundedRect(rect, 8))
            {
                using (SolidBrush b = new SolidBrush(BgColor))
                {
                    g.FillPath(b, path);
                }
                Color bCol = isHovered ? BorderHover : BorderNormal;
                using (Pen p = new Pen(bCol, 1.2f))
                {
                    g.DrawPath(p, path);
                }
            }
        }
    }

}