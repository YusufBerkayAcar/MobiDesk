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
    public enum VirtualModalMode
    {
        GeneralInfo,
        InstallSuccess,
        RemoveSuccess
    }

    public class VirtualDisplayInfoModal : Form
    {
        private VirtualModalMode mode;

        public VirtualDisplayInfoModal() : this(VirtualModalMode.GeneralInfo)
        {
        }

        public VirtualDisplayInfoModal(VirtualModalMode mode)
        {
            this.mode = mode;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            int modalW = (mode == VirtualModalMode.RemoveSuccess) ? 500 : 540;
            int modalH = (mode == VirtualModalMode.RemoveSuccess) ? 260 : 446;
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
                    if (this.mode != VirtualModalMode.RemoveSuccess)
                    {
                        OpenDisplaySettings();
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

        private void OpenDisplaySettings()
        {
            try
            {
                Process.Start("ms-settings:display");
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
            Color badgeBorder = mode == VirtualModalMode.InstallSuccess ? MainForm.ColorSuccess :
                                (mode == VirtualModalMode.RemoveSuccess ? Color.FromArgb(244, 114, 182) : MainForm.ColorAccent);
            Color badgeBg = mode == VirtualModalMode.InstallSuccess ? Color.FromArgb(16, 36, 26) :
                            (mode == VirtualModalMode.RemoveSuccess ? Color.FromArgb(36, 20, 30) : Color.FromArgb(24, 32, 48));
            string badgeChar = (mode == VirtualModalMode.InstallSuccess || mode == VirtualModalMode.RemoveSuccess) ? "✓" : "ℹ";

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
            string titleText = mode == VirtualModalMode.InstallSuccess
                ? L10n.T("Sanal Ekran Başarıyla Eklendi!", "Virtual Display Added Successfully!")
                : (mode == VirtualModalMode.RemoveSuccess
                    ? L10n.T("Sanal Ekran Devre Dışı Bırakıldı", "Virtual Display Disabled")
                    : L10n.T("Sanal Ekran Düzeni & Konumlandırma", "Virtual Display Layout & Positioning"));

            string subText = mode == VirtualModalMode.InstallSuccess
                ? L10n.T("Cihazınız artık 1:1 piksel netliğinde harici ekran olarak hazır", "Your device is now ready as a crisp 1:1 pixel external display")
                : (mode == VirtualModalMode.RemoveSuccess
                    ? L10n.T("Sanal monitör sürücüsü başarıyla sistemden kaldırıldı", "Virtual monitor driver has been disabled successfully")
                    : L10n.T("Windows Görüntü Ayarları üzerinden kolayca yönetilebilir", "Easily configured via Windows Display Settings"));

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
            int cardH = (mode == VirtualModalMode.RemoveSuccess) ? 104 : 300;
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

            if (mode == VirtualModalMode.RemoveSuccess)
            {
                AddInfoItem(pnlCard, 3,
                    L10n.T("Sistem Varsayılanına Döndürüldü", "Restored to System Defaults"),
                    L10n.T("Sanal monitör sürücüsü sistemden kaldırıldı. Ekran listeniz varsayılan donanım monitörlerinize güncellendi.",
                           "Virtual monitor driver was removed. Your display list has been restored to default hardware monitors."),
                    12, 10, cardInnerW);
            }
            else if (mode == VirtualModalMode.InstallSuccess)
            {
                int itemY = 12;
                AddInfoItem(pnlCard, 0,
                    L10n.T("Ekran Konumunu Belirleyin", "Position Your Display"),
                    L10n.T("Sanal ekran ana monitörünüzün yanına eklendi. Fare imlecinizin geçiş yönünü ayarlamak için Windows Görüntü Ayarları'ndan sürükleyebilirsiniz.",
                           "Virtual display added next to your monitor. You can drag and position it in Windows Display Settings to choose mouse transit direction."),
                    12, itemY, cardInnerW);
                itemY += 94;

                AddInfoItem(pnlCard, 1,
                    L10n.T("Ölçek ve Netlik (DPI)", "Scaling & Sharpness (DPI)"),
                    L10n.T("Tablette simgeleri büyütmek için %125, masaüstünüzü tam 1:1 piksel netliğinde genişletmek için %100 ölçeğini seçebilirsiniz.",
                           "Choose 125% for larger tablet elements or 100% for crisp 1:1 pixel precision."),
                    12, itemY, cardInnerW);
                itemY += 94;

                AddInfoItem(pnlCard, 2,
                    L10n.T("Yayını Başlatmaya Hazır", "Ready for Streaming"),
                    L10n.T("Ekran listesinden yeni sanal monitörü seçip 'Yayını Başlat' butonuna basarak tabletinizi ikinci ekran olarak kullanabilirsiniz.",
                           "Select the new virtual monitor from the screen list and click 'Start Streaming' to use your tablet as a second screen."),
                    12, itemY, cardInnerW);
            }
            else
            {
                int itemY = 12;
                AddInfoItem(pnlCard, 0,
                    L10n.T("Ekran Konumu ve Fare Geçiş Yönü", "Screen Position & Mouse Transition"),
                    L10n.T("Sanal ekranı ana monitörünüzün sağına, soluna veya üstüne serbestçe taşıyabilirsiniz. Fare imleciniz ekranlar arasında belirlediğiniz bu yönde akıcı şekilde geçer.",
                           "Move the virtual display to the right, left, or top of your primary monitor. Mouse cursor seamlessly passes between screens in this direction."),
                    12, itemY, cardInnerW);
                itemY += 94;

                AddInfoItem(pnlCard, 1,
                    L10n.T("Ölçek ve Yazı Boyutu (DPI)", "Scaling & Font Size (DPI)"),
                    L10n.T("Tablette simgeleri ve pencereleri büyütmek için %125, masaüstünüzü tam 1:1 piksel netliğinde genişletmek için %100 ölçeğini seçebilirsiniz.",
                           "Select 125% to make icons and windows larger on tablet, or 100% for crisp 1:1 pixel precision."),
                    12, itemY, cardInnerW);
                itemY += 94;

                AddInfoItem(pnlCard, 2,
                    L10n.T("Akıllı Çözünürlük Uyumu", "Smart Resolution Adaptation"),
                    L10n.T("MobiDesk cihazınız bağlandığında çözünürlüğü cihazınıza göre otomatik eşitler. Dilerseniz Windows ayarlarından çözünürlüğü de değiştirebilirsiniz.",
                           "MobiDesk automatically adapts resolution when your device connects. You can also adjust it in Windows Display Settings anytime."),
                    12, itemY, cardInnerW);
            }

            this.Controls.Add(pnlCard);

            // Alt Butonlar
            int btnH = 38;
            int btnY = this.Height - 56;

            if (mode == VirtualModalMode.RemoveSuccess)
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

                Color btnSettingsCol = mode == VirtualModalMode.InstallSuccess ? MainForm.ColorSuccess : MainForm.ColorPrimary;
                Color btnSettingsDark = mode == VirtualModalMode.InstallSuccess ? Color.FromArgb(14, 150, 105) : MainForm.ColorPrimaryDark;

                ModernButton btnOpenSettings = new ModernButton(
                    L10n.T("🚀 Windows Ayarlarını Aç", "🚀 Open Windows Settings"),
                    this.Width - 20 - 240, btnY, 240, btnH,
                    btnSettingsCol,
                    Color.White,
                    8,
                    btnSettingsDark
                );
                btnOpenSettings.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                btnOpenSettings.Click += delegate { OpenDisplaySettings(); };
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
                    using (Pen p = new Pen(Color.FromArgb(32, 42, 64), 1.0f)) g.DrawPath(p, gp);
                }

                // Vektör İkon Yuvası
                Rectangle icoR = new Rectangle(12, 14, 28, 28);
                using (GraphicsPath ip = RoundedPanel.CreateRoundedRect(icoR, 6))
                {
                    using (SolidBrush ib = new SolidBrush(Color.FromArgb(22, 30, 46))) g.FillPath(ib, ip);
                    using (Pen ipen = new Pen(Color.FromArgb(40, 54, 82), 1.0f)) g.DrawPath(ipen, ip);
                }

                if (iconType == 0) // Monitör Konum & Geçiş Vektörü
                {
                    using (Pen pMon = new Pen(MainForm.ColorAccent, 1.4f))
                    {
                        g.DrawRectangle(pMon, 16, 20, 9, 8);
                        g.DrawRectangle(pMon, 27, 20, 8, 8);
                        g.DrawLine(pMon, 22, 29, 25, 29);
                    }
                    using (Pen pArr = new Pen(Color.White, 1.2f))
                    {
                        g.DrawLine(pArr, 23, 24, 27, 24);
                    }
                }
                else if (iconType == 1) // Ölçek & Boyutlandırma Vektörü
                {
                    using (Pen pScale = new Pen(MainForm.ColorPrimary, 1.4f))
                    {
                        g.DrawRectangle(pScale, 17, 19, 18, 14);
                        g.DrawLine(pScale, 21, 26, 31, 26);
                        g.DrawLine(pScale, 26, 22, 26, 30);
                    }
                }
                else if (iconType == 2) // Çözünürlük & Hız (Şimşek Vektörü)
                {
                    using (SolidBrush bBolt = new SolidBrush(MainForm.ColorSuccess))
                    {
                        Point[] bolt = new Point[]
                        {
                            new Point(27, 17),
                            new Point(20, 27),
                            new Point(25, 27),
                            new Point(23, 37),
                            new Point(31, 25),
                            new Point(25, 25)
                        };
                        g.FillPolygon(bBolt, bolt);
                    }
                }
                else // iconType == 3: Tamam / Kaldırıldı (Onay / Rozet Vektörü)
                {
                    using (Pen pCheck = new Pen(Color.FromArgb(244, 114, 182), 2.0f))
                    {
                        pCheck.StartCap = LineCap.Round;
                        pCheck.EndCap = LineCap.Round;
                        g.DrawLine(pCheck, 18, 28, 23, 33);
                        g.DrawLine(pCheck, 23, 33, 33, 21);
                    }
                }
            };

            Label lblT = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = MainForm.ColorText,
                Location = new Point(48, 8),
                Size = new Size(w - 58, 18),
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            itemPnl.Controls.Add(lblT);

            Label lblD = new Label
            {
                Text = desc,
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = MainForm.ColorTextMuted,
                Location = new Point(48, 28),
                Size = new Size(w - 58, 48),
                BackColor = Color.Transparent,
                UseMnemonic = false
            };
            itemPnl.Controls.Add(lblD);

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
                Color borderCol = mode == VirtualModalMode.InstallSuccess ? MainForm.ColorSuccess :
                                  (mode == VirtualModalMode.RemoveSuccess ? Color.FromArgb(244, 114, 182) : MainForm.ColorPrimary);
                using (Pen p = new Pen(borderCol, 1.5f))
                {
                    g.DrawPath(p, gp);
                }
            }
        }
    }

}