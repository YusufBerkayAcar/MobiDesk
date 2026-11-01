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
    public class TabletHeroBanner : Panel
    {
        private string deviceName;
        private string specs;
        private string statusText;
        private Color statusColor;
        private bool isPortrait = false;

        public TabletHeroBanner(string name, string specs, string status, Color? statusColor = null)
        {
            this.deviceName = name;
            this.specs = specs;
            this.statusText = status;
            this.statusColor = statusColor.HasValue ? statusColor.Value : MainForm.ColorTextSubtle;
            this.DoubleBuffered = true;
            this.BackColor = MainForm.ColorBg;
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
            this.Invalidate();
        }

        public void SetDevice(string name, string specs, bool portrait = false)
        {
            if (string.Equals(this.deviceName, name, StringComparison.Ordinal) &&
                string.Equals(this.specs, specs, StringComparison.Ordinal) &&
                this.isPortrait == portrait)
                return;
            this.deviceName = name;
            this.specs = specs;
            this.isPortrait = portrait;
            this.Invalidate();
        }

        public void SetStatus(string status, Color color)
        {
            if (string.Equals(this.statusText, status, StringComparison.Ordinal) &&
                this.statusColor == color)
                return;
            this.statusText = status;
            this.statusColor = color;
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
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush clearBrush = new SolidBrush(MainForm.ColorBg))
            {
                g.FillRectangle(clearBrush, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            // Banner arka planı ve degrade ışık vurumu
            using (GraphicsPath path = RoundedPanel.CreateRoundedRect(rect, 10))
            {
                using (LinearGradientBrush lgb = new LinearGradientBrush(rect, Color.FromArgb(20, 26, 40), Color.FromArgb(14, 18, 28), 90f))
                {
                    g.FillPath(lgb, path);
                }
                using (Pen borderPen = new Pen(MainForm.ColorCardBorder, 1.2f))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            // Sol Vektör Cihaz Çizimi (Telefon veya Tablet)
            if (isPortrait)
            {
                int phY = (this.Height - 34) / 2;
                Rectangle phoneRect = new Rectangle(16, phY, 18, 34);
                using (GraphicsPath pp = RoundedPanel.CreateRoundedRect(phoneRect, 4))
                {
                    using (Pen pPhone = new Pen(MainForm.ColorAccent, 1.5f))
                    {
                        g.DrawPath(pPhone, pp);
                    }
                }
                using (SolidBrush bSc = new SolidBrush(Color.FromArgb(30, 40, 60)))
                {
                    g.FillRectangle(bSc, 19, phY + 4, 12, 23);
                }
                using (SolidBrush bDot = new SolidBrush(MainForm.ColorAccent))
                {
                    g.FillRectangle(bDot, 22, phY + 1, 6, 2);
                    g.FillRectangle(bDot, 22, phY + 29, 6, 2);
                }
            }
            else
            {
                int tabY = (this.Height - 28) / 2;
                Rectangle tabRect = new Rectangle(14, tabY, 30, 28);
                using (GraphicsPath tp = RoundedPanel.CreateRoundedRect(tabRect, 4))
                {
                    using (Pen pTab = new Pen(MainForm.ColorAccent, 1.5f))
                    {
                        g.DrawPath(pTab, tp);
                    }
                }
                using (SolidBrush bSc = new SolidBrush(Color.FromArgb(30, 40, 60)))
                {
                    g.FillRectangle(bSc, 17, tabY + 3, 20, 22);
                }
                using (SolidBrush bDot = new SolidBrush(MainForm.ColorAccent))
                {
                    g.FillEllipse(bDot, 39, tabY + 12, 3, 3);
                }
            }

            // Cihaz Adı
            using (Font titleFont = new Font("Segoe UI", 10.0f, FontStyle.Bold))
            using (SolidBrush bTitle = new SolidBrush(MainForm.ColorText))
            {
                g.DrawString(deviceName, titleFont, bTitle, 54, 10);
            }

            // Özellikler ve Bağlantı Alt Metni
            using (Font subFont = new Font("Segoe UI", 8.2f))
            using (SolidBrush bSub = new SolidBrush(MainForm.ColorTextMuted))
            {
                g.DrawString(specs, subFont, bSub, 54, 32);
            }

            // Sağ Durum Rozeti (Pill)
            using (Font pillFont = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            {
                Size sz = TextRenderer.MeasureText(g, statusText, pillFont);
                int pillW = Math.Max(120, sz.Width + 34);
                int pillH = 26;
                Rectangle pillRect = new Rectangle(this.Width - pillW - 14, (this.Height - pillH) / 2, pillW, pillH);

                using (GraphicsPath pillPath = RoundedPanel.CreateRoundedRect(pillRect, pillH / 2))
                {
                    using (SolidBrush pillBg = new SolidBrush(Color.FromArgb(24, 30, 46)))
                    {
                        g.FillPath(pillBg, pillPath);
                    }
                    using (Pen pillBorder = new Pen(Color.FromArgb(36, 48, 72), 1.0f))
                    {
                        g.DrawPath(pillBorder, pillPath);
                    }
                }

                // Rozet İçi Canlı Nokta
                using (SolidBrush dotBrush = new SolidBrush(statusColor))
                {
                    g.FillEllipse(dotBrush, pillRect.X + 11, pillRect.Y + 9, 8, 8);
                }

                // Rozet Metni
                Rectangle txtRect = new Rectangle(pillRect.X + 23, pillRect.Y, pillRect.Width - 26, pillRect.Height);
                TextRenderer.DrawText(g, statusText, pillFont, txtRect, statusColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }
    }


}