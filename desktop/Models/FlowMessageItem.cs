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
    public class FlowMessageItem
    {
        public string Id { get; set; }
        public string Sender { get; set; } // "pc" or "mobile"
        public string Text { get; set; }
        public long Timestamp { get; set; }
        public bool IsFile { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string FilePath { get; set; }
        public int Progress { get; set; }
        public string DeviceSerial { get; set; }
        public string DeviceName { get; set; }

        public FlowMessageItem()
        {
            Progress = 100;
        }
    }

}