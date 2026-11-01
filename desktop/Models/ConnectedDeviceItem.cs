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
    public class ConnectedDeviceItem
    {
        public string Serial { get; set; }
        public string Model { get; set; }
        public string FriendlyName { get; set; }
        public int FlowPort { get; set; }

        public override string ToString()
        {
            return FriendlyName + " (" + Serial + ")";
        }
    }

}