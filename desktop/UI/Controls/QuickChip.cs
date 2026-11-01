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
    public class QuickChip : Control
    {
        private bool isHovered = false;

        public QuickChip(string text, int x, int y, int w, int h)
        {
            this.Text = text;
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

            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            Color bg = isHovered ? Color.FromArgb(32, 42, 64) : Color.FromArgb(20, 26, 40);
            Color border = isHovered ? MainForm.ColorPrimary : Color.FromArgb(36, 46, 70);

            using (GraphicsPath p = RoundedPanel.CreateRoundedRect(rect, 4))
            {
                using (SolidBrush b = new SolidBrush(bg))
                {
                    g.FillPath(b, p);
                }
                using (Pen pen = new Pen(border, 1.0f))
                {
                    g.DrawPath(pen, p);
                }
            }

            Color fg = isHovered ? Color.White : MainForm.ColorTextMuted;
            using (Font f = new Font("Segoe UI", 7.0f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, this.Text, f, rect, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }


}