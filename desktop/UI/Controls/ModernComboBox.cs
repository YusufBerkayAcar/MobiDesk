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
    public class ModernComboBox : Control
    {
        private readonly List<object> items = new List<object>();
        private int selectedIndex = -1;
        private bool isHovered = false;
        private bool isDroppedDown = false;
        private ContextMenuStrip dropDownMenu;
        private int updateCount = 0;

        public event EventHandler SelectedIndexChanged;

        public void BeginUpdate()
        {
            updateCount++;
        }

        public void EndUpdate()
        {
            if (updateCount > 0)
            {
                updateCount--;
                if (updateCount == 0)
                {
                    this.Invalidate();
                }
            }
        }

        public class ItemCollection : System.Collections.IEnumerable
        {
            private readonly ModernComboBox owner;
            public ItemCollection(ModernComboBox owner) { this.owner = owner; }

            public int Count { get { return owner.items.Count; } }

            public object this[int index]
            {
                get { return owner.items[index]; }
                set
                {
                    owner.items[index] = value;
                    if (owner.updateCount == 0) owner.Invalidate();
                }
            }

            public int Add(object item)
            {
                int idx = owner.items.Count;
                owner.items.Add(item);
                if (owner.updateCount == 0)
                {
                    if (owner.selectedIndex < 0) owner.SelectedIndex = 0;
                    owner.Invalidate();
                }
                return idx;
            }

            public void Clear()
            {
                owner.items.Clear();
                owner.selectedIndex = -1;
                if (owner.updateCount == 0)
                {
                    owner.Invalidate();
                }
            }

            public bool Contains(object item) { return owner.items.Contains(item); }
            public int IndexOf(object item) { return owner.items.IndexOf(item); }
            public void Insert(int index, object item)
            {
                owner.items.Insert(index, item);
                if (owner.updateCount == 0) owner.Invalidate();
            }
            public void Remove(object item)
            {
                owner.items.Remove(item);
                if (owner.updateCount == 0) owner.Invalidate();
            }
            public void RemoveAt(int index)
            {
                owner.items.RemoveAt(index);
                if (owner.updateCount == 0) owner.Invalidate();
            }
            public System.Collections.IEnumerator GetEnumerator() { return owner.items.GetEnumerator(); }
        }

        private ItemCollection itemsCollection;
        public ItemCollection Items { get { return itemsCollection; } }

        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                if (selectedIndex != value)
                {
                    if (value >= 0 && value < items.Count)
                        selectedIndex = value;
                    else
                        selectedIndex = -1;

                    if (updateCount == 0)
                    {
                        this.Invalidate();
                    }
                    if (SelectedIndexChanged != null)
                    {
                        SelectedIndexChanged(this, EventArgs.Empty);
                    }
                }
            }
        }

        public object SelectedItem
        {
            get
            {
                if (selectedIndex >= 0 && selectedIndex < items.Count)
                    return items[selectedIndex];
                return null;
            }
            set
            {
                int idx = items.IndexOf(value);
                SelectedIndex = idx;
            }
        }

        public string SelectedText
        {
            get
            {
                object item = SelectedItem;
                return item != null ? item.ToString() : string.Empty;
            }
        }

        public ModernComboBox()
        {
            this.itemsCollection = new ItemCollection(this);
            this.Size = new Size(210, 28);
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            this.BackColor = Color.FromArgb(20, 26, 40);
            this.ForeColor = MainForm.ColorText;
            this.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            this.Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHovered = false;
            this.Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            this.Focus();
            ShowDropDown();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down)
            {
                if (SelectedIndex < items.Count - 1) SelectedIndex++;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                if (SelectedIndex > 0) SelectedIndex--;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                ShowDropDown();
                e.Handled = true;
            }
        }

        public void ShowDropDown()
        {
            if (items.Count == 0) return;

            if (dropDownMenu != null)
            {
                dropDownMenu.Dispose();
                dropDownMenu = null;
            }

            dropDownMenu = new ContextMenuStrip();
            dropDownMenu.Renderer = new DarkMenuRenderer();
            dropDownMenu.BackColor = Color.FromArgb(17, 24, 39);
            dropDownMenu.ShowImageMargin = false;
            dropDownMenu.Font = this.Font;
            dropDownMenu.MinimumSize = new Size(this.Width, 0);

            for (int i = 0; i < items.Count; i++)
            {
                int itemIndex = i;
                object itemObj = items[i];
                string itemText = itemObj != null ? itemObj.ToString() : "";

                ToolStripMenuItem menuItem = new ToolStripMenuItem(itemText);
                menuItem.AutoSize = false;
                menuItem.Height = 28;
                menuItem.Width = Math.Max(this.Width, 240);
                menuItem.ForeColor = (itemIndex == selectedIndex) ? MainForm.ColorAccent : MainForm.ColorText;
                menuItem.Font = (itemIndex == selectedIndex) ? new Font(this.Font, FontStyle.Bold) : this.Font;

                menuItem.Click += delegate
                {
                    this.SelectedIndex = itemIndex;
                };

                dropDownMenu.Items.Add(menuItem);
            }

            isDroppedDown = true;
            this.Invalidate();

            dropDownMenu.Closed += delegate
            {
                isDroppedDown = false;
                this.Invalidate();
            };

            dropDownMenu.Show(this, new Point(0, this.Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color pBg = (this.Parent != null) ? this.Parent.BackColor : MainForm.ColorCard;
            using (SolidBrush b = new SolidBrush(pBg))
            {
                g.FillRectangle(b, this.ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

            Color bg = (isHovered || isDroppedDown || this.Focused) ? Color.FromArgb(26, 35, 52) : Color.FromArgb(20, 26, 40);
            using (GraphicsPath path = RoundedPanel.CreateRoundedRect(rect, 6))
            {
                using (SolidBrush b = new SolidBrush(bg))
                {
                    g.FillPath(b, path);
                }

                Color borderCol = (isHovered || isDroppedDown || this.Focused) ? MainForm.ColorPrimary : MainForm.ColorCardBorder;
                using (Pen p = new Pen(borderCol, 1.2f))
                {
                    g.DrawPath(p, path);
                }
            }

            int btnW = 26;
            Rectangle btnRect = new Rectangle(this.Width - btnW, 0, btnW, this.Height);
            int cx = btnRect.X + (btnRect.Width / 2);
            int cy = btnRect.Y + (btnRect.Height / 2);

            Color chevronColor = (isHovered || isDroppedDown) ? MainForm.ColorAccent : MainForm.ColorTextMuted;
            using (Pen cp = new Pen(chevronColor, 1.8f))
            {
                cp.StartCap = LineCap.Round;
                cp.EndCap = LineCap.Round;
                cp.LineJoin = LineJoin.Round;

                if (isDroppedDown)
                {
                    Point[] pts = new Point[]
                    {
                        new Point(cx - 4, cy + 2),
                        new Point(cx, cy - 2),
                        new Point(cx + 4, cy + 2)
                    };
                    g.DrawLines(cp, pts);
                }
                else
                {
                    Point[] pts = new Point[]
                    {
                        new Point(cx - 4, cy - 2),
                        new Point(cx, cy + 2),
                        new Point(cx + 4, cy - 2)
                    };
                    g.DrawLines(cp, pts);
                }
            }

            string text = SelectedText;
            if (!string.IsNullOrEmpty(text))
            {
                Rectangle textRect = new Rectangle(10, 0, this.Width - btnW - 10, this.Height);
                TextRenderer.DrawText(g, text, this.Font, textRect, MainForm.ColorText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }


}