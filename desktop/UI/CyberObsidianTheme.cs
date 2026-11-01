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
    public static class CyberObsidianTheme
    {
        public static readonly Color Bg          = Color.FromArgb(11, 14, 22);             // #0B0E16 - Derin obsidian
        public static readonly Color Card        = Color.FromArgb(17, 22, 34);           // #111622 - Kart gövdesi
        public static readonly Color CardBorder  = Color.FromArgb(30, 39, 60);     // #1E273C - Zarif kenarlık
        public static readonly Color CardHover   = Color.FromArgb(23, 30, 46);      // #171E2E
        public static readonly Color Primary     = Color.FromArgb(99, 102, 241);      // #6366F1 - Parlak Indigo
        public static readonly Color PrimaryDark = Color.FromArgb(79, 70, 229);   // #4F46E5
        public static readonly Color Accent      = Color.FromArgb(6, 182, 212);        // #06B6D4 - Elektrik Camgöbeği
        public static readonly Color Success     = Color.FromArgb(16, 185, 129);      // #10B981 - Canlı Zümrüt
        public static readonly Color SuccessDark = Color.FromArgb(5, 150, 105);   // #059669
        public static readonly Color Danger      = Color.FromArgb(239, 68, 68);        // #EF4444 - Mercan Kırmızı
        public static readonly Color DangerDark  = Color.FromArgb(190, 18, 60);    // #BE123C
        public static readonly Color Warning     = Color.FromArgb(245, 158, 11);      // #F59E0B - Amber
        public static readonly Color Text        = Color.FromArgb(248, 250, 252);        // #F8FAFC - Net beyaz
        public static readonly Color TextMuted   = Color.FromArgb(148, 163, 184);   // #94A3B8 - Açık arduvaz
        public static readonly Color TextSubtle  = Color.FromArgb(100, 116, 139);  // #64748B - Koyu arduvaz
    }
}