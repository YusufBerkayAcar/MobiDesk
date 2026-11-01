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
    public static class MiniJson
    {
        public static Dictionary<string, string> Parse(string json)
        {
            Dictionary<string, string> dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(json)) return dict;
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex("\"(?<key>[^\"]+)\"\\s*:\\s*(\"(?<val>(\\\\.|[^\"])*)\"|(?<val>[^,}\\]]+))");
            System.Text.RegularExpressions.MatchCollection matches = regex.Matches(json);
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                string val = m.Groups["val"].Value.Trim();
                val = val.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\/", "/").Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t");
                dict[m.Groups["key"].Value] = val;
            }
            return dict;
        }

        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }
    }

}