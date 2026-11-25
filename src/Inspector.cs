using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZeroXEdit
{
    internal sealed class Inspector : UserControl
    {
        private readonly Font title = Theme.Ui(12, true), label = Theme.Ui(9), caption = Theme.Ui(8, true),
                              value = Theme.Mono(9), byteFont = Theme.Mono(27, true), binaryFont = Theme.Mono(11);
        private readonly ComboBox order = new ComboBox();
        private readonly ToolTip tips = new ToolTip();
        private EditorTab tab;
        private byte[] current = new byte[0];
        private readonly int[] bins = new int[32];
        private static readonly string[] Names = { "UInt8", "Int8",   "UInt16", "Int16",   "UInt32",
                                                   "Int32", "UInt64", "Int64",  "Float32", "Float64" };
        private static readonly int[] Widths = { 1, 1, 2, 2, 4, 4, 8, 8, 4, 8 };
        private string[] values = new string[Names.Length];
        internal bool BigEndian
        {
            get {
                return order.SelectedIndex == 1;
            }
        }
        internal Inspector()
        {
            DoubleBuffered = true;
            BackColor = Theme.Surface;
            AutoScroll = true;
            AccessibleName = "Data inspector";
            AutoScrollMinSize = new Size(0, Theme.D(586));
            order.Font = Theme.Ui(9);
            order.DropDownStyle = ComboBoxStyle.DropDownList;
            order.Items.AddRange(new object[] { "Little endian", "Big endian" });
            order.SelectedIndex = 0;
            order.AccessibleName = "Byte order";
            order.FlatStyle = FlatStyle.Flat;
            order.SelectedIndexChanged += delegate
            {
                RefreshValues();
            };
            Controls.Add(order);
            PositionOrder();
            MouseMove += delegate(object sender, MouseEventArgs e)
            {
                int row = (e.Y - AutoScrollPosition.Y - Theme.D(197)) / Theme.D(24);
                string tip =
                    e.Y - AutoScrollPosition.Y >= Theme.D(197) && row >= 0 && row < Names.Length ? values[row] : "";
                if (tips.GetToolTip(this) != tip)
                    tips.SetToolTip(this, tip);
            };
        }
        private void PositionOrder()
        {
            order.SetBounds(Math.Max(Theme.D(86), ClientSize.Width - Theme.D(138)), Theme.D(157) + AutoScrollPosition.Y,
                            Theme.D(118), Theme.D(27));
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (order != null)
                PositionOrder();
            Invalidate();
        }
        internal void RefreshDocument(EditorTab newTab)
        {
            tab = newTab;
            Array.Clear(bins, 0, bins.Length);
            if (tab != null)
                foreach (byte b in tab.Document.ReadBytes(0, 65536))
                    bins[b / 8]++;
            RefreshValues();
        }
        internal void RefreshValues()
        {
            current = tab == null ? new byte[0] : tab.Document.ReadBytes(tab.Start, 8);
            for (int i = 0; i < Names.Length; i++)
                values[i] = ByteTools.Interpret(current, Widths[i], BigEndian, i >= 8 ? 'f' : i % 2 == 0 ? 'u' : 's');
            Invalidate();
        }
        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = ClientSize.Width, left = Theme.D(18), right = w - Theme.D(18), sy = AutoScrollPosition.Y;
            Theme.Rule(g, Theme.Line, 0, 0, 0, Height);
            Theme.Text(g, "Data inspector", title, Theme.Ink, new Rectangle(left, sy, w - left, Theme.D(53)));
            Theme.Glyph(g, "panel", Theme.Muted,
                        new Rectangle(right - Theme.D(18), sy + Theme.D(18), Theme.D(18), Theme.D(18)));
            int cardY = sy + Theme.D(54);
            Theme.Round(g, Theme.Navy, new Rectangle(left, cardY, right - left, Theme.D(86)), Theme.D(7));
            Theme.Text(g, "CURRENT BYTE", caption, Color.FromArgb(192, 207, 229),
                       new Rectangle(left + Theme.D(14), cardY + Theme.D(9), right - left - Theme.D(20), Theme.D(20)));
            Theme.Text(g, current.Length == 0 ? "—" : current[0].ToString("X2"), byteFont, Color.White,
                       new Rectangle(left + Theme.D(12), cardY + Theme.D(28), Theme.D(85), Theme.D(51)));
            Theme.Text(g, current.Length == 0 ? "—" : ByteTools.Bits(current[0]), binaryFont,
                       Color.FromArgb(168, 211, 252),
                       new Rectangle(left + Theme.D(85), cardY + Theme.D(31), right - left - Theme.D(98), Theme.D(48)),
                       TextFormatFlags.Right);
            Theme.Text(g, "Byte order", label, Theme.Muted,
                       new Rectangle(left, sy + Theme.D(150), Theme.D(79), Theme.D(36)));
            Theme.Rule(g, Theme.Line, left, sy + Theme.D(193), right, sy + Theme.D(193));
            for (int i = 0; i < Names.Length; i++)
            {
                int y = sy + Theme.D(197) + i * Theme.D(24);
                Theme.Text(g, Names[i], label, Theme.Muted, new Rectangle(left, y, Theme.D(55), Theme.D(24)));
                Theme.Text(g, values[i] ?? "—", value, Theme.Ink,
                           new Rectangle(left + Theme.D(57), y, right - left - Theme.D(57), Theme.D(24)),
                           TextFormatFlags.Right);
            }
            int div = sy + Theme.D(451);
            Theme.Rule(g, Theme.Line, left, div, right, div);
            Theme.Text(g, "Byte distribution", title, Theme.Ink,
                       new Rectangle(left, div + Theme.D(8), right - left, Theme.D(32)));
            int peak = 1;
            foreach (int n in bins)
                peak = Math.Max(peak, n);
            int chartW = right - left, floor = div + Theme.D(86);
            for (int i = 0; i < bins.Length; i++)
            {
                int x = left + i * chartW / bins.Length;
                int h = bins[i] == 0 ? 0 : Math.Max(Theme.D(2), (int)(Theme.D(37) * (double)bins[i] / peak));
                Theme.Fill(g, i == 0 || i == bins.Length - 1 ? Theme.Blue : Color.FromArgb(124, 152, 223),
                           new Rectangle(x, floor - h,
                                         Math.Max(1, left + (i + 1) * chartW / bins.Length - x - Theme.D(2)), h));
            }
            Theme.Rule(g, Theme.Line, left, floor + 1, right, floor + 1);
            Theme.Text(g, "00", value, Theme.Muted, new Rectangle(left, floor + Theme.D(3), Theme.D(36), Theme.D(18)));
            Theme.Text(g, "FF", value, Theme.Muted,
                       new Rectangle(right - Theme.D(36), floor + Theme.D(3), Theme.D(36), Theme.D(18)),
                       TextFormatFlags.Right);
            long nBytes = tab == null ? 0 : Math.Min(65536, tab.Document.Length);
            Theme.Text(g,
                       (tab != null && tab.Document.Length > nBytes ? "First " : "") + nBytes.ToString("N0") + " bytes",
                       label, Theme.Muted, new Rectangle(left, floor + Theme.D(23), chartW, Theme.D(21)));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                title.Dispose();
                label.Dispose();
                caption.Dispose();
                value.Dispose();
                byteFont.Dispose();
                binaryFont.Dispose();
                tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
