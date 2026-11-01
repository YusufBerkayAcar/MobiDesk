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
    public class ModernSlider : Control
    {
        private int minimum;
        private int maximum;
        private int val;
        private bool isDragging = false;
        private bool isHovered = false;
        private Color colorStart;
        private Color colorEnd;

        public event EventHandler ValueChanged;

        public int Value
        {
            get { return val; }
            set
            {
                int clamped = Math.Max(minimum, Math.Min(maximum, value));
                if (clamped != val)
                {
                    val = clamped;
                    Invalidate();
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                }
            }
        }

        public ModernSlider(int min, int max, int defaultVal, Color cStart, Color cEnd)
        {
            this.minimum = min;
            this.maximum = max;
            this.val = defaultVal;
            this.colorStart = cStart;
            this.colorEnd = cEnd;
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

            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }

            int pad = 8;
            int trackH = 5;
            int trackY = (this.Height - trackH) / 2;
            int trackW = this.Width - pad * 2;
            if (trackW <= 0) return;

            // Arka İz
            Rectangle bgRect = new Rectangle(pad, trackY, trackW, trackH);
            using (GraphicsPath bp = RoundedPanel.CreateRoundedRect(bgRect, 2))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(28, 36, 52)))
                {
                    g.FillPath(b, bp);
                }
            }

            // Dolu İz
            float pct = (maximum > minimum) ? (float)(val - minimum) / (maximum - minimum) : 0f;
            int fillW = (int)(trackW * pct);
            if (fillW > 0)
            {
                Rectangle fillR = new Rectangle(pad, trackY, fillW, trackH);
                using (GraphicsPath fp = RoundedPanel.CreateRoundedRect(fillR, 2))
                {
                    using (LinearGradientBrush lgb = new LinearGradientBrush(bgRect, colorStart, colorEnd, 0f))
                    {
                        g.FillPath(lgb, fp);
                    }
                }
            }

            // Yuvarlak Tutamaç
            int thumbRadius = isHovered || isDragging ? 8 : 7;
            int thumbX = pad + (int)(trackW * pct);
            int thumbY = this.Height / 2;
            Rectangle thumbRect = new Rectangle(thumbX - thumbRadius, thumbY - thumbRadius, thumbRadius * 2, thumbRadius * 2);

            using (SolidBrush tb = new SolidBrush(Color.White))
            {
                g.FillEllipse(tb, thumbRect);
            }
            using (Pen tp = new Pen(colorStart, 2.5f))
            {
                g.DrawEllipse(tp, thumbRect);
            }
        }

        private void UpdateMouse(int mouseX)
        {
            int pad = 8;
            int trackW = this.Width - pad * 2;
            if (trackW <= 0) return;

            float ratio = (float)(mouseX - pad) / trackW;
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            int newVal = minimum + (int)Math.Round(ratio * (maximum - minimum));
            if (newVal != val)
            {
                val = newVal;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                this.Capture = true;
                UpdateMouse(e.X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (isDragging) UpdateMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (isDragging)
            {
                isDragging = false;
                this.Capture = false;
            }
        }
    }


}