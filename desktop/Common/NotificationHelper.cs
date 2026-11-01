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
    public static class NotificationHelper
    {
        private static readonly Dictionary<string, string> KnownAppNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "com.google.android.gm", "Gmail" },
            { "com.google.android.youtube", "YouTube" },
            { "com.google.android.apps.messaging", "Google Mesajlar" },
            { "com.google.android.dialer", "Telefon" },
            { "com.google.android.calendar", "Google Takvim" },
            { "com.google.android.apps.photos", "Google Fotoğraflar" },
            { "com.google.android.apps.maps", "Google Haritalar" },
            { "com.android.chrome", "Chrome" },
            { "com.android.vending", "Google Play Store" },
            { "com.whatsapp", "WhatsApp" },
            { "com.whatsapp.w4b", "WhatsApp Business" },
            { "com.instagram.android", "Instagram" },
            { "org.telegram.messenger", "Telegram" },
            { "com.twitter.android", "X" },
            { "com.x.android", "X" },
            { "com.spotify.music", "Spotify" },
            { "com.discord", "Discord" },
            { "com.samsung.android.messaging", "Samsung Mesajlar" },
            { "com.samsung.android.dialer", "Telefon" },
            { "com.samsung.android.incallui", "Arama" },
            { "com.sec.android.app.sbrowser", "Samsung Internet" },
            { "com.sec.android.gallery3d", "Galeri" },
            { "com.sec.android.app.camera", "Kamera" },
            { "com.samsung.android.app.notes", "Samsung Notes" },
            { "com.samsung.android.app.smartcapture", "Samsung yakalama" },
            { "com.microsoft.office.outlook", "Outlook" },
            { "com.microsoft.teams", "Microsoft Teams" },
            { "com.netflix.mediaclient", "Netflix" },
            { "com.facebook.katana", "Facebook" },
            { "com.facebook.orca", "Messenger" }
        };

        public static string ResolveAppName(string appName, string packageName)
        {
            if (!string.IsNullOrEmpty(packageName))
            {
                string known;
                if (KnownAppNames.TryGetValue(packageName, out known))
                {
                    return known;
                }
            }

            if (string.IsNullOrEmpty(appName) ||
                string.Equals(appName, packageName, StringComparison.OrdinalIgnoreCase) ||
                appName.StartsWith("com.") || appName.StartsWith("org.") || appName.StartsWith("net."))
            {
                if (!string.IsNullOrEmpty(packageName))
                {
                    string[] parts = packageName.Split('.');
                    if (parts.Length > 0)
                    {
                        string candidate = parts[parts.Length - 1];
                        if (candidate.Length > 2)
                        {
                            return char.ToUpper(candidate[0]) + candidate.Substring(1);
                        }
                    }
                }
            }

            return !string.IsNullOrEmpty(appName) ? appName : (!string.IsNullOrEmpty(packageName) ? packageName : "Bildirim");
        }

        public static Bitmap GetAppIconOrBadge(string packageName, string appName)
        {
            Bitmap bmp = new Bitmap(36, 36);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                string pkg = (packageName ?? "").ToLowerInvariant();
                string app = (appName ?? "").ToLowerInvariant();

                Color bgCol = Color.FromArgb(28, 38, 58);
                Color borderCol = Color.FromArgb(45, 60, 90);
                Color glyphCol = Color.White;
                string symbol = "";

                if (pkg.Contains(".gm") || pkg.Contains("gmail") || app == "gmail")
                {
                    bgCol = Color.FromArgb(234, 67, 53); // Gmail Red
                    symbol = "✉";
                }
                else if (pkg.Contains("whatsapp") || app.Contains("whatsapp"))
                {
                    bgCol = Color.FromArgb(37, 211, 102); // WhatsApp Green
                    symbol = "💬";
                }
                else if (pkg.Contains("youtube") || app.Contains("youtube"))
                {
                    bgCol = Color.FromArgb(255, 0, 0); // YouTube Red
                    symbol = "▶";
                }
                else if (pkg.Contains("telegram") || app.Contains("telegram"))
                {
                    bgCol = Color.FromArgb(34, 158, 217); // Telegram Blue
                    symbol = "✈";
                }
                else if (pkg.Contains("instagram") || app.Contains("instagram"))
                {
                    bgCol = Color.FromArgb(225, 48, 108); // Instagram Magenta
                    symbol = "📷";
                }
                else if (pkg.Contains("spotify") || app.Contains("spotify"))
                {
                    bgCol = Color.FromArgb(30, 215, 96); // Spotify Green
                    symbol = "♫";
                }
                else if (pkg.Contains("smartcapture") || app.Contains("yakalama"))
                {
                    bgCol = Color.FromArgb(124, 77, 255); // Purple
                    symbol = "📸";
                }
                else
                {
                    string resolved = ResolveAppName(appName, packageName);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        symbol = resolved.Substring(0, 1).ToUpper();
                    }
                    else
                    {
                        symbol = "🔔";
                    }
                }

                Rectangle rect = new Rectangle(0, 0, 35, 35);
                using (GraphicsPath path = RoundedPanel.CreateRoundedRect(rect, 8))
                {
                    using (SolidBrush b = new SolidBrush(bgCol))
                    {
                        g.FillPath(b, path);
                    }
                    using (Pen p = new Pen(borderCol, 1f))
                    {
                        g.DrawPath(p, path);
                    }
                }

                if (!string.IsNullOrEmpty(symbol))
                {
                    float fontSize = (symbol.Length == 1 && char.IsLetter(symbol[0])) ? 13.0f : 12.0f;
                    using (Font f = new Font("Segoe UI", fontSize, FontStyle.Bold))
                    {
                        SizeF sz = g.MeasureString(symbol, f);
                        float x = (36 - sz.Width) / 2f;
                        float y = (36 - sz.Height) / 2f;
                        using (SolidBrush textBrush = new SolidBrush(glyphCol))
                        {
                            g.DrawString(symbol, f, textBrush, x, y);
                        }
                    }
                }
            }
            return bmp;
        }
    }

}