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
    public static class L10n
    {
        public static bool IsEnglish
        {
            get { return string.Equals(AppConfig.Lang, "en", StringComparison.OrdinalIgnoreCase); }
            set
            {
                AppConfig.Lang = value ? "en" : "tr";
                AppConfig.Save();
            }
        }

        public static void LoadConfig()
        {
            AppConfig.Load();
        }

        public static void SaveConfig()
        {
            AppConfig.Save();
        }

        public static string T(string tr, string en)
        {
            return IsEnglish ? en : tr;
        }
    }

}