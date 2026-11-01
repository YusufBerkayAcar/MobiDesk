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
    public class ModernSegmentedMode : Control
    {
        private string text1;
        private string text2;
        private bool isExtend = true;
        private int hoveredIndex = -1;

        public bool IsExtend
        {
            get { return isExtend; }
            set { isExtend = value; Invalidate(); }
        }

        public void UpdateLabels(string t1, string t2)
        {
            this.text1 = t1;
            this.text2 = t2;
            Invalidate();
        }

        public delegate void ModeChangedHandler(bool isExtendMode);
        public event ModeChangedHandler ModeChanged;

        public ModernSegmentedMode(string t1, string t2, int x, int y, int w, int h)
        {
            this.text1 = t1;
            this.text2 = t2;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.BackColor = MainForm.ColorCard;
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            this.MouseMove += delegate(object s, MouseEventArgs e)
            {
                int newHov = (e.X < this.Width / 2) ? 0 : 1;
                if (newHov != hoveredIndex)
                {
                    hoveredIndex = newHov;
                    Invalidate();
                }
            };
            this.MouseLeave += delegate(object s, EventArgs e)
            {
                hoveredIndex = -1;
                Invalidate();
            };
            this.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    bool newExtend = (e.X < this.Width / 2);
                    if (newExtend != isExtend)
                    {
                        isExtend = newExtend;
                        Invalidate();
                        if (ModeChanged != null) ModeChanged(isExtend);
                    }
                }
            };
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

            // Dış Kaplama Tabanı
            Rectangle outer = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath op = RoundedPanel.CreateRoundedRect(outer, 8))
            {
                using (SolidBrush ob = new SolidBrush(Color.FromArgb(14, 18, 28)))
                {
                    g.FillPath(ob, op);
                }
                using (Pen open = new Pen(MainForm.ColorCardBorder, 1.0f))
                {
                    g.DrawPath(open, op);
                }
            }

            int gap = 3;
            int segW = (this.Width - gap * 3) / 2;
            int segH = this.Height - gap * 2;

            // 1. Sekme (Genişlet)
            Rectangle r1 = new Rectangle(gap, gap, segW, segH);
            DrawSegment(g, r1, text1, isExtend, hoveredIndex == 0);

            // 2. Sekme (Yinele)
            Rectangle r2 = new Rectangle(gap * 2 + segW, gap, segW, segH);
            DrawSegment(g, r2, text2, !isExtend, hoveredIndex == 1);
        }

        private void DrawSegment(Graphics g, Rectangle r, string text, bool active, bool hovered)
        {
            using (GraphicsPath p = RoundedPanel.CreateRoundedRect(r, 6))
            {
                if (active)
                {
                    using (LinearGradientBrush lgb = new LinearGradientBrush(r, MainForm.ColorPrimary, MainForm.ColorPrimaryDark, 90f))
                    {
                        g.FillPath(lgb, p);
                    }
                    using (Pen pen = new Pen(Color.FromArgb(129, 140, 248), 1.2f))
                    {
                        g.DrawPath(pen, p);
                    }
                }
                else
                {
                    if (hovered)
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(24, 32, 48)))
                        {
                            g.FillPath(b, p);
                        }
                    }
                }
            }

            Color fg = active ? Color.White : (hovered ? MainForm.ColorText : MainForm.ColorTextMuted);
            using (Font f = new Font("Segoe UI Emoji", 8.5f, active ? FontStyle.Bold : FontStyle.Regular))
            {
                TextRenderer.DrawText(g, text, f, r, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }


}