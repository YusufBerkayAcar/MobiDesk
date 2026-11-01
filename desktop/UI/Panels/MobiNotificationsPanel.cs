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
    public class MobiNotificationsPanel : DoubleBufferedPanel
    {
        internal MobiFlowManager flowManager;
        internal Panel pnlHeader;
        internal ModernButton btnClearAll;
        internal ModernButton btnStartupToggle;
        internal DoubleBufferedPanel pnlNotifsViewport;
        internal DoubleBufferedPanel pnlNotifsContent;
        internal ModernDarkScrollBar vScrollBar;
        internal Label lblTitle;
        internal Label lblCount;
        internal Label lblStatusDot;
        internal Label lblStatusText;
        internal Label lblEmptyHint;
        internal ModernComboBox cbDeviceFilter;

        internal readonly Dictionary<string, Control> cardMap = new Dictionary<string, Control>();
        internal readonly Dictionary<string, NotificationCardItem> notifItems = new Dictionary<string, NotificationCardItem>();
        internal readonly List<ConnectedDeviceItem> knownDevices = new List<ConnectedDeviceItem>();
        internal string currentFilterSerial = null;
        private bool isUpdatingDeviceFilter = false;

        public MobiNotificationsPanel(int x, int y, int width, int height, MobiFlowManager manager)
        {
            this.flowManager = manager;
            this.Location = new Point(x, y);
            this.Size = new Size(width, height);
            this.BackColor = MainForm.ColorBg;

            InitializeUI();
            this.Resize += delegate { UpdateLayout(); };
            this.VisibleChanged += delegate
            {
                if (this.Visible)
                {
                    UpdateLayout();
                    UpdateCount();
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
                Text = L10n.T("🔔 Bildirimler", "🔔 Notifications"),
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                ForeColor = MainForm.ColorText,
                Location = new Point(12, 10),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblTitle);

            lblCount = new Label
            {
                Text = "(0)",
                Font = new Font("Segoe UI", 9.0f, FontStyle.Regular),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(lblTitle.Right + 8, 11),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblCount);

            lblStatusDot = new Label
            {
                Text = "●",
                Font = new Font("Segoe UI", 10.0f, FontStyle.Bold),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(lblCount.Right + 8, 10),
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

            cbDeviceFilter = new ModernComboBox
            {
                Location = new Point(lblStatusText.Right + 12, 7),
                Size = new Size(160, 28)
            };
            cbDeviceFilter.Items.Add(L10n.T("🌐 Tüm Cihazlar", "🌐 All Devices"));
            cbDeviceFilter.SelectedIndex = 0;
            cbDeviceFilter.SelectedIndexChanged += delegate
            {
                if (isUpdatingDeviceFilter) return;
                string newFilter;
                if (cbDeviceFilter.SelectedIndex <= 0)
                {
                    newFilter = null;
                }
                else
                {
                    ConnectedDeviceItem sel = cbDeviceFilter.SelectedItem as ConnectedDeviceItem;
                    newFilter = sel != null ? sel.Serial : null;
                }
                if (!string.Equals(currentFilterSerial, newFilter, StringComparison.OrdinalIgnoreCase))
                {
                    currentFilterSerial = newFilter;
                    ApplyFilter();
                }
            };
            pnlHeader.Controls.Add(cbDeviceFilter);

            btnStartupToggle = new ModernButton("", w - 280, 7, 145, 28, Color.FromArgb(28, 38, 58), MainForm.ColorText, 6);
            btnStartupToggle.Font = new Font("Segoe UI", 8.0f, FontStyle.Bold);
            UpdateStartupButtonText();
            btnStartupToggle.Click += delegate
            {
                bool newState = !WindowsStartupManager.IsStartupEnabled();
                WindowsStartupManager.SetStartup(newState);
                UpdateStartupButtonText();
            };
            pnlHeader.Controls.Add(btnStartupToggle);

            btnClearAll = new ModernButton(L10n.T("🗑️ Tümünü Temizle", "🗑️ Clear All"), w - 122, 7, 114, 28, Color.FromArgb(28, 38, 58), MainForm.ColorText, 6);
            btnClearAll.Font = new Font("Segoe UI", 8.0f, FontStyle.Bold);
            btnClearAll.Click += delegate
            {
                cardMap.Clear();
                notifItems.Clear();
                List<Control> toRemove = new List<Control>();
                foreach (Control c in pnlNotifsContent.Controls)
                {
                    if (c != lblEmptyHint) toRemove.Add(c);
                }
                foreach (Control c in toRemove)
                {
                    pnlNotifsContent.Controls.Remove(c);
                    c.Dispose();
                }
                RearrangeCards();
                UpdateCount();
            };
            pnlHeader.Controls.Add(btnClearAll);
            this.Controls.Add(pnlHeader);

            // 2. Bildirimler Görünüm Alanı (Viewport + Content)
            pnlNotifsViewport = new DoubleBufferedPanel
            {
                Location = new Point(0, 44),
                Size = new Size(w, Math.Max(50, h - 46)),
                AutoScroll = false,
                BackColor = MainForm.ColorBg
            };

            pnlNotifsContent = new DoubleBufferedPanel
            {
                Location = new Point(0, 0),
                Size = new Size(w, Math.Max(50, h - 46)),
                BackColor = MainForm.ColorBg
            };
            pnlNotifsViewport.Controls.Add(pnlNotifsContent);
            this.Controls.Add(pnlNotifsViewport);

            // 3. Modern Koyu Kaydırma Çubuğu (Custom Dark ScrollBar)
            vScrollBar = new ModernDarkScrollBar
            {
                Location = new Point(w - 14, 48),
                Size = new Size(10, Math.Max(50, h - 54)),
                Visible = false
            };
            vScrollBar.ValueChanged += delegate
            {
                pnlNotifsContent.Top = -vScrollBar.Value;
            };
            this.Controls.Add(vScrollBar);
            vScrollBar.BringToFront();

            // Fare Tekerleği Desteği (Mouse Wheel)
            AttachWheel(pnlNotifsViewport);
            AttachWheel(pnlNotifsContent);

            // Boş Bildirim İpucu
            lblEmptyHint = new Label
            {
                Text = L10n.T(
                    "🔔 Henüz yeni bir bildirim yok\n\nTelefonunuz USB ile şarj olurken MobiDesk kapalı olsa bile gelen bildirimler buraya aktarılır.\n(Cihazınızda MobiDesk Bildirim İzninin açık olduğundan emin olun.)",
                    "🔔 No new notifications yet\n\nIncoming notifications mirror here even if MobiDesk is closed while charging over USB.\n(Make sure MobiDesk Notification Access is enabled on your device.)"
                ),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = MainForm.ColorTextMuted,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(Math.Max(200, w - 40), 120),
                Location = new Point(20, 60),
                Visible = true
            };
            pnlNotifsContent.Controls.Add(lblEmptyHint);
            lblEmptyHint.BringToFront();

            UpdateHeaderPositions();
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

        public void ApplyLanguage()
        {
            if (lblTitle != null) lblTitle.Text = L10n.T("🔔 Bildirimler", "🔔 Notifications");
            if (btnClearAll != null) btnClearAll.Text = L10n.T("🗑️ Tümünü Temizle", "🗑️ Clear All");
            UpdateStartupButtonText();
            if (lblEmptyHint != null)
            {
                lblEmptyHint.Text = L10n.T(
                    "🔔 Henüz yeni bir bildirim yok\n\nTelefonunuz USB ile şarj olurken MobiDesk kapalı olsa bile gelen bildirimler buraya aktarılır.\n(Cihazınızda MobiDesk Bildirim İzninin açık olduğundan emin olun.)",
                    "🔔 No new notifications yet\n\nIncoming notifications mirror here even if MobiDesk is closed while charging over USB.\n(Make sure MobiDesk Notification Access is enabled on your device.)"
                );
            }
            if (cbDeviceFilter != null && cbDeviceFilter.Items.Count > 0)
            {
                cbDeviceFilter.Items[0] = L10n.T("🌐 Tüm Cihazlar", "🌐 All Devices");
                cbDeviceFilter.Invalidate();
            }
            foreach (var card in cardMap.Values)
            {
                foreach (Control child in card.Controls)
                {
                    if (child is ModernButton && string.Equals(child.Tag as string, "BTN_DISMISS"))
                    {
                        child.Text = L10n.T("✕ Kapat", "✕ Dismiss");
                    }
                }
            }
            bool isConn = flowManager != null && flowManager.IsConnected();
            SetConnected(isConn);
            UpdateCount();
            UpdateHeaderPositions();
        }

        private void UpdateHeaderPositions()
        {
            if (pnlHeader == null || lblTitle == null || lblCount == null || lblStatusDot == null || lblStatusText == null || cbDeviceFilter == null || btnClearAll == null || btnStartupToggle == null) return;

            int curW = pnlHeader.ClientSize.Width;
            int rightMargin = 12;

            btnClearAll.Location = new Point(curW - btnClearAll.Width - rightMargin, 7);
            btnStartupToggle.Location = new Point(btnClearAll.Left - btnStartupToggle.Width - 8, 7);

            int maxLeftX = btnStartupToggle.Left - 12;

            int leftX = 12;
            lblTitle.Location = new Point(leftX, 10);
            leftX += lblTitle.Width + 6;

            lblCount.Location = new Point(leftX, 11);
            leftX += lblCount.Width + 6;

            lblStatusDot.Location = new Point(leftX, 10);
            leftX += lblStatusDot.Width + 2;

            lblStatusText.Location = new Point(leftX, 12);
            leftX += lblStatusText.Width + 10;

            int availFilterW = maxLeftX - leftX;
            if (availFilterW >= 130)
            {
                cbDeviceFilter.Visible = true;
                cbDeviceFilter.Location = new Point(leftX, 7);
                cbDeviceFilter.Width = Math.Min(170, availFilterW);
            }
            else if (availFilterW >= 80)
            {
                cbDeviceFilter.Visible = true;
                cbDeviceFilter.Location = new Point(leftX, 7);
                cbDeviceFilter.Width = availFilterW;
            }
            else
            {
                cbDeviceFilter.Visible = false;
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
                        !string.Equals(devices[i].FriendlyName, knownDevices[i].FriendlyName, StringComparison.OrdinalIgnoreCase))
                    {
                        devicesChanged = true;
                        break;
                    }
                }
            }

            if (!devicesChanged)
            {
                if (flowManager != null) SetConnected(flowManager.IsConnected());
                return;
            }

            knownDevices.Clear();
            if (devices != null) knownDevices.AddRange(devices);

            string prevFilter = currentFilterSerial;
            isUpdatingDeviceFilter = true;
            cbDeviceFilter.BeginUpdate();
            try
            {
                cbDeviceFilter.Items.Clear();
                cbDeviceFilter.Items.Add(L10n.T("🌐 Tüm Cihazlar", "🌐 All Devices"));

                int restoreIdx = 0;
                for (int i = 0; i < knownDevices.Count; i++)
                {
                    cbDeviceFilter.Items.Add(knownDevices[i]);
                    if (string.Equals(knownDevices[i].Serial, prevFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        restoreIdx = i + 1;
                    }
                }

                cbDeviceFilter.SelectedIndex = restoreIdx;
                if (restoreIdx == 0)
                {
                    currentFilterSerial = null;
                }
                else
                {
                    ConnectedDeviceItem sel = cbDeviceFilter.SelectedItem as ConnectedDeviceItem;
                    currentFilterSerial = sel != null ? sel.Serial : null;
                }
            }
            finally
            {
                cbDeviceFilter.EndUpdate();
                isUpdatingDeviceFilter = false;
            }

            ApplyFilter();
        }

        public void UpdateDeviceConnection(string serial, bool connected)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(delegate { UpdateDeviceConnection(serial, connected); }));
                return;
            }

            if (flowManager != null)
            {
                if (string.IsNullOrEmpty(currentFilterSerial))
                {
                    SetConnected(flowManager.IsConnected());
                }
                else if (string.Equals(currentFilterSerial, serial, StringComparison.OrdinalIgnoreCase))
                {
                    SetConnected(connected);
                }
            }
        }

        internal void UpdateStatusDisplay()
        {
            if (flowManager != null)
            {
                if (string.IsNullOrEmpty(currentFilterSerial))
                    SetConnected(flowManager.IsConnected());
                else
                    SetConnected(flowManager.IsConnected(currentFilterSerial));
            }
        }

        private void ApplyFilter()
        {
            if (pnlNotifsContent == null) return;
            pnlNotifsContent.SuspendLayout();
            foreach (var kvp in cardMap)
            {
                string key = kvp.Key;
                Control card = kvp.Value;
                NotificationCardItem item;
                if (notifItems.TryGetValue(key, out item))
                {
                    bool visible = string.IsNullOrEmpty(currentFilterSerial) ||
                                   string.Equals(item.DeviceSerial, currentFilterSerial, StringComparison.OrdinalIgnoreCase);
                    NotificationCardControl ncc = card as NotificationCardControl;
                    if (ncc != null) ncc.CardVisible = visible;
                    card.Visible = visible;
                }
            }
            RearrangeCards();
            pnlNotifsContent.ResumeLayout(true);
            UpdateCount();
        }

        public void UpdateLayout()
        {
            int curW = this.ClientSize.Width;
            int curH = this.ClientSize.Height;
            if (curW <= 0 || curH <= 0) return;

            int headerH = 42;
            if (pnlHeader != null)
            {
                pnlHeader.Location = new Point(0, 0);
                pnlHeader.Size = new Size(curW, headerH);
                UpdateHeaderPositions();
            }

            int scrollBarW = 10;
            if (vScrollBar != null)
            {
                vScrollBar.Location = new Point(curW - scrollBarW - 2, headerH + 6);
                vScrollBar.Size = new Size(scrollBarW, Math.Max(50, curH - headerH - 12));
            }

            if (pnlNotifsViewport != null)
            {
                int vpW = curW - (vScrollBar != null && vScrollBar.Visible ? (scrollBarW + 4) : 0);
                pnlNotifsViewport.Location = new Point(0, headerH + 2);
                pnlNotifsViewport.Size = new Size(vpW, Math.Max(50, curH - headerH - 4));
                if (lblEmptyHint != null)
                {
                    lblEmptyHint.Size = new Size(Math.Max(200, vpW - 40), 120);
                }
            }

            RearrangeCards();
        }

        public void UpdateStartupState()
        {
            UpdateStartupButtonText();
        }

        private void UpdateStartupButtonText()
        {
            if (btnStartupToggle == null) return;
            bool enabled = WindowsStartupManager.IsStartupEnabled();
            btnStartupToggle.Text = enabled
                ? L10n.T("🪟 Başlangıç: Açık", "🪟 Startup: On")
                : L10n.T("🪟 Başlangıç: Kapalı", "🪟 Startup: Off");
            btnStartupToggle.ForeColor = enabled
                ? MainForm.ColorSuccess
                : MainForm.ColorTextMuted;
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

        public void LoadMockNotifications()
        {
            string[] senders = new string[] { "WhatsApp", "Instagram", "Gmail", "Telegram", "Ziraat Mobil", "YouTube", "Twitter / X", "Slack", "Messages" };
            for (int mi = 0; mi < senders.Length; mi++)
            {
                AddNotification(new NotificationCardItem
                {
                    Id = 3000 + mi,
                    Key = "mock_key_" + mi,
                    PackageName = "com.mock." + senders[mi].ToLower().Replace(" ", ""),
                    AppName = senders[mi],
                    DeviceName = "Android Device",
                    Title = senders[mi] + " - Yeni Bildirim #" + (mi + 1),
                    Text = "Örnek bildirim içeriği ve detaylı mesaj metni burada gösterilir. (" + (mi + 1) + ")",
                    Timestamp = DateTime.Now.Ticks
                });
            }
        }

        public void AddNotification(NotificationCardItem item)
        {
            if (pnlNotifsContent == null || pnlNotifsViewport == null) return;
            if (lblEmptyHint != null) lblEmptyHint.Visible = false;

            if (cardMap.ContainsKey(item.Key))
            {
                RemoveNotification(item.Key);
            }

            notifItems[item.Key] = item;

            int viewportW = pnlNotifsViewport.ClientSize.Width;
            int scrollOffset = (vScrollBar != null && vScrollBar.Visible) ? 22 : 0;
            int cardW = Math.Max(200, viewportW - 24 - scrollOffset);

            NotificationCardControl card = new NotificationCardControl
            {
                Size = new Size(cardW, 72),
                Tag = item
            };

            // İkon
            PictureBox pbIcon = new PictureBox
            {
                Size = new Size(36, 36),
                Location = new Point(14, 18),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            item.AppName = NotificationHelper.ResolveAppName(item.AppName, item.PackageName);
            if (item.CachedIcon != null)
            {
                pbIcon.Image = item.CachedIcon;
            }
            else
            {
                pbIcon.Image = NotificationHelper.GetAppIconOrBadge(item.PackageName, item.AppName);
            }
            card.Controls.Add(pbIcon);

            // Bilgi ve metinler
            int textW = Math.Max(80, cardW - 58 - 74 - 16);
            string devTag = !string.IsNullOrEmpty(item.DeviceName) ? ("  •  [" + item.DeviceName + "]") : "";

            Label lblApp = new Label
            {
                Text = item.AppName + devTag + "  •  " + DateTime.Now.ToString("HH:mm"),
                Font = new Font("Segoe UI", 8.0f, FontStyle.Bold),
                ForeColor = MainForm.ColorAccent,
                Location = new Point(58, 8),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            card.Controls.Add(lblApp);

            Label lblTitle = new Label
            {
                Text = item.Title,
                Font = new Font("Segoe UI", 9.0f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(58, 26),
                Size = new Size(textW, 18),
                AutoEllipsis = true,
                Tag = "LBL_TITLE",
                BackColor = Color.Transparent
            };
            card.Controls.Add(lblTitle);

            Label lblBody = new Label
            {
                Text = item.Text,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(58, 46),
                Size = new Size(textW, 18),
                AutoEllipsis = true,
                Tag = "LBL_BODY",
                BackColor = Color.Transparent
            };
            card.Controls.Add(lblBody);

            // Kapat (Dismiss) Butonu
            ModernButton btnDismiss = new ModernButton(L10n.T("✕ Kapat", "✕ Dismiss"), cardW - 74, 22, 64, 28, Color.FromArgb(28, 38, 58), MainForm.ColorTextMuted, 5);
            btnDismiss.Font = new Font("Segoe UI", 8.0f, FontStyle.Bold);
            btnDismiss.Tag = "BTN_DISMISS";
            btnDismiss.Click += delegate
            {
                if (flowManager != null)
                {
                    try { flowManager.DismissNotification(item.PackageName, item.Id, item.Key, item.DeviceSerial); } catch { }
                }
                RemoveNotification(item.Key);
            };
            card.Controls.Add(btnDismiss);

            // Çocuklar üzerinde fare dolaşırken kartın ışıldaması
            foreach (Control child in card.Controls)
            {
                if (child != btnDismiss)
                {
                    child.MouseEnter += delegate { card.SetHover(true); };
                    child.MouseLeave += delegate
                    {
                        Point p = card.PointToClient(Cursor.Position);
                        if (!card.ClientRectangle.Contains(p)) card.SetHover(false);
                    };
                }
            }

            AttachWheel(card);

            cardMap[item.Key] = card;

            bool isVisible = string.IsNullOrEmpty(currentFilterSerial) ||
                             string.Equals(item.DeviceSerial, currentFilterSerial, StringComparison.OrdinalIgnoreCase);
            card.CardVisible = isVisible;
            card.Visible = isVisible;

            pnlNotifsContent.Controls.Add(card);
            RearrangeCards();
            UpdateCount();
        }

        public void RemoveNotification(string key)
        {
            if (pnlNotifsContent == null) return;
            Control c;
            if (cardMap.TryGetValue(key, out c))
            {
                cardMap.Remove(key);
                notifItems.Remove(key);
                pnlNotifsContent.Controls.Remove(c);
                c.Dispose();
                RearrangeCards();
                UpdateCount();
            }
        }

        public void RearrangeCards()
        {
            if (pnlNotifsViewport == null || pnlNotifsContent == null) return;

            int viewportH = pnlNotifsViewport.ClientSize.Height;
            if (viewportH <= 0) return;

            int visibleCount = 0;
            int estimatedTotalH = 10;
            foreach (Control c in pnlNotifsContent.Controls)
            {
                if (c == lblEmptyHint) continue;
                NotificationCardControl ncc = c as NotificationCardControl;
                bool isCardVis = (ncc != null) ? ncc.CardVisible : c.Visible;
                if (!isCardVis) continue;
                estimatedTotalH += c.Height + 8;
                visibleCount++;
            }

            bool needsScroll = (estimatedTotalH > viewportH);
            if (vScrollBar != null)
            {
                vScrollBar.Visible = (visibleCount > 0 && needsScroll);
            }

            int scrollBarW = 10;
            int vpW = this.ClientSize.Width - (vScrollBar != null && vScrollBar.Visible ? (scrollBarW + 4) : 0);
            pnlNotifsViewport.Size = new Size(vpW, Math.Max(50, this.ClientSize.Height - 44 - 4));
            pnlNotifsContent.Width = vpW;

            int targetCardW = Math.Max(200, vpW - 24);

            int curY = 10;
            foreach (Control c in pnlNotifsContent.Controls)
            {
                if (c == lblEmptyHint) continue;
                NotificationCardControl ncc = c as NotificationCardControl;
                bool isCardVis = (ncc != null) ? ncc.CardVisible : c.Visible;
                if (!isCardVis) continue;

                c.Location = new Point(12, curY);
                c.Width = targetCardW;

                // Kartın içindeki kontrolleri dinamik yeniden konumlandır
                foreach (Control child in c.Controls)
                {
                    if (child is ModernButton && string.Equals(child.Tag as string, "BTN_DISMISS"))
                    {
                        child.Location = new Point(targetCardW - child.Width - 12, (c.Height - child.Height) / 2);
                    }
                    else if (child is Label)
                    {
                        string tag = child.Tag as string;
                        if (tag == "LBL_TITLE" || tag == "LBL_BODY")
                        {
                            int textW = Math.Max(80, targetCardW - 58 - 74 - 16);
                            child.Width = textW;
                        }
                    }
                }

                curY += c.Height + 8;
            }

            int totalContentH = Math.Max(viewportH, curY + 12);
            pnlNotifsContent.Height = totalContentH;

            if (vScrollBar != null && vScrollBar.Visible)
            {
                vScrollBar.Minimum = 0;
                vScrollBar.Maximum = totalContentH;
                vScrollBar.LargeChange = viewportH;
                int maxVal = Math.Max(0, totalContentH - viewportH);
                if (vScrollBar.Value > maxVal) vScrollBar.Value = maxVal;
                pnlNotifsContent.Top = -vScrollBar.Value;
            }
            else
            {
                if (vScrollBar != null) vScrollBar.Value = 0;
                pnlNotifsContent.Top = 0;
            }

            if (lblEmptyHint != null)
            {
                bool showEmpty = (visibleCount == 0);
                lblEmptyHint.Visible = showEmpty;
                if (showEmpty)
                {
                    lblEmptyHint.Location = new Point(20, 60);
                    lblEmptyHint.Size = new Size(Math.Max(200, vpW - 40), 120);
                    lblEmptyHint.BringToFront();
                }
                else
                {
                    lblEmptyHint.SendToBack();
                }
            }
        }

        public void UpdateCount()
        {
            if (lblCount != null)
            {
                int count = 0;
                foreach (var c in cardMap.Values)
                {
                    NotificationCardControl ncc = c as NotificationCardControl;
                    bool isCardVis = (ncc != null) ? ncc.CardVisible : c.Visible;
                    if (isCardVis) count++;
                }
                lblCount.Text = string.Format("({0})", count);
                UpdateHeaderPositions();
            }
        }
    }

}