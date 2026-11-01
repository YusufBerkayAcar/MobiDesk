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
    public enum VirtualAudioModalMode
    {
        GeneralInfo,
        InstallSuccess,
        RemoveSuccess
    }

    public class VirtualAudioInfoModal : Form
    {
        private VirtualAudioModalMode mode;

        public VirtualAudioInfoModal() : this(VirtualAudioModalMode.GeneralInfo)
        {
        }

        public VirtualAudioInfoModal(VirtualAudioModalMode mode)
        {
            this.mode = mode;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            int modalW = (mode == VirtualAudioModalMode.RemoveSuccess) ? 500 : 540;
            int modalH = (mode == VirtualAudioModalMode.RemoveSuccess) ? 260 : 446;
            this.Size = new Size(modalW, modalH);
            this.BackColor = MainForm.ColorBg;
            this.ShowInTaskbar = false;
            this.DoubleBuffered = true;
            this.KeyPreview = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            this.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    this.Close();
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    if (this.mode != VirtualAudioModalMode.RemoveSuccess)
                    {
                        OpenSoundSettings();
                    }
                    else
                    {
                        this.Close();
                    }
                }
            };

            this.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.Y < 60)
                {
                    MainForm.DragWindow(this.Handle);
                }
            };

            InitializeModalUI();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (GraphicsPath path = RoundedPanel.CreateRoundedRect(new Rectangle(0, 0, this.Width, this.Height), 12))
            {
                this.Region = new Region(path);
            }
            this.Invalidate();
        }

        private void OpenSoundSettings()
        {
            try
            {
                Process.Start("ms-settings:sound");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, L10n.T("Hata", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            this.Close();
        }

        private void InitializeModalUI()
        {
            // Kapatma butonu (✕)
            Button btnClose = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                ForeColor = MainForm.ColorTextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(32, 32),
                Location = new Point(this.Width - 44, 14),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.MouseEnter += delegate { btnClose.ForeColor = MainForm.ColorDanger; };
            btnClose.MouseLeave += delegate { btnClose.ForeColor = MainForm.ColorTextMuted; };
            btnClose.Click += delegate { this.Close(); };
            this.Controls.Add(btnClose);

            // İkon Rozeti
            Color badgeBorder = mode == VirtualAudioModalMode.InstallSuccess ? MainForm.ColorSuccess :
                                (mode == VirtualAudioModalMode.RemoveSuccess ? Color.FromArgb(244, 114, 182) : MainForm.ColorAccent);
            Color badgeBg = mode == VirtualAudioModalMode.InstallSuccess ? Color.FromArgb(16, 36, 26) :
                            (mode == VirtualAudioModalMode.RemoveSuccess ? Color.FromArgb(36, 20, 30) : Color.FromArgb(24, 32, 48));
            string badgeChar = (mode == VirtualAudioModalMode.InstallSuccess || mode == VirtualAudioModalMode.RemoveSuccess) ? "✓" : "🔊";

            Panel pnlIcon = new Panel
            {
                Size = new Size(38, 38),
                Location = new Point(20, 16),
                BackColor = badgeBg
            };
            pnlIcon.Paint += delegate(object s, PaintEventArgs pe)
            {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, pnlIcon.Width - 1, pnlIcon.Height - 1);
                using (GraphicsPath gp = RoundedPanel.CreateRoundedRect(r, 8))
                {
                    using (SolidBrush b = new SolidBrush(badgeBg)) g.FillPath(b, gp);
                    using (Pen p = new Pen(badgeBorder, 1.2f)) g.DrawPath(p, gp);
                }
                using (Font f = new Font("Segoe UI", 13f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, badgeChar, f, r, badgeBorder, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            };
            this.Controls.Add(pnlIcon);

            // Başlık & Alt Başlık
            string titleText = mode == VirtualAudioModalMode.InstallSuccess
                ? L10n.T("MobiDesk Audio Başarıyla Kuruldu!", "MobiDesk Audio Added Successfully!")
                : (mode == VirtualAudioModalMode.RemoveSuccess
                    ? L10n.T("MobiDesk Audio Sistemden Kaldırıldı", "MobiDesk Audio Device Removed")
                    : L10n.T("MobiDesk Ses Yayını Altyapısı", "MobiDesk Audio Streaming Architecture"));

            string subText = mode == VirtualAudioModalMode.InstallSuccess
                ? L10n.T("Windows ses çıkışı artık tabletinize kayıpsız PCM ile aktarılmaya hazır", "Windows audio is now ready to stream lossless PCM to your tablet")
                : (mode == VirtualAudioModalMode.RemoveSuccess
                    ? L10n.T("Sanal ses kartı ve kayıtları sistemden başarıyla temizlendi", "Virtual audio device and registry entries removed successfully")
                    : L10n.T("48kHz 16-bit Stereo kayıpsız ses aktarımı ve WASAPI Loopback", "48kHz 16-bit Stereo lossless audio streaming with WASAPI Loopback"));

            Label lblTitle = new Label
            {
                Text = titleText,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = MainForm.ColorText,
                Location = new Point(68, 16),
                Size = new Size(this.Width - 120, 22),
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            lblTitle.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) MainForm.DragWindow(this.Handle); };
            this.Controls.Add(lblTitle);

            Label lblSubtitle = new Label
            {
                Text = subText,
                Font = new Font("Segoe UI", 7.8f),
                ForeColor = MainForm.ColorTextSubtle,
                Location = new Point(68, 38),
                Size = new Size(this.Width - 120, 18),
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            lblSubtitle.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) MainForm.DragWindow(this.Handle); };
            this.Controls.Add(lblSubtitle);

            // Ana İçerik Kartı
            int cardH = (mode == VirtualAudioModalMode.RemoveSuccess) ? 104 : 300;
            Panel pnlCard = new Panel
            {
                Location = new Point(20, 68),
                Size = new Size(this.Width - 40, cardH),
                BackColor = MainForm.ColorCard
            };
            pnlCard.Paint += delegate(object s, PaintEventArgs pe)
            {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, pnlCard.Width - 1, pnlCard.Height - 1);
                using (GraphicsPath gp = RoundedPanel.CreateRoundedRect(r, 10))
                {
                    using (SolidBrush b = new SolidBrush(MainForm.ColorCard)) g.FillPath(b, gp);
                    using (Pen p = new Pen(MainForm.ColorCardBorder, 1.0f)) g.DrawPath(p, gp);
                }
            };

            int cardInnerW = pnlCard.Width - 24;

            if (mode == VirtualAudioModalMode.RemoveSuccess)
            {
                AddInfoItem(pnlCard, 3,
                    L10n.T("Varsayılan Ses Aygıtı Etkin", "Default Audio Device Restored"),
                    L10n.T("MobiDesk Audio sanal ses kartı başarıyla kaldırıldı. Windows ses çıkışınız varsayılan hoparlörünüze döndü.",
                           "MobiDesk Audio virtual device removed. Windows audio output restored to default speakers."),
                    12, 10, cardInnerW);
            }
            else if (mode == VirtualAudioModalMode.InstallSuccess)
            {
                int itemY = 12;
                AddInfoItem(pnlCard, 0,
                    L10n.T("1. Windows Ses Ayarlarını Açın", "1. Open Windows Sound Settings"),
                    L10n.T("Aşağıdaki yeşil 'Windows Ses Ayarlarını Aç' butonuna tıklayarak doğrudan sistem ses ayarları sayfasına gidin.",
                           "Click the green 'Open Windows Sound Settings' button below to navigate directly to Sound settings."),
                    12, itemY, cardInnerW);
                itemY += 94;

                AddInfoItem(pnlCard, 1,
                    L10n.T("2. Çıkış Aygıtı: 'MobiDesk Audio' Seçin", "2. Select Output: 'MobiDesk Audio'"),
                    L10n.T("Çıkış cihazı listesinden 'MobiDesk Audio' (veya CABLE Input) aygıtını seçin. Bilgisayar sesleri doğrudan tabletinize yönlendirilecektir.",
                           "Select 'MobiDesk Audio' (or CABLE Input) from output devices. All PC audio will be routed to your tablet."),
                    12, itemY, cardInnerW);
                itemY += 94;

                AddInfoItem(pnlCard, 2,
                    L10n.T("3. USB Üzerinden Kayıpsız Ses Keyfi", "3. Lossless Audio over USB"),
                    L10n.T("48,000 Hz 16-bit Stereo PCM ile gecikmesiz, kristal netliğinde ses yayını otomatik başlar.",
                           "Ultra-low latency, crystal-clear 48kHz 16-bit Stereo PCM audio streaming begins automatically."),
                    12, itemY, cardInnerW);
            }
            else
            {
                int itemY = 12;
                AddInfoItem(pnlCard, 0,
                    L10n.T("Bağımsız Sanal Ses Kartı", "Dedicated Virtual Audio Device"),
                    L10n.T("MobiDesk Audio, Windows ses sistemine entegre WDM PortCls sanal sürücüsüdür. Hoparlör sesinizi bozmadan sesi izole eder.",
                           "MobiDesk Audio is an isolated WDM PortCls virtual audio device. It routes clean digital audio without speaker crosstalk."),
                    12, itemY, cardInnerW);
                itemY += 94;

                AddInfoItem(pnlCard, 1,
                    L10n.T("Özel TCP Port 55556 İletimi", "Dedicated TCP Port 55556"),
                    L10n.T("Ses akışı video kanalından bağımsız özel yüksek öncelikli soket üzerinden aktarılır. Video dalgalanmaları sesi etkilemez.",
                           "Audio streams over an isolated high-priority socket channel. Video framerate dips never affect audio fidelity."),
                    12, itemY, cardInnerW);
                itemY += 94;

                AddInfoItem(pnlCard, 2,
                    L10n.T("Sıfır Gecikme & Düşük Arabellek", "Zero-Lag & Low Latency"),
                    L10n.T("Android tarafındaki akıllı jitter arabelleği sayesinde ses ile görüntü milisaniyelik hassasiyetle tam senkron çalışır.",
                           "Smart jitter buffering on Android ensures sub-20ms audio latency perfectly synchronized with display mirror."),
                    12, itemY, cardInnerW);
            }

            this.Controls.Add(pnlCard);

            // Alt Butonlar
            int btnH = 38;
            int btnY = this.Height - 56;

            if (mode == VirtualAudioModalMode.RemoveSuccess)
            {
                ModernButton btnOk = new ModernButton(
                    L10n.T("Tamam", "OK"),
                    this.Width - 20 - 130, btnY, 130, btnH,
                    Color.FromArgb(32, 24, 32),
                    Color.FromArgb(244, 114, 182),
                    8,
                    Color.FromArgb(70, 30, 50)
                );
                btnOk.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                btnOk.Click += delegate { this.Close(); };
                this.Controls.Add(btnOk);
            }
            else
            {
                ModernButton btnCancel = new ModernButton(
                    L10n.T("Kapat", "Close"),
                    20, btnY, 110, btnH,
                    Color.FromArgb(20, 26, 38),
                    MainForm.ColorTextMuted,
                    8,
                    MainForm.ColorCardBorder
                );
                btnCancel.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                btnCancel.Click += delegate { this.Close(); };
                this.Controls.Add(btnCancel);

                Color btnSettingsCol = mode == VirtualAudioModalMode.InstallSuccess ? MainForm.ColorSuccess : MainForm.ColorPrimary;
                Color btnSettingsDark = mode == VirtualAudioModalMode.InstallSuccess ? Color.FromArgb(14, 150, 105) : MainForm.ColorPrimaryDark;

                ModernButton btnOpenSettings = new ModernButton(
                    L10n.T("🚀 Windows Ses Ayarlarını Aç", "🚀 Open Windows Sound Settings"),
                    this.Width - 20 - 260, btnY, 260, btnH,
                    btnSettingsCol,
                    Color.White,
                    8,
                    btnSettingsDark
                );
                btnOpenSettings.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                btnOpenSettings.Click += delegate { OpenSoundSettings(); };
                this.Controls.Add(btnOpenSettings);
            }
        }

        private void AddInfoItem(Control parent, int iconType, string title, string desc, int x, int y, int w)
        {
            Panel itemPnl = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, 84),
                BackColor = Color.FromArgb(13, 17, 26)
            };
            itemPnl.Paint += delegate(object s, PaintEventArgs pe)
            {
                Graphics g = pe.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, itemPnl.Width - 1, itemPnl.Height - 1);
                using (GraphicsPath gp = RoundedPanel.CreateRoundedRect(r, 8))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(13, 17, 26))) g.FillPath(b, gp);
                    using (Pen p = new Pen(MainForm.ColorCardBorder, 0.8f)) g.DrawPath(p, gp);
                }
            };

            string iconStr = "🔊";
            Color iconCol = MainForm.ColorAccent;
            if (iconType == 0) { iconStr = "⚙️"; iconCol = MainForm.ColorSuccess; }
            else if (iconType == 1) { iconStr = "🎧"; iconCol = MainForm.ColorPrimary; }
            else if (iconType == 2) { iconStr = "⚡"; iconCol = MainForm.ColorWarning; }
            else if (iconType == 3) { iconStr = "✓"; iconCol = Color.FromArgb(244, 114, 182); }

            Label lblIcon = new Label
            {
                Text = iconStr,
                Font = new Font("Segoe UI", 14f),
                ForeColor = iconCol,
                Location = new Point(10, 12),
                Size = new Size(32, 32),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            itemPnl.Controls.Add(lblIcon);

            Label lblItemTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                ForeColor = MainForm.ColorText,
                Location = new Point(48, 10),
                Size = new Size(w - 56, 20),
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            itemPnl.Controls.Add(lblItemTitle);

            Label lblItemDesc = new Label
            {
                Text = desc,
                Font = new Font("Segoe UI", 7.8f),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(48, 30),
                Size = new Size(w - 56, 48),
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            itemPnl.Controls.Add(lblItemDesc);

            parent.Controls.Add(itemPnl);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using (GraphicsPath gp = RoundedPanel.CreateRoundedRect(r, 12))
            {
                Color borderCol = mode == VirtualAudioModalMode.InstallSuccess ? MainForm.ColorSuccess :
                                  (mode == VirtualAudioModalMode.RemoveSuccess ? Color.FromArgb(244, 114, 182) : MainForm.ColorPrimary);
                using (Pen p = new Pen(borderCol, 1.5f))
                {
                    g.DrawPath(p, gp);
                }
            }
        }
    }


}