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
    public enum TitleBtnType
    {
        Minimize,
        Maximize,
        Close
    }

    public class ModernTitleBar : Panel
    {
        private Form parentForm;
        private string appTitle;
        private string appBadge;
        private TitleBarButton btnMin;
        private TitleBarButton btnMax;
        private TitleBarButton btnClose;
        private LanguageToggle langToggle;

        public event Action<bool> LanguageChanged;

        public void SetLanguage(bool isEnglish)
        {
            if (langToggle != null)
            {
                langToggle.IsEnglish = isEnglish;
            }
        }

        public ModernTitleBar(Form parent, string title, string badge)
        {
            this.parentForm = parent;
            this.appTitle = title;
            this.appBadge = badge;
            this.BackColor = MainForm.ColorBg;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            langToggle = new LanguageToggle();
            langToggle.IsEnglish = L10n.IsEnglish;
            langToggle.LanguageChanged += delegate(bool english)
            {
                if (LanguageChanged != null)
                {
                    LanguageChanged(english);
                }
            };
            this.Controls.Add(langToggle);

            btnMin = new TitleBarButton(TitleBtnType.Minimize, parentForm) { Size = new Size(42, 32) };
            btnMin.Click += delegate(object s, EventArgs e) { parentForm.WindowState = FormWindowState.Minimized; };

            btnMax = new TitleBarButton(TitleBtnType.Maximize, parentForm) { Size = new Size(42, 32) };
            btnMax.Click += delegate(object s, EventArgs e)
            {
                if (parentForm.WindowState == FormWindowState.Maximized)
                    parentForm.WindowState = FormWindowState.Normal;
                else
                    parentForm.WindowState = FormWindowState.Maximized;
            };

            btnClose = new TitleBarButton(TitleBtnType.Close, parentForm) { Size = new Size(42, 32) };
            btnClose.Click += delegate(object s, EventArgs e) { parentForm.Close(); };

            this.Controls.Add(btnMin);
            this.Controls.Add(btnMax);
            this.Controls.Add(btnClose);

            this.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.Clicks == 1)
                {
                    MainForm.DragWindow(parentForm.Handle);
                }
            };

            this.DoubleClick += delegate(object s, EventArgs e)
            {
                if (parentForm != null)
                {
                    parentForm.WindowState = (parentForm.WindowState == FormWindowState.Maximized)
                        ? FormWindowState.Normal
                        : FormWindowState.Maximized;
                }
            };
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

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;
            if (m.Msg == WM_NCHITTEST && parentForm != null && parentForm.WindowState == FormWindowState.Normal)
            {
                short x = (short)(m.LParam.ToInt32() & 0xFFFF);
                short y = (short)((m.LParam.ToInt32() >> 16) & 0xFFFF);
                Point pt = this.PointToClient(new Point(x, y));
                if (pt.Y < 7 || pt.X < 7 || pt.X >= this.Width - 7)
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (btnClose != null && btnMax != null && btnMin != null)
            {
                int btnY = (this.Height - 32) / 2;
                btnClose.Location = new Point(this.Width - 44, btnY);
                btnMax.Location = new Point(this.Width - 88, btnY);
                btnMin.Location = new Point(this.Width - 132, btnY);
                btnMax.Invalidate();
            }
            if (langToggle != null)
            {
                int langY = (this.Height - 22) / 2;
                langToggle.Location = new Point(this.Width - 206, langY);
            }
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush bgBrush = new SolidBrush(MainForm.ColorBg))
            {
                g.FillRectangle(bgBrush, this.ClientRectangle);
            }

            // Alt ayrım çizgisi
            using (Pen p = new Pen(Color.FromArgb(24, 32, 48), 1.0f))
            {
                g.DrawLine(p, 0, this.Height - 1, this.Width, this.Height - 1);
            }

            // App Icon
            int iconY = (this.Height - 24) / 2;
            Rectangle iconBox = new Rectangle(14, iconY, 24, 24);
            Image logo = MainForm.GetAppLogoImage();
            if (logo != null)
            {
                InterpolationMode prevMode = g.InterpolationMode;
                SmoothingMode prevSmooth = g.SmoothingMode;
                PixelOffsetMode prevOffset = g.PixelOffsetMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (GraphicsPath ip = RoundedPanel.CreateRoundedRect(iconBox, 5))
                {
                    GraphicsState state = g.Save();
                    g.SetClip(ip);
                    g.DrawImage(logo, iconBox);
                    g.Restore(state);
                }

                g.InterpolationMode = prevMode;
                g.SmoothingMode = prevSmooth;
                g.PixelOffsetMode = prevOffset;
            }
            else
            {
                using (GraphicsPath ip = RoundedPanel.CreateRoundedRect(iconBox, 6))
                using (LinearGradientBrush lgb = new LinearGradientBrush(iconBox, MainForm.ColorPrimary, MainForm.ColorAccent, 45f))
                {
                    g.FillPath(lgb, ip);
                }
                using (Font mf = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, "M", mf, iconBox, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }

            // Başlık Metni
            using (Font titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(MainForm.ColorText))
            {
                int titleY = (this.Height - 18) / 2;
                g.DrawString(appTitle, titleFont, b, 44, titleY);
            }

            // Sürüm Rozeti (BETA)
            using (Font titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (Font bFont = new Font("Segoe UI", 7.0f, FontStyle.Bold))
            {
                int badgeX = 44 + (int)g.MeasureString(appTitle, titleFont).Width + 8;
                int badgeW = Math.Max(42, (int)g.MeasureString(appBadge, bFont).Width + 14);
                int badgeY = (this.Height - 16) / 2;
                Rectangle badgeRect = new Rectangle(badgeX, badgeY, badgeW, 16);
                using (GraphicsPath bp = RoundedPanel.CreateRoundedRect(badgeRect, 4))
                {
                    using (SolidBrush bgB = new SolidBrush(Color.FromArgb(28, 36, 56)))
                    {
                        g.FillPath(bgB, bp);
                    }
                    using (Pen bPen = new Pen(Color.FromArgb(45, 58, 90), 1.0f))
                    {
                        g.DrawPath(bPen, bp);
                    }
                }
                TextRenderer.DrawText(g, appBadge, bFont, badgeRect, MainForm.ColorAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }


}