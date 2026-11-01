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
    public class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(17, 24, 39); } }
        public override Color MenuBorder { get { return Color.FromArgb(42, 54, 76); } }
        public override Color MenuItemBorder { get { return Color.Transparent; } }
        public override Color MenuItemSelected { get { return Color.FromArgb(37, 99, 235); } }
        public override Color MenuStripGradientBegin { get { return Color.FromArgb(17, 24, 39); } }
        public override Color MenuStripGradientEnd { get { return Color.FromArgb(17, 24, 39); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(17, 24, 39); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(17, 24, 39); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(17, 24, 39); } }
    }


}