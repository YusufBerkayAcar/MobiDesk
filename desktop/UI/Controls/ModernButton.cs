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
    public class ModernButton : Button
    {
        private Color baseColor;
        private Color textColor;
        private Color borderColor = Color.Empty;
        private bool isHovered = false;
        private bool isPressed = false;
        private int cornerRadius = 8;

        public ModernButton(string text, int x, int y, int w, int h, Color bg, Color fg, int radius = 8, Color? border = null)
        {
            this.Text = text;
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.baseColor = bg;
            this.textColor = fg;
            this.cornerRadius = radius;
            if (border.HasValue) this.borderColor = border.Value;
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.Font = new Font("Segoe UI Emoji", 8.5f, FontStyle.Bold);
            this.Cursor = Cursors.Hand;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            this.MouseEnter += delegate(object s, EventArgs e) { isHovered = true; this.Invalidate(); };
            this.MouseLeave += delegate(object s, EventArgs e) { isHovered = false; isPressed = false; this.Invalidate(); };
            this.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isPressed = true; this.Invalidate(); } };
            this.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { isPressed = false; this.Invalidate(); } };
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
        }

        public void SetColors(Color bg, Color fg, Color? border = null)
        {
            this.baseColor = bg;
            this.textColor = fg;
            if (border.HasValue) this.borderColor = border.Value;
            this.Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // Ebeveyn rengiyle temizle
            Color pBg = (this.Parent != null) ? this.Parent.BackColor : MainForm.ColorCard;
            using (SolidBrush b = new SolidBrush(pBg))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color pBg = (this.Parent != null) ? this.Parent.BackColor : MainForm.ColorCard;
            using (SolidBrush b = new SolidBrush(pBg))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            Color drawBg = baseColor;
            if (isPressed)
            {
                drawBg = ControlPaint.Dark(baseColor, 0.10f);
            }
            else if (isHovered)
            {
                drawBg = ControlPaint.Light(baseColor, 0.15f);
            }

            using (GraphicsPath path = RoundedPanel.CreateRoundedRect(rect, cornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(drawBg))
                {
                    g.FillPath(brush, path);
                }

                Color borderCol = borderColor.IsEmpty ? drawBg : (isHovered ? ControlPaint.Light(borderColor, 0.2f) : borderColor);
                using (Pen pen = new Pen(borderCol, 1.0f))
                {
                    g.DrawPath(pen, path);
                }
            }

            TextRenderer.DrawText(g, this.Text, this.Font, rect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }


}