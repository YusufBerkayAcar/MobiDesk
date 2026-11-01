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
    public class TitleBarButton : Control
    {
        private TitleBtnType btnType;
        private Form parentForm;
        private bool isHovered = false;
        private bool isPressed = false;

        public TitleBarButton(TitleBtnType type, Form parent = null)
        {
            this.btnType = type;
            this.parentForm = parent;
            this.BackColor = MainForm.ColorBg;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.Cursor = Cursors.Default;

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; isPressed = false; Invalidate(); };
            this.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isPressed = true; Invalidate(); } };
            this.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isPressed = false; Invalidate(); } };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
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

            using (SolidBrush b = new SolidBrush(MainForm.ColorBg))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }

            Color bg = Color.Transparent;
            Color fg = MainForm.ColorTextMuted;

            if (btnType == TitleBtnType.Close)
            {
                if (isPressed) { bg = Color.FromArgb(185, 28, 28); fg = Color.White; }
                else if (isHovered) { bg = Color.FromArgb(220, 38, 38); fg = Color.White; }
            }
            else
            {
                if (isPressed) { bg = Color.FromArgb(38, 48, 70); fg = Color.White; }
                else if (isHovered) { bg = Color.FromArgb(28, 36, 54); fg = Color.White; }
            }

            if (bg != Color.Transparent)
            {
                Rectangle r = new Rectangle(0, 0, this.Width, this.Height);
                using (GraphicsPath gp = RoundedPanel.CreateRoundedRect(r, 6))
                {
                    using (SolidBrush b = new SolidBrush(bg))
                    {
                        g.FillPath(b, gp);
                    }
                }
            }

            int cx = this.Width / 2;
            int cy = this.Height / 2;
            using (Pen pen = new Pen(fg, 1.4f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (btnType == TitleBtnType.Close)
                {
                    g.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy + 4);
                    g.DrawLine(pen, cx - 4, cy + 4, cx + 4, cy - 4);
                }
                else if (btnType == TitleBtnType.Minimize)
                {
                    g.DrawLine(pen, cx - 5, cy, cx + 5, cy);
                }
                else if (btnType == TitleBtnType.Maximize)
                {
                    bool isMaximized = (parentForm != null && parentForm.WindowState == FormWindowState.Maximized);
                    if (isMaximized)
                    {
                        // Restore icon: iki iç içe pencere
                        Rectangle r1 = new Rectangle(cx - 3, cy - 5, 8, 7);
                        Rectangle r2 = new Rectangle(cx - 5, cy - 3, 8, 7);
                        g.DrawRectangle(pen, r1);
                        using (SolidBrush fillBg = new SolidBrush(bg != Color.Transparent ? bg : MainForm.ColorBg))
                        {
                            g.FillRectangle(fillBg, r2.X + 1, r2.Y + 1, r2.Width - 1, r2.Height - 1);
                        }
                        g.DrawRectangle(pen, r2);
                    }
                    else
                    {
                        // Maximize icon: tek zarif kare
                        Rectangle r = new Rectangle(cx - 5, cy - 5, 10, 10);
                        g.DrawRectangle(pen, r);
                    }
                }
            }
        }
    }


}