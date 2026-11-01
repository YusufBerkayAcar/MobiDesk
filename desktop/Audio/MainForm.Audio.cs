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
        private void BtnAddAudio_Click(object sender, EventArgs e)
        {
            string oldText = btnAddAudio != null ? btnAddAudio.Text : "";
            try
            {
                if (btnAddAudio != null)
                {
                    btnAddAudio.Enabled = false;
                    btnAddAudio.Text = L10n.T("⏳ Ekleniyor...", "⏳ Adding...");
                    btnAddAudio.Refresh();
                }

                RunAudioScript("Sanal_Ses_Ekle.bat", delegate
                {
                    using (VirtualAudioInfoModal modal = new VirtualAudioInfoModal(VirtualAudioModalMode.InstallSuccess))
                    {
                        modal.ShowDialog(this);
                    }
                });
            }
            finally
            {
                if (btnAddAudio != null)
                {
                    btnAddAudio.Enabled = true;
                    btnAddAudio.Text = oldText;
                }
            }
        }

        private void BtnRemoveAudio_Click(object sender, EventArgs e)
        {
            string oldText = btnRemoveAudio != null ? btnRemoveAudio.Text : "";
            try
            {
                if (btnRemoveAudio != null)
                {
                    btnRemoveAudio.Enabled = false;
                    btnRemoveAudio.Text = L10n.T("⏳ Kaldırılıyor...", "⏳ Removing...");
                    btnRemoveAudio.Refresh();
                }

                RunAudioScript("Sanal_Ses_Kaldir.bat", delegate
                {
                    using (VirtualAudioInfoModal modal = new VirtualAudioInfoModal(VirtualAudioModalMode.RemoveSuccess))
                    {
                        modal.ShowDialog(this);
                    }
                });
            }
            finally
            {
                if (btnRemoveAudio != null)
                {
                    btnRemoveAudio.Enabled = true;
                    btnRemoveAudio.Text = oldText;
                }
            }
        }

        private void BtnAudioInfo_Click(object sender, EventArgs e)
        {
            using (VirtualAudioInfoModal modal = new VirtualAudioInfoModal(VirtualAudioModalMode.GeneralInfo))
            {
                modal.ShowDialog(this);
            }
        }

        private void RunAudioScript(string scriptFileName, Action onSuccess)
        {
            string scriptPath = MobiPathResolver.ResolveAudioScript(scriptFileName);
            if (!File.Exists(scriptPath))
            {
                MessageBox.Show(this,
                    L10n.T(scriptFileName + " dosyası bulunamadı!", scriptFileName + " file not found!"),
                    L10n.T("Hata", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"" + scriptPath + "\"",
                    WorkingDirectory = Path.GetDirectoryName(scriptPath),
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Normal,
                    CreateNoWindow = false,
                    UseShellExecute = true
                };

                using (Process p = Process.Start(psi))
                {
                    if (p != null) p.WaitForExit();
                }

                Thread.Sleep(1500);

                if (scriptFileName.IndexOf("Ekle", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    IMMDevice dev = FindMobiDeskAudioDevice();
                    if (dev != null)
                    {
                        if (onSuccess != null) onSuccess();
                    }
                    else
                    {
                        MessageBox.Show(this,
                            L10n.T("Sanal ses aygıtı sistemde henüz algılanamadı.\n\nEğer ekranda kurulum sihirbazı açıldıysa lütfen 'Install Driver' butonuna tıklayarak kurulumu onaylayınız.\nArdından Windows Ses Çıkış ayarlarından 'MobiDesk Audio' veya 'CABLE Input' aygıtını seçebilirsiniz.",
                                   "Virtual audio device not detected yet.\n\nIf the setup window appeared, please click 'Install Driver' to complete installation.\nThen select 'MobiDesk Audio' or 'CABLE Input' in Windows Sound Settings."),
                            L10n.T("Bilgi", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    if (onSuccess != null) onSuccess();
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
        }

        private void StartAudioStreamingWorker()
        {
            lock (this)
            {
                if (isAudioStreaming) return;
                isAudioStreaming = true;
                audioStreamThread = new Thread(AudioStreamingWorker)
                {
                    IsBackground = true,
                    Name = "MobiDeskAudioStreamingWorker",
                    Priority = ThreadPriority.AboveNormal
                };
                audioStreamThread.Start();
            }
        }

        private void StopAudioStreamingWorker()
        {
            lock (this)
            {
                isAudioStreaming = false;
                if (audioStreamThread != null)
                {
                    try { audioStreamThread.Interrupt(); } catch { }
                    audioStreamThread = null;
                }
            }
        }

        [DllImport("ole32.dll")]
        internal static extern int PropVariantClear(ref PropVariant pvar);

        internal static IMMDevice FindMobiDeskAudioDevice()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                IMMDeviceCollection col;
                // eRender = 0, DEVICE_STATE_ACTIVE = 1
                int hr = enumerator.EnumAudioEndpoints(0, 1, out col);
                if (hr != 0 || col == null) return null;

                uint count;
                col.GetCount(out count);
                var keyFriendlyName = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
                var keyDeviceDesc = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);

                IMMDevice fallbackDev = null;

                for (uint i = 0; i < count; i++)
                {
                    IMMDevice dev = null;
                    hr = col.Item(i, out dev);
                    if (hr != 0 || dev == null) continue;

                    IPropertyStore store = null;
                    hr = dev.OpenPropertyStore(0, out store);
                    if (hr != 0 || store == null) continue;

                    string name = null;
                    string desc = null;

                    PropVariant pv = new PropVariant();
                    int hrVal1 = store.GetValue(ref keyFriendlyName, out pv);
                    if (hrVal1 == 0 && pv.vt == 31 && pv.pwszVal != IntPtr.Zero)
                    {
                        name = Marshal.PtrToStringUni(pv.pwszVal);
                    }
                    PropVariantClear(ref pv);

                    PropVariant pvDesc = new PropVariant();
                    int hrVal2 = store.GetValue(ref keyDeviceDesc, out pvDesc);
                    if (hrVal2 == 0 && pvDesc.vt == 31 && pvDesc.pwszVal != IntPtr.Zero)
                    {
                        desc = Marshal.PtrToStringUni(pvDesc.pwszVal);
                    }
                    PropVariantClear(ref pvDesc);

                    // Strictly ignore multi-channel 16ch pro audio endpoint to prevent wrong capture
                    bool is16ch = (!string.IsNullOrEmpty(name) && name.IndexOf("16ch", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                  (!string.IsNullOrEmpty(desc) && desc.IndexOf("16ch", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (is16ch) continue;

                    bool isMobiDesk = (!string.IsNullOrEmpty(name) && name.IndexOf("MobiDesk Audio", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                     (!string.IsNullOrEmpty(desc) && desc.IndexOf("MobiDesk Audio", StringComparison.OrdinalIgnoreCase) >= 0);

                    bool isCableInput = (!string.IsNullOrEmpty(name) && name.IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                        (!string.IsNullOrEmpty(desc) && desc.IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0);

                    bool isGenericCable = (!string.IsNullOrEmpty(name) && name.IndexOf("VB-Audio", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                          (!string.IsNullOrEmpty(desc) && desc.IndexOf("VB-Audio", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (isMobiDesk)
                    {
                        Program.Log("Found dedicated MobiDesk Audio device: " + (name ?? desc));
                        return dev;
                    }

                    if (isCableInput)
                    {
                        Program.Log("Found standard stereo CABLE Input: " + (name ?? desc));
                        return dev;
                    }

                    if (isGenericCable && fallbackDev == null)
                    {
                        fallbackDev = dev;
                    }
                }

                if (fallbackDev != null)
                {
                    Program.Log("Found generic VB-Audio CABLE endpoint for MobiDesk streaming.");
                    return fallbackDev;
                }
            }
            catch (Exception ex)
            {
                Program.Log("FindMobiDeskAudioDevice error: " + ex.Message);
            }
            return null; // STRICT ZERO FALLBACK: NEVER FALL BACK TO DEFAULT PLAYBACK DEVICE!
        }

        private void ShowAudioDriverMissingWarning()
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(ShowAudioDriverMissingWarning));
                return;
            }
            MessageBox.Show(
                this,
                L10n.T(
                    "'MobiDesk Audio' sanal ses kartı sistemde bulunamadı.\n\nSes yayını yapabilmek için lütfen 'Sanal Ses Ayarla' butonuna tıklayarak sürücüyü yükleyin.",
                    "'MobiDesk Audio' virtual audio device was not found on your system.\n\nTo stream audio, please install the driver using the 'Setup Virtual Audio' button."
                ),
                L10n.T("MobiDesk Audio Bulunamadı", "MobiDesk Audio Not Found"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }

        private void AudioStreamingWorker()
        {
            const int AUDIO_PORT = 55556;
            string adbPath = FindAdbPath();

            while (isStreaming && isAudioStreaming && AppConfig.EnableAudio)
            {
                RunAdbForwardPort(adbPath, activeTargetSerial, AUDIO_PORT, AUDIO_PORT);

                TcpClient client = null;
                while (isStreaming && isAudioStreaming && AppConfig.EnableAudio)
                {
                    try
                    {
                        client = new TcpClient();
                        client.NoDelay = true;
                        client.SendBufferSize = 64 * 1024;
                        client.Connect("127.0.0.1", AUDIO_PORT);
                        break;
                    }
                    catch
                    {
                        try { if (client != null) client.Close(); } catch { }
                        client = null;
                        Thread.Sleep(800);
                    }
                }

                if (client == null || !client.Connected) continue;

                // Enumerate for 'MobiDesk Audio' with ZERO FALLBACK
                IMMDevice dev = FindMobiDeskAudioDevice();
                if (dev == null)
                {
                    Program.Log("AudioStreamingWorker: 'MobiDesk Audio' endpoint missing. ZERO FALLBACK - aborting audio stream.");
                    try { client.Close(); } catch { }
                    ShowAudioDriverMissingWarning();
                    isAudioStreaming = false;
                    break;
                }

                Guid iidAudioClient = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
                object objClient;
                int hr = dev.Activate(ref iidAudioClient, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out objClient);
                if (hr != 0 || objClient == null)
                {
                    try { client.Close(); } catch { }
                    Thread.Sleep(1000);
                    continue;
                }

                IAudioClient audioClient = (IAudioClient)objClient;
                IntPtr pFormat;
                hr = audioClient.GetMixFormat(out pFormat);
                if (hr != 0 || pFormat == IntPtr.Zero)
                {
                    try { client.Close(); } catch { }
                    Thread.Sleep(1000);
                    continue;
                }

                Guid sessionGuid = Guid.Empty;
                hr = audioClient.Initialize(0 /* SHARED */, 0x00020000 /* AUDCLNT_STREAMFLAGS_LOOPBACK */, 10000000, 0, pFormat, ref sessionGuid);
                if (hr != 0)
                {
                    Program.Log("AudioStreamingWorker: audioClient.Initialize failed hr=0x" + hr.ToString("X8"));
                    try { client.Close(); } catch { }
                    Thread.Sleep(1000);
                    continue;
                }

                Guid iidCapture = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
                object objCapture;
                hr = audioClient.GetService(ref iidCapture, out objCapture);
                if (hr != 0 || objCapture == null)
                {
                    try { client.Close(); } catch { }
                    Thread.Sleep(1000);
                    continue;
                }

                IAudioCaptureClient captureClient = (IAudioCaptureClient)objCapture;
                audioClient.Start();
                Program.Log("AudioStreamingWorker: Started WASAPI loopback capture from MobiDesk Audio.");

                string cableDevId = null;
                try { dev.GetId(out cableDevId); } catch { }

                Guid iidVol = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
                object objCableVol = null;
                IAudioEndpointVolume epVol = null;
                try
                {
                    if (dev.Activate(ref iidVol, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out objCableVol) == 0 && objCableVol != null)
                    {
                        epVol = objCableVol as IAudioEndpointVolume;
                    }
                }
                catch { }

                IMMDeviceEnumerator devEnum = null;
                try { devEnum = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject(); } catch { }

                IMMDevice defDev = null;
                IAudioEndpointVolume defEpVol = null;
                string defDevId = null;
                int defaultCheckCounter = 0;

                NetworkStream netStream = client.GetStream();
                BinaryWriter bw = new BinaryWriter(netStream);
                byte[] pcmBuffer = new byte[1920 * 4];

                try
                {
                    while (isStreaming && isAudioStreaming && AppConfig.EnableAudio && client.Connected)
                    {
                        uint packetSize;
                        hr = captureClient.GetNextPacketSize(out packetSize);
                        if (hr != 0 || packetSize == 0)
                        {
                            Thread.Sleep(2);
                            continue;
                        }

                        // Dynamically refresh default audio endpoint every ~500ms so volume keys/default device changes reflect immediately
                        defaultCheckCounter++;
                        if (defaultCheckCounter >= 50 && devEnum != null)
                        {
                            defaultCheckCounter = 0;
                            try
                            {
                                IMMDevice curDefDev = null;
                                if (devEnum.GetDefaultAudioEndpoint(0 /* eRender */, 0 /* eConsole */, out curDefDev) == 0 && curDefDev != null)
                                {
                                    string curDefId = null;
                                    curDefDev.GetId(out curDefId);
                                    if (curDefId != defDevId)
                                    {
                                        defDevId = curDefId;
                                        defDev = curDefDev;
                                        object objDefVol = null;
                                        if (defDev.Activate(ref iidVol, 1, IntPtr.Zero, out objDefVol) == 0 && objDefVol != null)
                                        {
                                            defEpVol = objDefVol as IAudioEndpointVolume;
                                        }
                                        else
                                        {
                                            defEpVol = null;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }

                        IntPtr pData;
                        uint numFramesRead;
                        int flags;
                        ulong devPos, qpcPos;
                        hr = captureClient.GetBuffer(out pData, out numFramesRead, out flags, out devPos, out qpcPos);
                        if (hr != 0) continue;

                        float effectiveVol = 1.0f;
                        bool isMuted = false;

                        // 1. CABLE Input master volume & mute
                        if (epVol != null)
                        {
                            try
                            {
                                bool mute = false;
                                if (epVol.GetMute(out mute) == 0 && mute)
                                {
                                    isMuted = true;
                                }
                                else
                                {
                                    float lvl = 1.0f;
                                    if (epVol.GetMasterVolumeLevelScalar(out lvl) == 0)
                                    {
                                        if (lvl < 0.0f) lvl = 0.0f;
                                        else if (lvl > 1.0f) lvl = 1.0f;
                                        effectiveVol = lvl;
                                    }
                                }
                            }
                            catch { }
                        }

                        // 2. Default endpoint master volume & mute (if user controls default device or hardware Fn+Vol keys)
                        if (!isMuted && defEpVol != null && !string.Equals(cableDevId, defDevId, StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                bool defMute = false;
                                if (defEpVol.GetMute(out defMute) == 0 && defMute)
                                {
                                    isMuted = true;
                                }
                                else
                                {
                                    float defLvl = 1.0f;
                                    if (defEpVol.GetMasterVolumeLevelScalar(out defLvl) == 0)
                                    {
                                        if (defLvl < 0.0f) defLvl = 0.0f;
                                        else if (defLvl > 1.0f) defLvl = 1.0f;
                                        effectiveVol *= defLvl;
                                    }
                                }
                            }
                            catch { }
                        }

                        if (isMuted) effectiveVol = 0.0f;

                        if (numFramesRead > 0)
                        {
                            int pcmBytes = (int)numFramesRead * 4; // 16-bit stereo = 4 bytes per frame
                            if (pcmBuffer.Length < pcmBytes) pcmBuffer = new byte[pcmBytes * 2];

                            bool isSilence = (flags & 0x0002) != 0 || effectiveVol <= 0.0001f; // AUDCLNT_BUFFERFLAGS_SILENT or Muted
                            if (isSilence || pData == IntPtr.Zero)
                            {
                                Array.Clear(pcmBuffer, 0, pcmBytes);
                            }
                            else
                            {
                                unsafe
                                {
                                    float* fSrc = (float*)pData.ToPointer();
                                    fixed (byte* pDst = pcmBuffer)
                                    {
                                        short* sDst = (short*)pDst;
                                        int totalSamples = (int)numFramesRead * 2;
                                        if (Math.Abs(effectiveVol - 1.0f) < 0.001f)
                                        {
                                            for (int s = 0; s < totalSamples; s++)
                                            {
                                                float val = fSrc[s];
                                                if (val > 1.0f) val = 1.0f;
                                                else if (val < -1.0f) val = -1.0f;
                                                sDst[s] = (short)(val * 32767.0f);
                                            }
                                        }
                                        else
                                        {
                                            for (int s = 0; s < totalSamples; s++)
                                            {
                                                float val = fSrc[s] * effectiveVol;
                                                if (val > 1.0f) val = 1.0f;
                                                else if (val < -1.0f) val = -1.0f;
                                                sDst[s] = (short)(val * 32767.0f);
                                            }
                                        }
                                    }
                                }
                            }

                            long timestampMs = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
                            bw.Write(IPAddress.HostToNetworkOrder(pcmBytes));
                            bw.Write(IPAddress.HostToNetworkOrder(timestampMs));
                            bw.Write(pcmBuffer, 0, pcmBytes);
                            bw.Flush();
                        }

                        captureClient.ReleaseBuffer(numFramesRead);
                    }
                }
                catch (Exception ex)
                {
                    Program.Log("AudioStreamingWorker loop exception: " + ex.Message);
                }
                finally
                {
                    try { audioClient.Stop(); } catch { }
                    try { client.Close(); } catch { }
                    Program.Log("AudioStreamingWorker: Stopped audio streaming.");
                }
            }
        }
    }
}