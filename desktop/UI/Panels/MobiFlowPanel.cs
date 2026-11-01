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
    public class MobiFlowPanel : DoubleBufferedPanel
    {
        private MobiFlowManager flowManager;
        private Panel pnlHeader;
        private Panel pnlBottom;
        internal DoubleBufferedPanel pnlFlowViewport;
        internal DoubleBufferedPanel pnlMessages;
        internal ModernDarkScrollBar vScrollBar;
        private TextBox txtInput;
        private ModernButton btnSend;
        private ModernButton btnSendFile;
        private ModernButton btnOpenFolder;
        private Label lblTitle;
        private Label lblStatusDot;
        private Label lblStatusText;
        private Label lblDeviceLabel;
        private ModernComboBox cbDeviceSelect;

        private readonly List<FlowMessageItem> messageHistory = new List<FlowMessageItem>();
        private readonly List<ConnectedDeviceItem> knownDevices = new List<ConnectedDeviceItem>();
        private string currentSelectedSerial = null;
        private bool isUpdatingDeviceList = false;

        public string SelectedDeviceSerial
        {
            get { return currentSelectedSerial; }
        }

        public string CurrentSelectedSerial
        {
            get { return currentSelectedSerial; }
        }

        public MobiFlowPanel(int x, int y, int width, int height, MobiFlowManager manager)
        {
            this.flowManager = manager;
            this.Location = new Point(x, y);
            this.Size = new Size(width, height);
            this.BackColor = MainForm.ColorBg;
            this.AllowDrop = true;

            InitializeUI();
            this.Resize += delegate { UpdateLayout(); };
            this.VisibleChanged += delegate
            {
                if (this.Visible)
                {
                    UpdateLayout();
                }
            };
            UpdateLayout();
        }

        private void InitializeUI()
        {
            this.Controls.Clear();
            int w = this.Width;
            int h = this.Height;

            // 1. Üst Başlık & Eylemler
            pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(w, 42),
                BackColor = MainForm.ColorCard
            };

            lblTitle = new Label
            {
                Text = L10n.T("💬 MobiDesk Flow", "💬 MobiDesk Flow"),
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                ForeColor = MainForm.ColorText,
                Location = new Point(12, 10),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblTitle);

            lblStatusDot = new Label
            {
                Text = "●",
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(lblTitle.Right + 8, 10),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblStatusDot);

            lblStatusText = new Label
            {
                Text = L10n.T("Bekleniyor", "Waiting"),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(lblStatusDot.Right + 2, 12),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblStatusText);

            lblDeviceLabel = new Label
            {
                Text = L10n.T("Cihaz:", "Device:"),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(lblStatusText.Right + 14, 12),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblDeviceLabel);

            cbDeviceSelect = new ModernComboBox
            {
                Location = new Point(lblDeviceLabel.Right + 8, 7),
                Size = new Size(220, 28)
            };
            cbDeviceSelect.SelectedIndexChanged += delegate
            {
                if (isUpdatingDeviceList) return;
                ConnectedDeviceItem sel = cbDeviceSelect.SelectedItem as ConnectedDeviceItem;
                string newSerial = sel != null ? sel.Serial : null;
                if (!string.Equals(currentSelectedSerial, newSerial, StringComparison.OrdinalIgnoreCase))
                {
                    currentSelectedSerial = newSerial;
                    UpdateStatusDisplay();
                    RebuildMessagesList();
                }
            };
            pnlHeader.Controls.Add(cbDeviceSelect);

            btnOpenFolder = new ModernButton(L10n.T("📂 İndirilenler", "📂 Downloads"), w - 140, 7, 130, 28, Color.FromArgb(28, 38, 58), MainForm.ColorText, 6);
            btnOpenFolder.Font = new Font("Segoe UI", 8.0f, FontStyle.Bold);
            btnOpenFolder.Click += delegate
            {
                try { Process.Start("explorer.exe", MobiFlowManager.DownloadsFolder); } catch { }
            };
            pnlHeader.Controls.Add(btnOpenFolder);
            this.Controls.Add(pnlHeader);

            // 2. Alt Mesaj Yazma ve Dosya Gönderme Çubuğu
            int bottomH = 48;
            pnlBottom = new Panel
            {
                Location = new Point(0, h - bottomH),
                Size = new Size(w, bottomH),
                BackColor = MainForm.ColorCard
            };

            btnSendFile = new ModernButton(L10n.T("📎 Dosya Gönder", "📎 Send File"), 10, 8, 120, 32, Color.FromArgb(28, 38, 58), MainForm.ColorText, 6);
            btnSendFile.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnSendFile.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog { Title = L10n.T("MobiDesk Flow - Gönderilecek Dosyayı Seçin", "MobiDesk Flow - Select File to Send"), Multiselect = true })
                {
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        foreach (string file in ofd.FileNames)
                        {
                            flowManager.SendFile(file, currentSelectedSerial);
                        }
                    }
                }
            };
            pnlBottom.Controls.Add(btnSendFile);

            btnSend = new ModernButton(L10n.T("➤ Gönder", "➤ Send"), w - 90, 8, 80, 32, MainForm.ColorPrimary, Color.White, 6);
            btnSend.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            btnSend.Click += delegate { SendCurrentInput(); };
            pnlBottom.Controls.Add(btnSend);

            txtInput = new TextBox
            {
                Location = new Point(140, 11),
                Size = new Size(Math.Max(100, w - 240), 26),
                BackColor = MainForm.ColorBg,
                ForeColor = MainForm.ColorText,
                Font = new Font("Segoe UI", 10.0f, FontStyle.Regular),
                BorderStyle = BorderStyle.FixedSingle
            };
            txtInput.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    SendCurrentInput();
                }
            };
            pnlBottom.Controls.Add(txtInput);
            this.Controls.Add(pnlBottom);

            UpdateHeaderPositions();

            // 3. Mesaj Görünüm Alanı (Viewport + Content)
            int viewportH = Math.Max(50, h - 44 - bottomH - 2);
            pnlFlowViewport = new DoubleBufferedPanel
            {
                Location = new Point(0, 44),
                Size = new Size(w, viewportH),
                AutoScroll = false,
                BackColor = MainForm.ColorBg
            };

            pnlMessages = new DoubleBufferedPanel
            {
                Location = new Point(0, 0),
                Size = new Size(w, viewportH),
                BackColor = MainForm.ColorBg,
                AllowDrop = true
            };
            pnlFlowViewport.Controls.Add(pnlMessages);
            this.Controls.Add(pnlFlowViewport);

            // 4. Modern Koyu Kaydırma Çubuğu (Custom Dark ScrollBar)
            vScrollBar = new ModernDarkScrollBar
            {
                Location = new Point(w - 14, 48),
                Size = new Size(10, Math.Max(20, viewportH - 8)),
                Visible = false
            };
            vScrollBar.ValueChanged += delegate
            {
                pnlMessages.Top = -vScrollBar.Value;
            };
            this.Controls.Add(vScrollBar);
            vScrollBar.BringToFront();

            // Fare Tekerleği Desteği (Mouse Wheel)
            AttachWheel(pnlFlowViewport);
            AttachWheel(pnlMessages);

            // Sürükle-Bırak Olayları
            SetupDragAndDrop(this);
            SetupDragAndDrop(pnlFlowViewport);
            SetupDragAndDrop(pnlMessages);
        }

        private void AttachWheel(Control c)
        {
            if (c == null) return;
            c.MouseWheel += delegate(object s, MouseEventArgs e)
            {
                if (vScrollBar != null && vScrollBar.Visible)
                {
                    int step = (e.Delta < 0) ? 60 : -60;
                    vScrollBar.Value += step;
                }
            };
            foreach (Control child in c.Controls)
            {
                AttachWheel(child);
            }
        }

        public void UpdateDeviceList(List<ConnectedDeviceItem> devices)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(delegate { UpdateDeviceList(devices); }));
                return;
            }

            bool devicesChanged = false;
            if (devices == null && knownDevices.Count > 0) devicesChanged = true;
            else if (devices != null && devices.Count != knownDevices.Count) devicesChanged = true;
            else if (devices != null)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    if (!string.Equals(devices[i].Serial, knownDevices[i].Serial, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(devices[i].FriendlyName, knownDevices[i].FriendlyName, StringComparison.OrdinalIgnoreCase) ||
                        devices[i].FlowPort != knownDevices[i].FlowPort)
                    {
                        devicesChanged = true;
                        break;
                    }
                }
            }

            if (!devicesChanged)
            {
                UpdateStatusDisplay();
                return;
            }

            knownDevices.Clear();
            if (devices != null) knownDevices.AddRange(devices);

            string previousSerial = currentSelectedSerial;
            isUpdatingDeviceList = true;
            cbDeviceSelect.BeginUpdate();
            try
            {
                cbDeviceSelect.Items.Clear();

                int selectedIndexToRestore = -1;
                for (int i = 0; i < knownDevices.Count; i++)
                {
                    cbDeviceSelect.Items.Add(knownDevices[i]);
                    if (string.Equals(knownDevices[i].Serial, previousSerial, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndexToRestore = i;
                    }
                }

                if (cbDeviceSelect.Items.Count > 0)
                {
                    cbDeviceSelect.SelectedIndex = (selectedIndexToRestore >= 0) ? selectedIndexToRestore : 0;
                    ConnectedDeviceItem sel = cbDeviceSelect.SelectedItem as ConnectedDeviceItem;
                    currentSelectedSerial = sel != null ? sel.Serial : null;
                }
                else
                {
                    currentSelectedSerial = null;
                }
            }
            finally
            {
                cbDeviceSelect.EndUpdate();
                isUpdatingDeviceList = false;
            }

            UpdateStatusDisplay();
            RebuildMessagesList();
        }

        public void UpdateDeviceConnection(string serial, bool connected)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(delegate { UpdateDeviceConnection(serial, connected); }));
                return;
            }

            if (string.Equals(currentSelectedSerial, serial, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(currentSelectedSerial))
            {
                SetConnected(connected);
            }
        }

        internal void UpdateStatusDisplay()
        {
            bool isConn = flowManager != null && flowManager.IsConnected(currentSelectedSerial);
            SetConnected(isConn);
        }

        public void UpdateLayout()
        {
            int curW = this.ClientSize.Width;
            int curH = this.ClientSize.Height;
            if (curW <= 0 || curH <= 0) return;

            int headerH = 42;
            int bottomH = 48;

            if (pnlHeader != null)
            {
                pnlHeader.Location = new Point(0, 0);
                pnlHeader.Size = new Size(curW, headerH);
                if (btnOpenFolder != null)
                {
                    btnOpenFolder.Location = new Point(curW - 140, 7);
                }
            }

            if (pnlBottom != null)
            {
                pnlBottom.Location = new Point(0, curH - bottomH);
                pnlBottom.Size = new Size(curW, bottomH);

                if (btnSendFile != null)
                {
                    btnSendFile.Location = new Point(10, 8);
                }

                if (btnSend != null)
                {
                    btnSend.Location = new Point(curW - 90, 8);
                }

                if (txtInput != null)
                {
                    int inputX = 140;
                    int inputW = Math.Max(100, curW - inputX - 100);
                    txtInput.Location = new Point(inputX, 11);
                    txtInput.Size = new Size(inputW, 26);
                }
            }

            int scrollBarW = 10;
            int viewportY = headerH + 2;
            int viewportH = Math.Max(50, curH - headerH - bottomH - 4);

            if (vScrollBar != null)
            {
                vScrollBar.Location = new Point(curW - scrollBarW - 2, viewportY + 2);
                vScrollBar.Size = new Size(scrollBarW, Math.Max(20, viewportH - 4));
            }

            if (pnlFlowViewport != null)
            {
                int vpW = curW - (vScrollBar != null && vScrollBar.Visible ? (scrollBarW + 4) : 0);
                pnlFlowViewport.Location = new Point(0, viewportY);
                pnlFlowViewport.Size = new Size(vpW, viewportH);
            }

            RearrangeMessages(false);
        }

        public void RearrangeMessages(bool scrollToBottom = false)
        {
            if (pnlFlowViewport == null || pnlMessages == null) return;

            int viewportH = pnlFlowViewport.ClientSize.Height;
            if (viewportH <= 0) return;

            int curW = this.ClientSize.Width;
            if (curW <= 0) return;

            // 1. Tahmini toplam yükseklik ve kaydırma çubuğu gereksinimi
            int estimatedTotalH = 10;
            int visibleCount = 0;
            foreach (Control c in pnlMessages.Controls)
            {
                estimatedTotalH += c.Height + 10;
                visibleCount++;
            }

            bool needsScroll = (visibleCount > 0 && estimatedTotalH > viewportH);
            if (vScrollBar != null)
            {
                vScrollBar.Visible = needsScroll;
            }

            int scrollBarW = 10;
            int vpW = curW - (vScrollBar != null && vScrollBar.Visible ? (scrollBarW + 4) : 0);
            if (vpW <= 50) vpW = 400;

            pnlFlowViewport.Size = new Size(vpW, viewportH);
            pnlMessages.Width = vpW;

            int panelW = vpW - 24;
            int bubbleMaxW = Math.Max(260, (int)(panelW * 0.70f));

            pnlMessages.SuspendLayout();

            int curY = 10;
            foreach (Control c in pnlMessages.Controls)
            {
                Panel bubble = c as Panel;
                if (bubble == null) continue;

                bool isMe = string.Equals(bubble.AccessibleName, "ME", StringComparison.OrdinalIgnoreCase);

                // İç kontrolleri sırala ve dinamik genişlik/yükseklik uyarla
                List<Control> children = new List<Control>();
                foreach (Control child in bubble.Controls) children.Add(child);
                children.Sort(delegate(Control a, Control b)
                {
                    if (a.TabIndex != b.TabIndex) return a.TabIndex.CompareTo(b.TabIndex);
                    return a.Top.CompareTo(b.Top);
                });

                int innerY = 6;
                int maxInnerW = 0;

                foreach (Control child in children)
                {
                    Label lbl = child as Label;
                    if (lbl != null)
                    {
                        lbl.MaximumSize = new Size(bubbleMaxW - 24, 0);
                        Size pref = lbl.GetPreferredSize(new Size(bubbleMaxW - 24, 0));
                        lbl.Size = pref;
                        child.Location = new Point(10, innerY);
                        innerY += pref.Height + 3;
                        if (pref.Width > maxInnerW) maxInnerW = pref.Width;
                    }
                    else
                    {
                        child.Location = new Point(10, innerY);
                        innerY += child.Height + 4;
                        if (child.Width > maxInnerW) maxInnerW = child.Width;
                    }
                }

                int desiredW = Math.Min(bubbleMaxW, Math.Max(160, maxInnerW + 24));
                bubble.Size = new Size(desiredW, innerY + 4);

                int bubbleX = isMe ? Math.Max(12, vpW - bubble.Width - 14) : 12;
                bubble.Location = new Point(bubbleX, curY);

                curY += bubble.Height + 10;
            }

            int totalContentH = Math.Max(viewportH, curY + 16);
            pnlMessages.Height = totalContentH;

            if (vScrollBar != null && vScrollBar.Visible)
            {
                vScrollBar.Minimum = 0;
                vScrollBar.Maximum = totalContentH;
                vScrollBar.LargeChange = viewportH;
                int maxVal = Math.Max(0, totalContentH - viewportH);
                if (scrollToBottom)
                {
                    vScrollBar.Value = maxVal;
                }
                else
                {
                    if (vScrollBar.Value > maxVal) vScrollBar.Value = maxVal;
                }
                pnlMessages.Top = -vScrollBar.Value;
            }
            else
            {
                if (vScrollBar != null) vScrollBar.Value = 0;
                pnlMessages.Top = 0;
            }

            pnlMessages.ResumeLayout(true);
        }

        public void UpdateScrollBounds(bool scrollToBottom)
        {
            RearrangeMessages(scrollToBottom);
        }

        private void SetupDragAndDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                }
            };
            c.DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null)
                {
                    foreach (string f in files)
                    {
                        flowManager.SendFile(f, currentSelectedSerial);
                    }
                }
            };
        }

        private void SendCurrentInput()
        {
            string text = txtInput.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            txtInput.Text = "";
            flowManager.SendTextMessage(text, currentSelectedSerial);
        }

        public void ApplyLanguage()
        {
            if (lblTitle != null) lblTitle.Text = L10n.T("💬 MobiDesk Flow", "💬 MobiDesk Flow");
            if (lblDeviceLabel != null) lblDeviceLabel.Text = L10n.T("Cihaz:", "Device:");
            if (btnOpenFolder != null) btnOpenFolder.Text = L10n.T("📂 İndirilenler", "📂 Downloads");
            if (btnSendFile != null) btnSendFile.Text = L10n.T("📎 Dosya Gönder", "📎 Send File");
            if (btnSend != null) btnSend.Text = L10n.T("➤ Gönder", "➤ Send");

            bool isConn = flowManager != null && flowManager.IsConnected(currentSelectedSerial);
            SetConnected(isConn);
            UpdateHeaderPositions();
            RebuildMessagesList();
        }

        private void UpdateHeaderPositions()
        {
            if (lblTitle == null || lblStatusDot == null || lblStatusText == null || lblDeviceLabel == null || cbDeviceSelect == null) return;
            lblStatusDot.Location = new Point(lblTitle.Right + 8, 10);
            lblStatusText.Location = new Point(lblStatusDot.Right + 2, 12);
            lblDeviceLabel.Location = new Point(lblStatusText.Right + 14, 12);
            cbDeviceSelect.Location = new Point(lblDeviceLabel.Right + 8, 7);
        }

        public void SetConnected(bool connected)
        {
            if (lblStatusDot != null && lblStatusText != null)
            {
                Color targetDot = connected ? MainForm.ColorSuccess : MainForm.ColorTextMuted;
                string targetText = connected ? L10n.T("Bağlandı", "Connected") : L10n.T("Bekleniyor", "Waiting");
                if (lblStatusDot.ForeColor != targetDot || !string.Equals(lblStatusText.Text, targetText, StringComparison.Ordinal))
                {
                    lblStatusDot.ForeColor = targetDot;
                    lblStatusText.Text = targetText;
                    lblStatusText.ForeColor = targetDot;
                    UpdateHeaderPositions();
                }
            }
        }

        public void AddMessage(FlowMessageItem item)
        {
            if (pnlMessages == null) return;
            messageHistory.Add(item);

            // Gelen mesaj başka bir cihazdan ise otomatik olarak o cihaza odaklan
            if (item.Sender == "mobile" && !string.IsNullOrEmpty(item.DeviceSerial))
            {
                if (!string.Equals(currentSelectedSerial, item.DeviceSerial, StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < knownDevices.Count; i++)
                    {
                        if (string.Equals(knownDevices[i].Serial, item.DeviceSerial, StringComparison.OrdinalIgnoreCase))
                        {
                            cbDeviceSelect.SelectedIndex = i;
                            currentSelectedSerial = item.DeviceSerial;
                            UpdateStatusDisplay();
                            RebuildMessagesList();
                            return;
                        }
                    }
                }
            }

            if (ShouldShowMessage(item))
            {
                RenderMessageBubble(item);
            }
        }

        private bool ShouldShowMessage(FlowMessageItem item)
        {
            if (string.IsNullOrEmpty(currentSelectedSerial)) return true;
            if (string.Equals(item.DeviceSerial, currentSelectedSerial, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void RebuildMessagesList()
        {
            if (pnlMessages == null) return;
            pnlMessages.SuspendLayout();
            pnlMessages.Controls.Clear();

            foreach (var m in messageHistory)
            {
                if (ShouldShowMessage(m))
                {
                    RenderMessageBubble(m, false);
                }
            }
            pnlMessages.ResumeLayout(true);
            RearrangeMessages(true);
        }

        private void RenderMessageBubble(FlowMessageItem item)
        {
            RenderMessageBubble(item, true);
        }

        private void RenderMessageBubble(FlowMessageItem item, bool updateScroll)
        {
            bool isMe = (item.Sender == "pc");
            int curW = pnlFlowViewport != null ? pnlFlowViewport.ClientSize.Width : (this.ClientSize.Width - 14);
            if (curW <= 0) curW = 400;
            int panelW = curW - 24;
            int bubbleMaxW = Math.Max(260, (int)(panelW * 0.70f));

            DoubleBufferedPanel bubble = new DoubleBufferedPanel();
            bubble.AccessibleName = isMe ? "ME" : "OTHER";
            bubble.BackColor = isMe ? Color.FromArgb(29, 78, 216) : MainForm.ColorCard;
            bubble.Padding = new Padding(10, 8, 10, 8);
            bubble.Tag = item.Id;

            int curY = 6;

            if (knownDevices.Count > 1 && !string.IsNullOrEmpty(item.DeviceName))
            {
                Label lblDevBadge = new Label
                {
                    Text = (isMe ? "PC ➔ " : "") + item.DeviceName,
                    Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                    ForeColor = isMe ? Color.FromArgb(191, 219, 254) : MainForm.ColorAccent,
                    Location = new Point(10, curY),
                    MaximumSize = new Size(bubbleMaxW - 24, 0),
                    AutoSize = true,
                    Tag = "LBL_DEV_BADGE",
                    TabIndex = 0
                };
                bubble.Controls.Add(lblDevBadge);
                curY += 16;
            }

            if (item.IsFile)
            {
                Label lblFileName = new Label
                {
                    Text = "📁 " + (item.FileName ?? L10n.T("Dosya", "File")),
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    ForeColor = Color.White,
                    Location = new Point(10, curY),
                    MaximumSize = new Size(bubbleMaxW - 24, 0),
                    AutoSize = true,
                    Tag = "LBL_FILE_NAME",
                    TabIndex = 1
                };
                bubble.Controls.Add(lblFileName);
                curY += 20;

                long sizeKb = item.FileSize / 1024;
                string sizeStr = (sizeKb > 1024) ? string.Format("{0:F1} MB", sizeKb / 1024.0) : sizeKb + " KB";
                Label lblSize = new Label
                {
                    Text = (item.Progress < 100) ? string.Format(L10n.T("Aktarılıyor: %{0} • {1}", "Transferring: %{0} • {1}"), item.Progress, sizeStr) : (L10n.T("Tamamlandı • ", "Completed • ") + sizeStr),
                    Font = new Font("Segoe UI", 8.0f, FontStyle.Regular),
                    ForeColor = MainForm.ColorTextMuted,
                    Location = new Point(10, curY),
                    MaximumSize = new Size(bubbleMaxW - 24, 0),
                    AutoSize = true,
                    Tag = "FILE_STATUS_LABEL",
                    TabIndex = 2
                };
                bubble.Controls.Add(lblSize);
                curY += 20;

                if (!string.IsNullOrEmpty(item.FilePath))
                {
                    LinkLabel lnkOpen = new LinkLabel
                    {
                        Text = L10n.T("📂 Klasörde Göster", "📂 Show in Folder"),
                        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                        LinkColor = Color.FromArgb(56, 189, 248),
                        Location = new Point(10, curY),
                        MaximumSize = new Size(bubbleMaxW - 24, 0),
                        AutoSize = true,
                        Tag = "LNK_OPEN",
                        TabIndex = 3
                    };
                    string target = item.FilePath;
                    lnkOpen.LinkClicked += delegate
                    {
                        try { Process.Start("explorer.exe", "/select,\"" + target + "\""); } catch { }
                    };
                    bubble.Controls.Add(lnkOpen);
                    curY += 22;
                }
            }
            else
            {
                Label lblText = new Label
                {
                    Text = item.Text,
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                    ForeColor = Color.White,
                    Location = new Point(10, curY),
                    MaximumSize = new Size(bubbleMaxW - 24, 0),
                    AutoSize = true,
                    Tag = "LBL_TEXT",
                    TabIndex = 4
                };
                bubble.Controls.Add(lblText);
                curY += lblText.Height + 4;

                if (item.Text != null && (item.Text.StartsWith("http://") || item.Text.StartsWith("https://")))
                {
                    LinkLabel lnkUrl = new LinkLabel
                    {
                        Text = L10n.T("🌐 Tarayıcıda Aç", "🌐 Open in Browser"),
                        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                        LinkColor = Color.FromArgb(56, 189, 248),
                        Location = new Point(10, curY),
                        MaximumSize = new Size(bubbleMaxW - 24, 0),
                        AutoSize = true,
                        Tag = "LNK_URL",
                        TabIndex = 5
                    };
                    string url = item.Text;
                    lnkUrl.LinkClicked += delegate { try { Process.Start(url); } catch { } };
                    bubble.Controls.Add(lnkUrl);
                    curY += 20;
                }
            }

            Label lblTime = new Label
            {
                Text = DateTime.Now.ToString("HH:mm"),
                Font = new Font("Segoe UI", 7.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(160, 203, 213, 225),
                Location = new Point(10, curY),
                MaximumSize = new Size(bubbleMaxW - 24, 0),
                AutoSize = true,
                Tag = "LBL_TIME",
                TabIndex = 6
            };
            bubble.Controls.Add(lblTime);
            curY += 18;

            int lastY = 10;
            if (pnlMessages.Controls.Count > 0)
            {
                Control lastC = pnlMessages.Controls[pnlMessages.Controls.Count - 1];
                lastY = lastC.Bottom + 10;
            }

            int bubbleX = isMe ? Math.Max(12, curW - bubbleMaxW - 14) : 12;
            bubble.Location = new Point(bubbleX, lastY);
            bubble.Size = new Size(bubbleMaxW, curY + 4);

            AttachWheel(bubble);
            SetupDragAndDrop(bubble);
            pnlMessages.Controls.Add(bubble);

            if (updateScroll)
            {
                RearrangeMessages(true);
            }
        }

        public void UpdateFileProgress(FlowMessageItem item)
        {
            if (pnlMessages == null) return;
            foreach (Control c in pnlMessages.Controls)
            {
                if (c is Panel && string.Equals(c.Tag as string, item.Id))
                {
                    foreach (Control child in c.Controls)
                    {
                        if (child is Label && string.Equals(child.Tag as string, "FILE_STATUS_LABEL"))
                        {
                            long sizeKb = item.FileSize / 1024;
                            string sizeStr = (sizeKb > 1024) ? string.Format("{0:F1} MB", sizeKb / 1024.0) : sizeKb + " KB";
                            child.Text = (item.Progress < 100)
                                ? string.Format(L10n.T("Aktarılıyor: %{0} • {1}", "Transferring: %{0} • {1}"), item.Progress, sizeStr)
                                : (L10n.T("Tamamlandı • ", "Completed • ") + sizeStr);
                        }
                    }
                }
            }
        }
    }

}