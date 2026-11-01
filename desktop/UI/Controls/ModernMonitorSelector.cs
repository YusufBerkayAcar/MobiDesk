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
    public class ModernMonitorSelector : Panel
    {
        private List<MonitorItemData> monitors = new List<MonitorItemData>();
        private int selectedIdx = 0;

        internal DoubleBufferedPanel pnlContent;
        internal ModernDarkScrollBar vScrollBar;

        public delegate void SelectionChangedHandler(int index, Screen scr, bool isVirtual);
        public event SelectionChangedHandler SelectionChanged;

        public int SelectedIndex
        {
            get { return selectedIdx; }
            set
            {
                if (value >= 0 && value < monitors.Count)
                {
                    selectedIdx = value;
                    UpdateCardSelections();
                    if (SelectionChanged != null)
                    {
                        SelectionChanged(selectedIdx, monitors[selectedIdx].Screen, monitors[selectedIdx].IsVirtual);
                    }
                }
            }
        }

        public ModernMonitorSelector(int x, int y, int w, int h)
        {
            this.Location = new Point(x, y);
            this.Size = new Size(w, h);
            this.AutoScroll = false;
            this.BackColor = MainForm.ColorCard;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            pnlContent = new DoubleBufferedPanel
            {
                Location = new Point(0, 0),
                Size = new Size(w, h),
                BackColor = MainForm.ColorCard
            };
            this.Controls.Add(pnlContent);

            vScrollBar = new ModernDarkScrollBar
            {
                Location = new Point(w - 9, 0),
                Size = new Size(8, h),
                Visible = false,
                BackColor = MainForm.ColorCard
            };
            vScrollBar.ValueChanged += delegate
            {
                if (pnlContent != null) pnlContent.Top = -vScrollBar.Value;
            };
            this.Controls.Add(vScrollBar);
            vScrollBar.BringToFront();

            AttachWheel(this);
            AttachWheel(pnlContent);
        }

        private void AttachWheel(Control c)
        {
            if (c == null) return;
            c.MouseWheel += delegate(object s, MouseEventArgs e)
            {
                if (vScrollBar != null && vScrollBar.Visible)
                {
                    int step = (e.Delta < 0) ? 36 : -36;
                    vScrollBar.Value += step;
                }
            };
            foreach (Control child in c.Controls)
            {
                AttachWheel(child);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                pevent.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(MainForm.ColorCard))
            {
                e.Graphics.FillRectangle(b, this.ClientRectangle);
            }
        }

        public void SetMonitors(List<MonitorItemData> items, int defaultIndex)
        {
            this.monitors = items ?? new List<MonitorItemData>();
            this.selectedIdx = (defaultIndex >= 0 && defaultIndex < monitors.Count) ? defaultIndex : 0;
            if (vScrollBar != null) vScrollBar.Value = 0;
            if (pnlContent != null) pnlContent.Top = 0;
            RefreshCards();

            if (monitors.Count > selectedIdx && SelectionChanged != null)
            {
                SelectionChanged(selectedIdx, monitors[selectedIdx].Screen, monitors[selectedIdx].IsVirtual);
            }
        }

        public void UpdateLanguageTags()
        {
            if (monitors == null) return;
            for (int i = 0; i < monitors.Count; i++)
            {
                MonitorItemData m = monitors[i];
                if (m.IsVirtual)
                {
                    m.Tag = L10n.T("★ SANAL MOBIDESK", "★ VIRTUAL MOBIDESK");
                }
                else if (m.Screen != null && m.Screen.Primary)
                {
                    m.Tag = L10n.T("ANA MONİTÖR", "PRIMARY MONITOR");
                }
                else
                {
                    m.Tag = string.Format(L10n.T("MONİTÖR {0}", "MONITOR {0}"), i + 1);
                }
            }
            RefreshCards();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RefreshCards();
            this.Invalidate(true);
        }

        private void UpdateCardSelections()
        {
            if (pnlContent == null) return;
            for (int i = 0; i < pnlContent.Controls.Count; i++)
            {
                MonitorCardItem card = pnlContent.Controls[i] as MonitorCardItem;
                if (card != null)
                {
                    card.IsSelected = (i == selectedIdx);
                }
            }
        }

        private void RefreshCards()
        {
            if (pnlContent == null) return;
            pnlContent.SuspendLayout();
            pnlContent.Controls.Clear();

            int n = monitors.Count;
            if (n == 0)
            {
                if (vScrollBar != null) vScrollBar.Visible = false;
                pnlContent.Top = 0;
                pnlContent.ResumeLayout(true);
                return;
            }

            int availW = this.ClientSize.Width;
            int availH = this.ClientSize.Height;
            if (availW <= 0 || availH <= 0)
            {
                pnlContent.ResumeLayout(true);
                return;
            }

            int gap = (availH < 120 || n >= 3) ? 4 : 6;
            
            // Eğer tüm kartlar dikey alana sığıyorsa kart yüksekliğini dinamik optimize et
            int cardH;
            if (n <= 2)
            {
                cardH = (availH < 100) ? 38 : ((availH < 130) ? 42 : 46);
            }
            else
            {
                // 3 veya daha fazla monitör için (Sanal ekran kesinlikle sığmalı)
                int idealH = (availH - (n - 1) * gap) / n;
                if (idealH >= 34)
                {
                    cardH = Math.Min(44, idealH);
                }
                else
                {
                    cardH = 38;
                }
            }

            int totalH = n * cardH + (n - 1) * gap;
            bool needScroll = totalH > availH;

            int scrollBarW = 8;
            if (vScrollBar != null)
            {
                vScrollBar.Visible = needScroll;
                vScrollBar.Location = new Point(availW - scrollBarW - 1, 0);
                vScrollBar.Size = new Size(scrollBarW, availH);
                vScrollBar.BringToFront();
            }

            int itemW = needScroll ? (availW - scrollBarW - 4) : availW;
            pnlContent.Width = availW;
            pnlContent.Height = Math.Max(availH, totalH);

            int curY = 0;
            for (int i = 0; i < n; i++)
            {
                MonitorItemData data = monitors[i];
                bool isSelected = (i == selectedIdx);

                MonitorCardItem card = new MonitorCardItem(data, isSelected, 0, curY, itemW, cardH);
                int cardIdx = i;
                card.Click += delegate(object s, EventArgs e)
                {
                    this.SelectedIndex = cardIdx;
                };
                AttachWheel(card);
                pnlContent.Controls.Add(card);
                curY += cardH + gap;
            }

            if (needScroll && vScrollBar != null)
            {
                vScrollBar.Minimum = 0;
                vScrollBar.Maximum = totalH;
                vScrollBar.LargeChange = availH;
                int maxVal = Math.Max(0, totalH - availH);
                if (vScrollBar.Value > maxVal) vScrollBar.Value = maxVal;
                pnlContent.Top = -vScrollBar.Value;
            }
            else
            {
                if (vScrollBar != null) vScrollBar.Value = 0;
                pnlContent.Top = 0;
            }

            pnlContent.ResumeLayout(true);
            this.Invalidate(true);
        }
    }


}