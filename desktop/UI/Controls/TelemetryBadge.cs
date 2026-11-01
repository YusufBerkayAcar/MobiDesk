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
    public class TelemetryBadge : Control
    {
        private string label;
        private string val;

        public string Value
        {
            get { return val; }
            set { val = value; Invalidate(); }
        }

        public string Label
        {
            get { return label; }
            set { label = value; Invalidate(); }
        }

        public TelemetryBadge(string lbl, string initialVal, int x, int y, int w, int h)
        {
            this.label = lbl;
            this.val = initialVal;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.BackColor = MainForm.ColorCard;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
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
            using (GraphicsPath p = RoundedPanel.CreateRoundedRect(rect, 6))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(14, 18, 28)))
                {
                    g.FillPath(b, p);
                }
                using (Pen pen = new Pen(Color.FromArgb(32, 42, 64), 1.0f))
                {
                    g.DrawPath(pen, p);
                }
            }

            Rectangle topR = new Rectangle(0, 2, this.Width, 11);
            using (Font fL = new Font("Segoe UI", 6.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, label, fL, topR, MainForm.ColorTextSubtle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            Rectangle botR = new Rectangle(0, 13, this.Width, 13);
            using (Font fV = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, val, fV, botR, MainForm.ColorAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }


}