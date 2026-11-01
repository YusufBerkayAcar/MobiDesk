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
    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColorTable()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected)
            {
                Rectangle r = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
                using (GraphicsPath path = RoundedPanel.CreateRoundedRect(r, 4))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(37, 99, 235)))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.FillPath(b, path);
                    }
                }
            }
            else
            {
                Rectangle r = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(17, 24, 39)))
                {
                    e.Graphics.FillRectangle(b, r);
                }
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle r = new Rectangle(8, e.Item.Height / 2, e.Item.Width - 16, 1);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(42, 54, 76)))
            {
                e.Graphics.FillRectangle(b, r);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Rectangle r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (Pen p = new Pen(Color.FromArgb(42, 54, 76), 1))
            {
                e.Graphics.DrawRectangle(p, r);
            }
        }
    }


}