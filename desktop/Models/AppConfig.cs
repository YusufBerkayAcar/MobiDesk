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
    public static class AppConfig
    {
        private static string ConfigFile = MobiPathResolver.ResolveFile("mobidesk.cfg");
        private static readonly object fileLock = new object();

        public static string Lang = "tr";
        public static int Fps = 60;
        public static int Quality = 65;
        public static bool AdaptiveFps = true;
        public static bool ShowFps = true;
        public static bool BatterySaver = false;
        public static bool IsExtendMode = true;
        public static string LastMonitorDevice = "";
        public static int WindowWidth = 860;
        public static int WindowHeight = 680;
        public static bool MockNotifications = false;
        public static bool EnableAudio = false;

        public static void Load()
        {
            lock (fileLock)
            {
                try
                {
                    if (File.Exists(ConfigFile))
                    {
                        string[] lines = File.ReadAllLines(ConfigFile);
                        foreach (string rawLine in lines)
                        {
                            string line = rawLine.Trim();
                            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                            int eqIdx = line.IndexOf('=');
                            if (eqIdx <= 0) continue;

                            string key = line.Substring(0, eqIdx).Trim().ToLowerInvariant();
                            string val = line.Substring(eqIdx + 1).Trim();

                            switch (key)
                            {
                                case "lang":
                                    Lang = val.ToLowerInvariant();
                                    break;
                                case "fps":
                                    int fpsVal;
                                    if (int.TryParse(val, out fpsVal))
                                        Fps = Math.Max(15, Math.Min(75, fpsVal));
                                    break;
                                case "quality":
                                    int qVal;
                                    if (int.TryParse(val, out qVal))
                                        Quality = Math.Max(15, Math.Min(100, qVal));
                                    break;
                                case "adaptive_fps":
                                    bool adaptVal;
                                    if (bool.TryParse(val, out adaptVal))
                                        AdaptiveFps = adaptVal;
                                    break;
                                case "show_fps":
                                    bool showFpsVal;
                                    if (bool.TryParse(val, out showFpsVal))
                                        ShowFps = showFpsVal;
                                    break;
                                case "battery_saver":
                                    bool battVal;
                                    if (bool.TryParse(val, out battVal))
                                        BatterySaver = battVal;
                                    break;
                                case "enable_audio":
                                case "audio":
                                    bool audVal;
                                    if (bool.TryParse(val, out audVal))
                                        EnableAudio = audVal;
                                    else if (val == "1")
                                        EnableAudio = true;
                                    else if (val == "0")
                                        EnableAudio = false;
                                    break;
                                case "mock":
                                case "mock_notifications":
                                    bool mockVal;
                                    if (bool.TryParse(val, out mockVal))
                                        MockNotifications = mockVal;
                                    break;
                                case "is_extend_mode":
                                case "mode":
                                    if (val.Equals("mirror", StringComparison.OrdinalIgnoreCase) || val.Equals("duplicate", StringComparison.OrdinalIgnoreCase) || val.Equals("false", StringComparison.OrdinalIgnoreCase))
                                        IsExtendMode = false;
                                    else
                                        IsExtendMode = true;
                                    break;
                                case "last_monitor_device":
                                case "monitor":
                                    LastMonitorDevice = val;
                                    break;
                                case "window_w":
                                    int w;
                                    if (int.TryParse(val, out w))
                                        WindowWidth = Math.Max(860, w);
                                    break;
                                case "window_h":
                                    int h;
                                    if (int.TryParse(val, out h))
                                        WindowHeight = Math.Max(680, h);
                                    break;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        public static void Save()
        {
            lock (fileLock)
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("lang=" + (Lang ?? "tr"));
                    sb.AppendLine("fps=" + Fps);
                    sb.AppendLine("quality=" + Quality);
                    sb.AppendLine("adaptive_fps=" + (AdaptiveFps ? "true" : "false"));
                    sb.AppendLine("show_fps=" + (ShowFps ? "true" : "false"));
                    sb.AppendLine("battery_saver=" + (BatterySaver ? "true" : "false"));
                    sb.AppendLine("enable_audio=" + (EnableAudio ? "true" : "false"));
                    sb.AppendLine("mode=" + (IsExtendMode ? "extend" : "mirror"));
                    sb.AppendLine("monitor=" + (LastMonitorDevice ?? ""));
                    sb.AppendLine("window_w=" + WindowWidth);
                    sb.AppendLine("window_h=" + WindowHeight);

                    File.WriteAllText(ConfigFile, sb.ToString());
                }
                catch { }
            }
        }
    }

}