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
        private void InitializeStudioUI()
        {
            this.Text = "MobiDesk Studio";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = true;
            this.BackColor = ColorBg;
            this.ForeColor = ColorText;
            this.Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);

            int windowW = Math.Max(860, AppConfig.WindowWidth);
            int windowH = Math.Max(680, AppConfig.WindowHeight);
            this.MinimumSize = new Size(860, 680);
            this.ClientSize = new Size(windowW, windowH);

            // 1. MODERN ENTEGRE BAŞLIK ÇUBUĞU
            titleBar = new ModernTitleBar(this, "MobiDesk Studio", "BETA");
            titleBar.Location = new Point(0, 0);
            titleBar.Size = new Size(windowW, 42);
            titleBar.LanguageChanged += delegate(bool english)
            {
                SetLanguage(english);
            };
            this.Controls.Add(titleBar);

            // 2. HERO CİHAZ BİLGİ KARTI (DİNAMİK USB ALGILAMA)
            heroBanner = new TabletHeroBanner(
                L10n.T("USB Cihazı Aranıyor...", "Searching for USB Device..."),
                L10n.T("USB kablosunu bağlayın • USB Hata Ayıklama açık olmalıdır", "Connect USB cable • USB Debugging must be enabled"),
                L10n.T("ARANIYOR", "SEARCHING"),
                MainForm.ColorWarning
            );
            heroBanner.Location = new Point(18, 52);
            heroBanner.Size = new Size(windowW - 36, 58);
            heroBanner.Cursor = Cursors.Hand;
            heroBanner.Click += delegate(object s, EventArgs e)
            {
                if (connectedSerials != null && connectedSerials.Count > 1)
                {
                    int idx = connectedSerials.IndexOf(activeTargetSerial);
                    idx = (idx + 1) % connectedSerials.Count;
                    activeTargetSerial = connectedSerials[idx];
                    ThreadPool.QueueUserWorkItem(delegate { CheckConnectedUsbDevicesAsync(); });
                }
            };
            this.Controls.Add(heroBanner);

            // 2.5. MODERN SEKME ÇUBUĞU (EKRAN / FLOW / BİLDİRİMLER)
            tabSelector = new ModernStudioTabs(18, 116, windowW - 36, 36);
            tabSelector.SelectedTabChanged += delegate(int tabIndex)
            {
                SwitchStudioTab(tabIndex);
            };
            this.Controls.Add(tabSelector);

            int colY = 158;
            int bottomSpace = 118;
            int colH = Math.Max(396, windowH - colY - bottomSpace);
            int colW = Math.Max(350, (windowW - 36 - 16) / 2);

            // ═══════════════════════════════════════════════
            // SOL PANEL: EKRAN VE YAYIN MODU
            // ═══════════════════════════════════════════════
            pnlLeftCol = new DoubleBufferedPanel
            {
                Location = new Point(18, colY),
                Size = new Size(colW, colH),
                BackColor = ColorBg
            };

            // Sol Kart 1: Çalışma Modu (H=68)
            cardMode = new ModernCard("ÇALIŞMA MODU (TOPOLOJİ)", 0, 0, colW, 68);
            modeControl = new ModernSegmentedMode(
                "🖥️ Genişlet (2. Ekran)",
                "🔄 Yinele (Ayna)",
                12, 24, colW - 24, 38
            );
            modeControl.IsExtend = AppConfig.IsExtendMode;
            modeControl.ModeChanged += delegate(bool isExtend)
            {
                AppConfig.IsExtendMode = isExtend;
                AppConfig.Save();
                AutoSelectScreen(isExtend);
            };
            cardMode.Controls.Add(modeControl);
            pnlLeftCol.Controls.Add(cardMode);

            int cardVirtH = 88;
            int cardAudioH = 104;
            int cardVirtY = colH - cardVirtH;
            int cardAudioY = cardVirtY - cardAudioH - 6;
            int screensY = 68 + 6; // 74
            int screensH = Math.Max(120, (cardAudioY - 6) - screensY);
            int btnHalfW = (colW - 24 - 8) / 2;

            // Sol Kart 3: Ses Yayını (MobiDesk Audio) (H=104, ferah ve net düzen)
            cardAudio = new ModernCard(L10n.T("SES YAYINI (MOBIDESK AUDIO)", "AUDIO STREAMING (MOBIDESK AUDIO)"), 0, cardAudioY, colW, cardAudioH);

            btnAudioInfo = new ModernButton(L10n.T("ℹ️ Bilgi", "ℹ️ Info"), colW - 74, 5, 62, 19, Color.FromArgb(28, 36, 54), ColorAccent, 5);
            btnAudioInfo.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            btnAudioInfo.Click += BtnAudioInfo_Click;
            cardAudio.Controls.Add(btnAudioInfo);

            swEnableAudio = new ModernSwitch(
                L10n.T("Sesi Tablete İlet", "Stream Audio to Tablet"),
                L10n.T("Windows sesini USB üzerinden tablete aktarır", "Streams Windows PC audio to tablet via USB/ADB"),
                12, 28, colW - 24, 34
            );
            swEnableAudio.Checked = AppConfig.EnableAudio;
            swEnableAudio.CheckedChanged += delegate(object s, EventArgs e)
            {
                AppConfig.EnableAudio = swEnableAudio.Checked;
                AppConfig.Save();
                if (swEnableAudio.Checked)
                {
                    if (isStreaming) StartAudioStreamingWorker();
                }
                else
                {
                    StopAudioStreamingWorker();
                }
            };
            cardAudio.Controls.Add(swEnableAudio);

            btnAddAudio = new ModernButton("➕ " + L10n.T("Sanal Ses Ayarla", "Setup Virtual Audio"), 12, 68, btnHalfW, 28, ColorSuccess, Color.White, 7);
            btnAddAudio.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            btnAddAudio.Click += BtnAddAudio_Click;

            btnRemoveAudio = new ModernButton("🗑️ " + L10n.T("Sürücüyü Kaldır", "Remove Driver"), 12 + btnHalfW + 8, 68, btnHalfW, 28, Color.FromArgb(32, 24, 32), Color.FromArgb(244, 114, 182), 7, Color.FromArgb(70, 30, 50));
            btnRemoveAudio.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            btnRemoveAudio.Click += BtnRemoveAudio_Click;

            cardAudio.Controls.Add(btnAddAudio);
            cardAudio.Controls.Add(btnRemoveAudio);
            pnlLeftCol.Controls.Add(cardAudio);

            // Sol Kart 4: Sanal Ekran Sürücüsü (VDD) (H=88, alta kilitli)
            cardVirtual = new ModernCard("SANAL 3. EKRAN YÖNETİMİ (VDD)", 0, cardVirtY, colW, cardVirtH);

            btnVirtualInfo = new ModernButton(L10n.T("ℹ️ Bilgi", "ℹ️ Info"), colW - 74, 5, 62, 19, Color.FromArgb(28, 36, 54), ColorAccent, 5);
            btnVirtualInfo.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            btnVirtualInfo.Click += BtnVirtualInfo_Click;
            vddToolTip = new ToolTip();
            vddToolTip.SetToolTip(btnVirtualInfo, L10n.T("Sanal ekran konumu Windows Görüntü Ayarları'ndan düzenlenebilir. (Açmak için tıklayın)", "Virtual display position can be arranged in Windows Display Settings. (Click to open)"));
            cardVirtual.Controls.Add(btnVirtualInfo);

            lblVddHint = new Label
            {
                Text = L10n.T("Bağlı cihaza özel 1:1 piksel netliğinde sanal ekran sağlar.", "Provides a 1:1 pixel-perfect virtual external display."),
                Font = new Font("Segoe UI", 7.2f),
                ForeColor = ColorTextSubtle,
                BackColor = ColorCard,
                Location = new Point(14, 28),
                Size = new Size(colW - 28, 16)
            };
            cardVirtual.Controls.Add(lblVddHint);

            btnAddVirtual = new ModernButton("➕ Sanal Ekran Ayarla", 12, 48, btnHalfW, 30, ColorPrimary, Color.White, 7);
            btnAddVirtual.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btnAddVirtual.Click += BtnAddVirtual_Click;

            btnRemoveVirtual = new ModernButton("🗑️ Sürücüyü Kaldır", 12 + btnHalfW + 8, 48, btnHalfW, 30, Color.FromArgb(32, 24, 32), Color.FromArgb(244, 114, 182), 7, Color.FromArgb(70, 30, 50));
            btnRemoveVirtual.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            btnRemoveVirtual.Click += BtnRemoveVirtual_Click;

            cardVirtual.Controls.Add(btnAddVirtual);
            cardVirtual.Controls.Add(btnRemoveVirtual);
            pnlLeftCol.Controls.Add(cardVirtual);

            // Sol Kart 2: Yansıtılacak Monitör Seçimi (Orta dinamik alan)
            cardScreens = new ModernCard("YANSITILACAK MONİTÖR", 0, screensY, colW, screensH);
            
            btnRefresh = new ModernButton("🔄 Yenile", colW - 86, 5, 74, 20, Color.FromArgb(28, 36, 54), ColorTextMuted, 5);
            btnRefresh.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            btnRefresh.Click += delegate(object s, EventArgs e) { RefreshScreenList(); };
            cardScreens.Controls.Add(btnRefresh);

            screenSelector = new ModernMonitorSelector(12, 28, colW - 24, Math.Max(40, screensH - 34));
            screenSelector.SelectionChanged += delegate(int index, Screen scr, bool isVirt)
            {
                selectedScreenIndex = index;
                if (scr != null)
                {
                    selectedDeviceName = scr.DeviceName;
                    AppConfig.LastMonitorDevice = selectedDeviceName;
                    AppConfig.Save();
                    Rectangle phys = GetPhysicalMonitorBounds(selectedDeviceName);
                    currentScreenBounds = (phys.Width > 0 && phys.Height > 0) ? phys : scr.Bounds;
                }
            };
            cardScreens.Controls.Add(screenSelector);
            pnlLeftCol.Controls.Add(cardScreens);

            this.Controls.Add(pnlLeftCol);

            // ═══════════════════════════════════════════════
            // SAĞ PANEL: PERFORMANS VE MOTOR AYARLARI
            // ═══════════════════════════════════════════════
            pnlRightCol = new DoubleBufferedPanel
            {
                Location = new Point(18 + colW + 16, colY),
                Size = new Size(colW, colH),
                BackColor = ColorBg
            };

            // Sağ Kart 1: Performans & Hız (H=172, Y=0)
            cardPerf = new ModernCard("AKIŞ VE HIZ LİMİTİ", 0, 0, colW, 172);

            // FPS Satırı
            lblFpsTitle = new Label
            {
                Text = "Hedef Kare Hızı",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = ColorTextMuted,
                BackColor = ColorCard,
                Location = new Point(12, 25),
                AutoSize = true
            };
            lblFpsVal = new Label
            {
                Text = "60 FPS",
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                ForeColor = ColorSuccess,
                BackColor = ColorCard,
                Location = new Point(colW - 90, 24),
                Size = new Size(78, 18),
                TextAlign = ContentAlignment.MiddleRight
            };
            cardPerf.Controls.Add(lblFpsTitle);
            cardPerf.Controls.Add(lblFpsVal);

            tbFps = new ModernSlider(15, 75, AppConfig.Fps, ColorSuccess, Color.FromArgb(6, 182, 212))
            {
                Location = new Point(12, 45),
                Size = new Size(colW - 24, 18)
            };
            tbFps.ValueChanged += delegate(object s, EventArgs e) { UpdateFps(tbFps.Value); };
            tbFps.MouseUp += delegate(object s, MouseEventArgs e)
            {
                AppConfig.Fps = tbFps.Value;
                AppConfig.Save();
            };
            cardPerf.Controls.Add(tbFps);

            // FPS Hızlı Butonlar
            fpsChips.Clear();
            int[] fpsPresets = new int[] { 30, 45, 60, 75 };
            int chipW = (colW - 24 - 9) / 4;
            for (int i = 0; i < fpsPresets.Length; i++)
            {
                int val = fpsPresets[i];
                QuickChip chip = new QuickChip(val.ToString() + " FPS", 12 + i * (chipW + 3), 67, chipW, 22);
                chip.Click += delegate(object s, EventArgs e)
                {
                    tbFps.Value = val;
                    AppConfig.Fps = val;
                    AppConfig.Save();
                };
                cardPerf.Controls.Add(chip);
                fpsChips.Add(chip);
            }

            // Kalite Satırı
            lblQualityTitle = new Label
            {
                Text = "Görüntü Netliği Kalite",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = ColorTextMuted,
                BackColor = ColorCard,
                Location = new Point(12, 96),
                AutoSize = true
            };
            lblQualityVal = new Label
            {
                Text = "%65 (Dengeli)",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = ColorAccent,
                BackColor = ColorCard,
                Location = new Point(colW - 120, 95),
                Size = new Size(108, 18),
                TextAlign = ContentAlignment.MiddleRight
            };
            cardPerf.Controls.Add(lblQualityTitle);
            cardPerf.Controls.Add(lblQualityVal);

            tbQuality = new ModernSlider(15, 100, AppConfig.Quality, ColorPrimary, Color.FromArgb(168, 85, 247))
            {
                Location = new Point(12, 116),
                Size = new Size(colW - 24, 18)
            };
            tbQuality.ValueChanged += delegate(object s, EventArgs e) { UpdateQuality(tbQuality.Value); };
            tbQuality.MouseUp += delegate(object s, MouseEventArgs e)
            {
                AppConfig.Quality = tbQuality.Value;
                AppConfig.Save();
            };
            cardPerf.Controls.Add(tbQuality);

            // Kalite Hızlı Butonlar
            qualityChips.Clear();
            string[] qLabels = new string[] { "%40", "%65", "%85", "%100" };
            int[] qPresets = new int[] { 40, 65, 85, 100 };
            for (int i = 0; i < qPresets.Length; i++)
            {
                int val = qPresets[i];
                QuickChip chip = new QuickChip(qLabels[i], 12 + i * (chipW + 3), 138, chipW, 22);
                chip.Click += delegate(object s, EventArgs e)
                {
                    tbQuality.Value = val;
                    AppConfig.Quality = val;
                    AppConfig.Save();
                };
                cardPerf.Controls.Add(chip);
                qualityChips.Add(chip);
            }

            pnlRightCol.Controls.Add(cardPerf);

            // Sağ Kart 2: Sistem ve Motor Seçenekleri (H=214, Y=182)
            int optsY = 182;
            int optsH = Math.Max(214, colH - optsY);
            cardOpts = new ModernCard("SİSTEM VE OPTİMİZASYONLAR", 0, optsY, colW, optsH);

            swAdaptiveFps = new ModernSwitch(
                "⚡ " + L10n.T("Akıllı 60 FPS Performans Modu", "Smart 60 FPS Performance Mode"),
                L10n.T("Yüksek pikselli cihazları 60 FPS'e dengeler (Kapalıyken 1:1)", "Balances high-res devices for 60 FPS (1:1 when off)"),
                12, 26, colW - 24, 40
            );
            swAdaptiveFps.Checked = AppConfig.AdaptiveFps;
            swAdaptiveFps.CheckedChanged += delegate(object s, EventArgs e)
            {
                enableAdaptiveFps = swAdaptiveFps.Checked;
                AppConfig.AdaptiveFps = swAdaptiveFps.Checked;
                AppConfig.Save();
                UpdateDeviceSpecsDisplay();
            };
            cardOpts.Controls.Add(swAdaptiveFps);

            swShowFps = new ModernSwitch(
                L10n.T("Cihazda Canlı FPS Sayacı Göster", "Show Live FPS Counter on Device"),
                L10n.T("Sol üst köşede yeşil donanım OSD sayacı gösterir", "Displays green on-screen OSD telemetry counter"),
                12, 70, colW - 24, 40
            );
            swShowFps.Checked = AppConfig.ShowFps;
            swShowFps.CheckedChanged += delegate(object s, EventArgs e)
            {
                showFpsOnTablet = swShowFps.Checked;
                AppConfig.ShowFps = swShowFps.Checked;
                AppConfig.Save();
                pendingFpsConfig = true;
            };
            cardOpts.Controls.Add(swShowFps);

            swBatterySaver = new ModernSwitch(
                L10n.T("Akıllı Güç ve Veri Tasarrufu", "Smart Power & Data Saver"),
                L10n.T("Statik ekranda gereksiz veri aktarımını durdurur", "Pauses transmission when screen is static"),
                12, 114, colW - 24, 40
            );
            swBatterySaver.Checked = AppConfig.BatterySaver;
            swBatterySaver.CheckedChanged += delegate(object s, EventArgs e)
            {
                enableBatterySaver = swBatterySaver.Checked;
                AppConfig.BatterySaver = swBatterySaver.Checked;
                AppConfig.Save();
            };
            cardOpts.Controls.Add(swBatterySaver);

            swStartup = new ModernSwitch(
                "🪟 " + L10n.T("Windows ile Birlikte Başlat", "Start with Windows"),
                L10n.T("PC açılışında arka planda sessizce başlar, bildirimleri yakalar", "Starts silently in tray on boot to capture notifications"),
                12, 158, colW - 24, 40
            );
            swStartup.Checked = WindowsStartupManager.IsStartupEnabled();
            swStartup.CheckedChanged += delegate(object s, EventArgs e)
            {
                WindowsStartupManager.SetStartup(swStartup.Checked);
                if (pnlNotifications != null) pnlNotifications.UpdateStartupState();
            };
            cardOpts.Controls.Add(swStartup);

            pnlRightCol.Controls.Add(cardOpts);

            UpdateFps(AppConfig.Fps);
            UpdateQuality(AppConfig.Quality);

            this.Controls.Add(pnlRightCol);

            // ═══════════════════════════════════════════════
            // ALT KOMUTA VE CANLI TELEMETRİ GÜVERTESİ
            // ═══════════════════════════════════════════════
            int bottomY = colY + colH + 10;

            // Büyük Aksiyon Butonu
            btnStartStop = new ModernButton(
                "▶    EKRAN PAYLAŞIMINI BAŞLAT",
                18, bottomY, windowW - 36, 46,
                ColorSuccess, Color.FromArgb(11, 15, 25), 10
            );
            btnStartStop.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            btnStartStop.Click += BtnStartStop_Click;
            this.Controls.Add(btnStartStop);

            // Canlı Telemetri Çubuğu
            pnlBottomBar = new RoundedPanel(windowW - 36, 46, 10, ColorCard, ColorCardBorder);
            pnlBottomBar.Location = new Point(18, bottomY + 52);

            lblStatusDot = new Label
            {
                Text = "●",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = ColorSuccess,
                BackColor = ColorCard,
                Location = new Point(12, 7),
                AutoSize = true
            };
            pnlBottomBar.Controls.Add(lblStatusDot);

            lblStatusTitle = new Label
            {
                Text = "HAZIR",
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                ForeColor = ColorText,
                BackColor = ColorCard,
                Location = new Point(32, 8),
                AutoSize = true
            };
            pnlBottomBar.Controls.Add(lblStatusTitle);

            lblStatusSub = new Label
            {
                Text = "Tablete aktarımı başlatmak için yukarıdaki butona tıklayın.",
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = ColorTextMuted,
                BackColor = ColorCard,
                Location = new Point(33, 28),
                Size = new Size(330, 16)
            };
            pnlBottomBar.Controls.Add(lblStatusSub);

            // Sağ Telemetri Rozetleri
            badgeLatency = new TelemetryBadge("⏱ Gecikme", "<1 ms", (windowW - 36) - 86, 11, 74, 28);
            badgeSize = new TelemetryBadge("📦 Boyut", "0 KB", (windowW - 36) - 176, 11, 82, 28);
            badgeBandwidth = new TelemetryBadge("📶 Veri", "0.0 MB/s", (windowW - 36) - 276, 11, 92, 28);
            badgeFps = new TelemetryBadge("⚡ FPS", "0", (windowW - 36) - 356, 11, 72, 28);

            pnlBottomBar.Controls.Add(badgeLatency);
            pnlBottomBar.Controls.Add(badgeSize);
            pnlBottomBar.Controls.Add(badgeBandwidth);
            pnlBottomBar.Controls.Add(badgeFps);

            this.Controls.Add(pnlBottomBar);

            // 5. MOBIFLOW VE BİLDİRİMLER PANELLERİ
            pnlFlow = new MobiFlowPanel(18, 158, windowW - 36, windowH - 158 - 18, flowManager);
            pnlFlow.Visible = false;
            this.Controls.Add(pnlFlow);

            pnlNotifications = new MobiNotificationsPanel(18, 158, windowW - 36, windowH - 158 - 18, flowManager);
            pnlNotifications.Visible = false;
            this.Controls.Add(pnlNotifications);

            // Yerleşim düzenini dinamik boyutlara hizala
            LayoutStudioUI();

            // Dinamik USB Cihaz İzleme Zamanlayıcısını Başlat
            deviceMonitorTimer = new System.Windows.Forms.Timer();
            deviceMonitorTimer.Interval = 2500;
            deviceMonitorTimer.Tick += delegate(object s, EventArgs e) { CheckConnectedUsbDevicesAsync(); };
            deviceMonitorTimer.Start();
            CheckConnectedUsbDevicesAsync();

            this.FormClosing += MainForm_FormClosing;
        }

        private void LayoutStudioUI()
        {
            if (titleBar == null || heroBanner == null || pnlLeftCol == null || pnlRightCol == null || btnStartStop == null || pnlBottomBar == null)
                return;

            this.SuspendLayout();
            pnlLeftCol.SuspendLayout();
            pnlRightCol.SuspendLayout();

            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;

            // 1. Başlık Çubuğu
            titleBar.Location = new Point(0, 0);
            titleBar.Size = new Size(w, 42);

            // 2. Hero Cihaz Bannerı
            heroBanner.Location = new Point(18, 52);
            heroBanner.Size = new Size(w - 36, 58);

            // 2.5 Sekme Çubuğu
            if (tabSelector != null)
            {
                tabSelector.Location = new Point(18, 116);
                tabSelector.Size = new Size(w - 36, 36);
            }

            // 3. İki Sütunlu Kart Izgarası
            int colY = 158;
            int bottomSpace = 118; // btnStartStop(46) + pnlBottomBar(46) + boşluklar
            int colH = Math.Max(396, h - colY - bottomSpace);
            int colW = Math.Max(350, (w - 36 - 16) / 2);

            // Sol Panel
            pnlLeftCol.Location = new Point(18, colY);
            pnlLeftCol.Size = new Size(colW, colH);

            if (cardMode != null)
            {
                cardMode.Location = new Point(0, 0);
                cardMode.Size = new Size(colW, 68);
                if (modeControl != null)
                {
                    modeControl.Location = new Point(12, 24);
                    modeControl.Size = new Size(colW - 24, 38);
                }
            }

            int cardVirtH = 88;
            int cardAudioH = 104;
            int cardVirtY = colH - cardVirtH;
            int cardAudioY = cardVirtY - cardAudioH - 6;
            int btnHalfW = (colW - 24 - 8) / 2;

            if (cardVirtual != null)
            {
                cardVirtual.Location = new Point(0, cardVirtY);
                cardVirtual.Size = new Size(colW, cardVirtH);
                if (btnVirtualInfo != null)
                {
                    btnVirtualInfo.Location = new Point(colW - 74, 5);
                    btnVirtualInfo.Size = new Size(62, 19);
                }
                if (lblVddHint != null)
                {
                    lblVddHint.Location = new Point(14, 28);
                    lblVddHint.Size = new Size(colW - 28, 16);
                }
                if (btnAddVirtual != null)
                {
                    btnAddVirtual.Location = new Point(12, 48);
                    btnAddVirtual.Size = new Size(btnHalfW, 30);
                }
                if (btnRemoveVirtual != null)
                {
                    btnRemoveVirtual.Location = new Point(12 + btnHalfW + 8, 48);
                    btnRemoveVirtual.Size = new Size(btnHalfW, 30);
                }
            }

            if (cardAudio != null)
            {
                cardAudio.Location = new Point(0, cardAudioY);
                cardAudio.Size = new Size(colW, cardAudioH);
                if (btnAudioInfo != null)
                {
                    btnAudioInfo.Location = new Point(colW - 74, 5);
                    btnAudioInfo.Size = new Size(62, 19);
                }
                if (swEnableAudio != null)
                {
                    swEnableAudio.Location = new Point(12, 28);
                    swEnableAudio.Size = new Size(colW - 24, 34);
                }
                if (btnAddAudio != null)
                {
                    btnAddAudio.Location = new Point(12, 68);
                    btnAddAudio.Size = new Size(btnHalfW, 28);
                }
                if (btnRemoveAudio != null)
                {
                    btnRemoveAudio.Location = new Point(12 + btnHalfW + 8, 68);
                    btnRemoveAudio.Size = new Size(btnHalfW, 28);
                }
            }

            if (cardScreens != null)
            {
                int screensY = 68 + 6; // 74
                int screensH = Math.Max(120, (cardAudioY - 6) - screensY);
                cardScreens.Location = new Point(0, screensY);
                cardScreens.Size = new Size(colW, screensH);
                if (btnRefresh != null)
                {
                    btnRefresh.Location = new Point(colW - 86, 5);
                    btnRefresh.Size = new Size(74, 20);
                }
                if (screenSelector != null)
                {
                    screenSelector.Location = new Point(12, 28);
                    screenSelector.Size = new Size(colW - 24, Math.Max(40, screensH - 34));
                }
            }

            // Sağ Panel
            pnlRightCol.Location = new Point(18 + colW + 16, colY);
            pnlRightCol.Size = new Size(colW, colH);

            // Flow & Bildirim Panelleri (Tam Boyut)
            if (pnlFlow != null)
            {
                pnlFlow.Location = new Point(18, colY);
                pnlFlow.Size = new Size(w - 36, h - colY - 18);
            }
            if (pnlNotifications != null)
            {
                pnlNotifications.Location = new Point(18, colY);
                pnlNotifications.Size = new Size(w - 36, h - colY - 18);
            }

            if (cardPerf != null)
            {
                cardPerf.Location = new Point(0, 0);
                cardPerf.Size = new Size(colW, 172);

                if (lblFpsVal != null) lblFpsVal.Location = new Point(colW - 90, 24);
                if (tbFps != null)
                {
                    tbFps.Location = new Point(12, 45);
                    tbFps.Size = new Size(colW - 24, 18);
                }

                int chipW = (colW - 24 - 9) / 4;
                if (fpsChips != null)
                {
                    for (int i = 0; i < fpsChips.Count; i++)
                    {
                        fpsChips[i].Location = new Point(12 + i * (chipW + 3), 67);
                        fpsChips[i].Size = new Size(chipW, 22);
                    }
                }

                if (lblQualityVal != null) lblQualityVal.Location = new Point(colW - 120, 95);
                if (tbQuality != null)
                {
                    tbQuality.Location = new Point(12, 116);
                    tbQuality.Size = new Size(colW - 24, 18);
                }

                if (qualityChips != null)
                {
                    for (int i = 0; i < qualityChips.Count; i++)
                    {
                        qualityChips[i].Location = new Point(12 + i * (chipW + 3), 138);
                        qualityChips[i].Size = new Size(chipW, 22);
                    }
                }
            }

            if (cardOpts != null)
            {
                int optsY = 182;
                int optsH = Math.Max(214, colH - optsY);
                cardOpts.Location = new Point(0, optsY);
                cardOpts.Size = new Size(colW, optsH);

                if (swAdaptiveFps != null)
                {
                    swAdaptiveFps.Location = new Point(12, 26);
                    swAdaptiveFps.Size = new Size(colW - 24, 40);
                }
                if (swShowFps != null)
                {
                    swShowFps.Location = new Point(12, 70);
                    swShowFps.Size = new Size(colW - 24, 40);
                }
                if (swBatterySaver != null)
                {
                    swBatterySaver.Location = new Point(12, 114);
                    swBatterySaver.Size = new Size(colW - 24, 40);
                }
                if (swStartup != null)
                {
                    swStartup.Location = new Point(12, 158);
                    swStartup.Size = new Size(colW - 24, 40);
                }
            }

            // 4. Alt Komuta ve Canlı Telemetri Güvertesi
            int bottomY = colY + colH + 10;
            btnStartStop.Location = new Point(18, bottomY);
            btnStartStop.Size = new Size(w - 36, 46);

            pnlBottomBar.Location = new Point(18, bottomY + 52);
            pnlBottomBar.Size = new Size(w - 36, 46);

            int barW = w - 36;
            if (badgeLatency != null) badgeLatency.Location = new Point(barW - 86, 11);
            if (badgeSize != null) badgeSize.Location = new Point(barW - 176, 11);
            if (badgeBandwidth != null) badgeBandwidth.Location = new Point(barW - 276, 11);
            if (badgeFps != null) badgeFps.Location = new Point(barW - 356, 11);

            if (lblStatusSub != null)
            {
                lblStatusSub.Size = new Size(Math.Max(200, barW - 380), 16);
            }

            pnlLeftCol.ResumeLayout(false);
            pnlRightCol.ResumeLayout(false);
            this.ResumeLayout(false);

            heroBanner.Invalidate();
            pnlLeftCol.Invalidate(true);
            pnlRightCol.Invalidate(true);
            pnlBottomBar.Invalidate(true);
            this.Invalidate(true);
        }
    }
}