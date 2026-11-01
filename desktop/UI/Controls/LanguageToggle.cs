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
    public class LanguageToggle : Control
    {
        private bool isEnglish = false;
        private int hoveredIndex = -1;

        public event Action<bool> LanguageChanged;

        public bool IsEnglish
        {
            get { return isEnglish; }
            set
            {
                if (isEnglish != value)
                {
                    isEnglish = value;
                    Invalidate();
                }
            }
        }

        public LanguageToggle()
        {
            this.Size = new Size(64, 22);
            this.BackColor = MainForm.ColorBg;
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
                    bool newEn = (e.X >= this.Width / 2);
                    if (newEn != isEnglish)
                    {
                        isEnglish = newEn;
                        Invalidate();
                        if (LanguageChanged != null)
                        {
                            LanguageChanged(isEnglish);
                        }
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

            using (SolidBrush b = new SolidBrush(MainForm.ColorBg))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }

            Rectangle r = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath p = RoundedPanel.CreateRoundedRect(r, 6))
            {
                using (SolidBrush bgPill = new SolidBrush(Color.FromArgb(20, 26, 40)))
                {
                    g.FillPath(bgPill, p);
                }
                using (Pen penBorder = new Pen(Color.FromArgb(42, 54, 76), 1.0f))
                {
                    g.DrawPath(penBorder, p);
                }
            }

            int halfW = this.Width / 2;
            Rectangle activeRect = isEnglish
                ? new Rectangle(halfW + 1, 2, halfW - 3, this.Height - 4)
                : new Rectangle(2, 2, halfW - 3, this.Height - 4);

            using (GraphicsPath activePath = RoundedPanel.CreateRoundedRect(activeRect, 4))
            {
                using (SolidBrush ab = new SolidBrush(MainForm.ColorPrimary))
                {
                    g.FillPath(ab, activePath);
                }
            }

            using (Font f = new Font("Segoe UI", 7.5f, FontStyle.Bold))
            {
                // TR
                Rectangle rTr = new Rectangle(0, 0, halfW, this.Height);
                Color trColor = !isEnglish ? Color.White : (hoveredIndex == 0 ? Color.FromArgb(226, 232, 240) : Color.FromArgb(148, 163, 184));
                using (SolidBrush sb = new SolidBrush(trColor))
                {
                    StringFormat sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString("TR", f, sb, rTr, sf);
                }

                // EN
                Rectangle rEn = new Rectangle(halfW, 0, halfW, this.Height);
                Color enColor = isEnglish ? Color.White : (hoveredIndex == 1 ? Color.FromArgb(226, 232, 240) : Color.FromArgb(148, 163, 184));
                using (SolidBrush sb = new SolidBrush(enColor))
                {
                    StringFormat sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString("EN", f, sb, rEn, sf);
                }
            }
        }
    }


}