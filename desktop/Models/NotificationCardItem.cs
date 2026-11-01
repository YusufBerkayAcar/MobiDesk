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
    public class NotificationCardItem
    {
        public int Id { get; set; }
        public string Key { get; set; }
        public string PackageName { get; set; }
        public string AppName { get; set; }
        public string Title { get; set; }
        public string Text { get; set; }
        public long Timestamp { get; set; }
        public string IconBase64 { get; set; }
        public Image CachedIcon { get; set; }
        public string DeviceSerial { get; set; }
        public string DeviceName { get; set; }
    }

}