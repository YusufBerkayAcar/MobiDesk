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
    public partial class MainForm : Form
    {
        private void BtnAddVirtual_Click(object sender, EventArgs e)
        {
            string scriptPath = MobiPathResolver.ResolveDisplayScript("Sanal_Ekran_Ekle.bat");
            if (!File.Exists(scriptPath))
            {
                MessageBox.Show(this,
                    L10n.T("Sanal_Ekran_Ekle.bat dosyası bulunamadı!", "Sanal_Ekran_Ekle.bat file not found!"),
                    L10n.T("Hata", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string oldText = btnAddVirtual != null ? btnAddVirtual.Text : "";
            try
            {
                if (btnAddVirtual != null)
                {
                    btnAddVirtual.Enabled = false;
                    btnAddVirtual.Text = L10n.T("⏳ Ekleniyor...", "⏳ Adding...");
                    btnAddVirtual.Refresh();
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"\"" + scriptPath + "\" --silent\"",
                    WorkingDirectory = Path.GetDirectoryName(scriptPath),
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = true
                };

                Process p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit();
                }

                Thread.Sleep(1200);
                RefreshScreenList();

                using (VirtualDisplayInfoModal modal = new VirtualDisplayInfoModal(VirtualModalMode.InstallSuccess))
                {
                    modal.ShowDialog(this);
                }
            }
            catch (System.ComponentModel.Win32Exception wEx)
            {
                if (wEx.NativeErrorCode != 1223)
                {
                    MessageBox.Show(this,
                        L10n.T("İşlem hatası: ", "Operation error: ") + wEx.Message,
                        L10n.T("Hata", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    L10n.T("İşlem iptal edildi veya hata: ", "Operation cancelled or error: ") + ex.Message,
                    L10n.T("Bilgi", "Info"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (btnAddVirtual != null)
                {
                    btnAddVirtual.Enabled = true;
                    btnAddVirtual.Text = oldText;
                }
            }
        }

        private void BtnVirtualInfo_Click(object sender, EventArgs e)
        {
            using (VirtualDisplayInfoModal modal = new VirtualDisplayInfoModal(VirtualModalMode.GeneralInfo))
            {
                modal.ShowDialog(this);
            }
        }

        private void BtnRemoveVirtual_Click(object sender, EventArgs e)
        {
            string scriptPath = MobiPathResolver.ResolveDisplayScript("Sanal_Ekran_Kaldir.bat");
            if (!File.Exists(scriptPath))
            {
                MessageBox.Show(this,
                    L10n.T("Sanal_Ekran_Kaldir.bat dosyası bulunamadı!", "Sanal_Ekran_Kaldir.bat file not found!"),
                    L10n.T("Hata", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string oldText = btnRemoveVirtual != null ? btnRemoveVirtual.Text : "";
            try
            {
                if (btnRemoveVirtual != null)
                {
                    btnRemoveVirtual.Enabled = false;
                    btnRemoveVirtual.Text = L10n.T("⏳ Kaldırılıyor...", "⏳ Removing...");
                    btnRemoveVirtual.Refresh();
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"\"" + scriptPath + "\" --silent\"",
                    WorkingDirectory = Path.GetDirectoryName(scriptPath),
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = true
                };

                Process p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit();
                }

                Thread.Sleep(1000);
                RefreshScreenList();

                using (VirtualDisplayInfoModal modal = new VirtualDisplayInfoModal(VirtualModalMode.RemoveSuccess))
                {
                    modal.ShowDialog(this);
                }
            }
            catch (System.ComponentModel.Win32Exception wEx)
            {
                if (wEx.NativeErrorCode != 1223)
                {
                    MessageBox.Show(this,
                        L10n.T("İşlem hatası: ", "Operation error: ") + wEx.Message,
                        L10n.T("Hata", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    L10n.T("İşlem iptal edildi veya hata: ", "Operation cancelled or error: ") + ex.Message,
                    L10n.T("Bilgi", "Info"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (btnRemoveVirtual != null)
                {
                    btnRemoveVirtual.Enabled = true;
                    btnRemoveVirtual.Text = oldText;
                }
            }
        }

        private void TrySyncVirtualDisplayResolution(int w, int h)
        {
            try
            {
                Screen[] screens = Screen.AllScreens;
                if (selectedScreenIndex >= 0 && selectedScreenIndex < screens.Length)
                {
                    Screen s = screens[selectedScreenIndex];
                    if (IsVirtualDisplay(s.DeviceName))
                    {
                        if (s.Bounds.Width != w || s.Bounds.Height != h)
                        {
                            DEVMODE dm = new DEVMODE();
                            dm.dmSize = (short)Marshal.SizeOf(dm);
                            dm.dmPelsWidth = w;
                            dm.dmPelsHeight = h;
                            dm.dmFields = 0x00080000 | 0x00100000;

                            int res = ChangeDisplaySettingsEx(s.DeviceName, ref dm, IntPtr.Zero, 0x00000001, IntPtr.Zero);
                            if (res == 0)
                            {
                                this.BeginInvoke(new Action(delegate()
                                {
                                    RefreshScreenList();
                                }));
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
}