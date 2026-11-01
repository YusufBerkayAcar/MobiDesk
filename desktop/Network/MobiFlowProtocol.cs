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
    public static class MobiFlowProtocol
    {
        public const int PORT = 55557;
        public static readonly byte[] MAGIC = new byte[] { 0x4D, 0x46 }; // "MF"

        public const short TYPE_TEXT = 0x0001;
        public const short TYPE_FILE_START = 0x0002;
        public const short TYPE_FILE_CHUNK = 0x0003;
        public const short TYPE_FILE_END = 0x0004;
        public const short TYPE_NOTIFICATION = 0x0005;
        public const short TYPE_NOTIFICATION_REMOVE = 0x0006;
        public const short TYPE_PING = 0x0007;
        public const short TYPE_PONG = 0x0008;
        public const short TYPE_DISMISS_NOTIFICATION = 0x0009;

        public static void WriteNetUTF(BinaryWriter bw, string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s ?? "");
            bw.Write(IPAddress.HostToNetworkOrder((short)bytes.Length));
            bw.Write(bytes);
        }

        public static string ReadNetUTF(BinaryReader br)
        {
            short len = IPAddress.NetworkToHostOrder(br.ReadInt16());
            if (len <= 0) return "";
            byte[] bytes = br.ReadBytes(len);
            return Encoding.UTF8.GetString(bytes);
        }

        public static void WriteNetInt64(BinaryWriter bw, long val)
        {
            bw.Write(IPAddress.HostToNetworkOrder(val));
        }

        public static long ReadNetInt64(BinaryReader br)
        {
            return IPAddress.NetworkToHostOrder(br.ReadInt64());
        }

        public static void WriteNetInt32(BinaryWriter bw, int val)
        {
            bw.Write(IPAddress.HostToNetworkOrder(val));
        }

        public static int ReadNetInt32(BinaryReader br)
        {
            return IPAddress.NetworkToHostOrder(br.ReadInt32());
        }
    }

}