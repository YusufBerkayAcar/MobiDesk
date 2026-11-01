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
    public static class MobiPathResolver
    {
        private static readonly string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static readonly string[] searchRoots = new string[]
        {
            baseDir,
            Path.GetFullPath(Path.Combine(baseDir, "..")),
            Path.GetFullPath(Path.Combine(baseDir, "../..")),
            AppDomain.CurrentDomain.BaseDirectory
        };

        public static string GetProjectRoot()
        {
            foreach (string root in searchRoots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                if (Directory.Exists(Path.Combine(root, "drivers")) || Directory.Exists(Path.Combine(root, "assets")))
                    return root;
            }
            return baseDir;
        }

        public static string ResolveFile(string fileName, params string[] subDirCandidates)
        {
            if (File.Exists(fileName)) return Path.GetFullPath(fileName);

            string direct = Path.Combine(baseDir, fileName);
            if (File.Exists(direct)) return direct;

            string nameOnly = Path.GetFileName(fileName);

            foreach (string root in searchRoots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                string candidate = Path.Combine(root, fileName);
                if (File.Exists(candidate)) return candidate;

                if (subDirCandidates != null)
                {
                    foreach (string sub in subDirCandidates)
                    {
                        string subPath = Path.Combine(root, sub.Replace('/', Path.DirectorySeparatorChar), fileName);
                        if (File.Exists(subPath)) return subPath;
                        if (!string.IsNullOrEmpty(nameOnly) && nameOnly != fileName)
                        {
                            string subNameOnly = Path.Combine(root, sub.Replace('/', Path.DirectorySeparatorChar), nameOnly);
                            if (File.Exists(subNameOnly)) return subNameOnly;
                        }
                    }
                }

                string[] wellKnown = new string[]
                {
                    "drivers/display",
                    "drivers/audio",
                    "drivers/audio/vda",
                    "drivers/audio/VBCable",
                    "assets",
                    "assets/icons",
                    "assets/screenshots",
                    "desktop"
                };
                foreach (string sub in wellKnown)
                {
                    string p = Path.Combine(root, sub.Replace('/', Path.DirectorySeparatorChar), fileName);
                    if (File.Exists(p)) return p;
                    if (!string.IsNullOrEmpty(nameOnly) && nameOnly != fileName)
                    {
                        string pNameOnly = Path.Combine(root, sub.Replace('/', Path.DirectorySeparatorChar), nameOnly);
                        if (File.Exists(pNameOnly)) return pNameOnly;
                    }
                }
            }
            return direct;
        }

        public static string ResolveDirectory(string dirName, params string[] subDirCandidates)
        {
            if (Directory.Exists(dirName)) return Path.GetFullPath(dirName);

            string direct = Path.Combine(baseDir, dirName);
            if (Directory.Exists(direct)) return direct;

            foreach (string root in searchRoots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                string candidate = Path.Combine(root, dirName);
                if (Directory.Exists(candidate)) return candidate;

                if (subDirCandidates != null)
                {
                    foreach (string sub in subDirCandidates)
                    {
                        string subPath = Path.Combine(root, sub.Replace('/', Path.DirectorySeparatorChar), dirName);
                        if (Directory.Exists(subPath)) return subPath;
                    }
                }

                string[] wellKnown = new string[]
                {
                    "drivers/display",
                    "drivers/audio",
                    "drivers/audio/vda",
                    "assets",
                    "assets/icons"
                };
                foreach (string sub in wellKnown)
                {
                    string p = Path.Combine(root, sub.Replace('/', Path.DirectorySeparatorChar), dirName);
                    if (Directory.Exists(p)) return p;
                }
            }
            return direct;
        }

        public static string ResolveDisplayScript(string scriptName)
        {
            return ResolveFile(scriptName, "drivers/display", "drivers", "");
        }

        public static string ResolveAudioScript(string scriptName)
        {
            return ResolveFile(scriptName, "drivers/audio", "drivers", "");
        }

        public static string ResolveAsset(string assetName)
        {
            return ResolveFile(assetName, "assets/icons", "assets", "assets/screenshots", "");
        }
    }

}