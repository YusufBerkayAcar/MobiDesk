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
    public class ModernDarkScrollBar : Control
    {
        private int minimum = 0;
        private int maximum = 100;
        private int val = 0;
        private int smallChange = 40;
        private int largeChange = 100;

        private bool isHovered = false;
        private bool isDragging = false;
        private int dragStartY = 0;
        private int dragStartVal = 0;

        public event EventHandler ValueChanged;

        public ModernDarkScrollBar()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.UserPaint |
                          ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.ResizeRedraw |
                          ControlStyles.SupportsTransparentBackColor, true);
            this.DoubleBuffered = true;
            this.Width = 10;
            this.BackColor = MainForm.ColorBg;
            this.Cursor = Cursors.Default;
        }

        public int Minimum
        {
            get { return minimum; }
            set { minimum = value; Invalidate(); }
        }

        public int Maximum
        {
            get { return maximum; }
            set
            {
                maximum = value;
                this.Value = val;
                Invalidate();
            }
        }

        public int SmallChange
        {
            get { return smallChange; }
            set { smallChange = value; }
        }

        public int LargeChange
        {
            get { return largeChange; }
            set
            {
                largeChange = value;
                this.Value = val;
                Invalidate();
            }
        }

        public int Value
        {
            get { return val; }
            set
            {
                int maxPossible = Math.Max(minimum, maximum - largeChange);
                int clamped = Math.Max(minimum, Math.Min(maxPossible, value));
                if (val != clamped)
                {
                    val = clamped;
                    Invalidate();
                    if (ValueChanged != null)
                    {
                        ValueChanged(this, EventArgs.Empty);
                    }
                }
            }
        }

        private Rectangle GetThumbRect()
        {
            int trackH = this.Height;
            if (trackH <= 0) return Rectangle.Empty;

            int totalRange = Math.Max(1, maximum - minimum);
            int viewRange = Math.Min(totalRange, Math.Max(1, largeChange));
            float ratio = (float)viewRange / (float)totalRange;
            int thumbH = Math.Max(32, (int)(trackH * ratio));
            if (thumbH > trackH) thumbH = trackH;

            int maxVal = Math.Max(1, maximum - largeChange - minimum);
            int curOffset = val - minimum;
            int trackTravel = trackH - thumbH;
            int thumbY = (trackTravel > 0) ? (int)((float)curOffset / maxVal * trackTravel) : 0;
            thumbY = Math.Max(0, Math.Min(trackTravel, thumbY));

            return new Rectangle(1, thumbY, this.Width - 2, thumbH);
        }

        internal Rectangle GetThumbRectForTest()
        {
            return GetThumbRect();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!isDragging)
            {
                isHovered = false;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                Rectangle thumbRect = GetThumbRect();
                if (thumbRect.Contains(e.Location))
                {
                    isDragging = true;
                    this.Capture = true;
                    dragStartY = e.Y;
                    dragStartVal = val;
                }
                else
                {
                    if (e.Y < thumbRect.Top)
                    {
                        this.Value -= largeChange;
                    }
                    else if (e.Y > thumbRect.Bottom)
                    {
                        this.Value += largeChange;
                    }
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (isDragging)
            {
                Rectangle thumbRect = GetThumbRect();
                int trackTravel = this.Height - thumbRect.Height;
                if (trackTravel > 0)
                {
                    int maxVal = Math.Max(1, maximum - largeChange - minimum);
                    int deltaY = e.Y - dragStartY;
                    int deltaVal = (int)((float)deltaY / trackTravel * maxVal);
                    this.Value = dragStartVal + deltaVal;
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (isDragging)
            {
                this.Capture = false;
                isDragging = false;
                Point cursorP = this.PointToClient(Cursor.Position);
                isHovered = this.ClientRectangle.Contains(cursorP);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Arka plan / Track: İnce koyu ray
            int trackW = 4;
            int trackX = (this.Width - trackW) / 2;
            using (Brush trackBrush = new SolidBrush(Color.FromArgb(24, 33, 50)))
            {
                using (GraphicsPath trackPath = RoundedPanel.CreateRoundedRect(new Rectangle(trackX, 2, trackW, Math.Max(1, this.Height - 4)), 2))
                {
                    g.FillPath(trackBrush, trackPath);
                }
            }

            // Thumb: Modern hap (pill) biçiminde
            Rectangle thumbRect = GetThumbRect();
            if (thumbRect.Width > 0 && thumbRect.Height > 0)
            {
                Color thumbColor;
                if (isDragging)
                {
                    thumbColor = Color.FromArgb(59, 130, 246); // MobiDesk Royal Blue
                }
                else if (isHovered)
                {
                    thumbColor = Color.FromArgb(100, 116, 139); // Slate-500 hover
                }
                else
                {
                    thumbColor = Color.FromArgb(71, 85, 105);   // Slate-600 visible dark
                }

                int pillW = 6;
                int pillX = (this.Width - pillW) / 2;
                Rectangle pillRect = new Rectangle(pillX, thumbRect.Y, pillW, thumbRect.Height);
                int radius = pillW / 2;

                using (Brush thumbBrush = new SolidBrush(thumbColor))
                {
                    using (GraphicsPath pillPath = RoundedPanel.CreateRoundedRect(pillRect, radius))
                    {
                        g.FillPath(thumbBrush, pillPath);
                    }
                }
            }
        }
    }


}