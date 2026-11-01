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
    public static class WindowsStartupManager
    {
        private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "MobiDesk";

        public static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY, false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(APP_NAME);
                        return val != null;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log("IsStartupEnabled error: " + ex.Message);
            }
            return false;
        }

        public static bool SetStartup(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string exePath = Application.ExecutablePath;
                            string cmd = "\"" + exePath + "\" --minimized";
                            key.SetValue(APP_NAME, cmd);
                            Program.Log("Startup enabled in registry: " + cmd);
                        }
                        else
                        {
                            key.DeleteValue(APP_NAME, false);
                            Program.Log("Startup disabled in registry");
                        }
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Log("SetStartup error: " + ex.Message);
            }
            return false;
        }
    }

}