using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ZeroXEdit
{
    // One viewport keeps the file offsets aligned, including past the end of the shorter file.
    internal sealed class ComparisonGrid : Control
    {
        internal static readonly Color Pink = Color.FromArgb(255, 209, 228);
        private static readonly Color ChangedInk = Color.FromArgb(120, 25, 66);
        private readonly Font mono = Theme.Mono(10), small = Theme.Mono(9), header = Theme.Ui(8, true);
        private readonly VScrollBar scroll = new VScrollBar { Dock = DockStyle.Right, TabStop = false };
        private FileComparison comparison;
        private int columns = 8;
        private long topRow;
        private bool syncing;
        internal long HighlightOffset { get; private set; }
        internal event Action<IOException> ReadError;

        internal ComparisonGrid()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = true;
            BackColor = Theme.Paper;
            AccessibleName = "Side-by-side byte comparison";
            Controls.Add(scroll);
            scroll.ValueChanged += delegate {
                if (syncing) return;
                long max = MaxTopRow;
                topRow = max <= ScrollLimit ? scroll.Value : (long)Math.Round((decimal)scroll.Value * max / ScrollLimit);
                Invalidate();
            };
        }
        private const int ScrollLimit = 1000000000;
        private int Gutter { get { return Theme.D(comparison != null && comparison.Length > uint.MaxValue ? 153 : 91); } }
        private int RowHeight { get { return Math.Max(Theme.D(24), mono.Height + Theme.D(3)); } }
        private int HeaderHeight { get { return Theme.D(33); } }
        private int Half { get { return (ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Gutter) / 2; } }
        private int PageRows { get { return Math.Max(1, (ClientSize.Height - HeaderHeight) / RowHeight); } }
        private long MaxTopRow
        {
            get { return comparison == null || comparison.Length == 0 ? 0 :
                Math.Max(0, (comparison.Length - 1) / columns + 1 - PageRows); }
        }
        internal void Bind(FileComparison value)
        {
            comparison = value;
            topRow = 0;
            HighlightOffset = -1;
            UpdateScroll();
        }
        internal void Reveal(long offset)
        {
            HighlightOffset = offset;
            long row = offset / columns;
            if (row < topRow || row >= topRow + PageRows)
                topRow = Math.Max(0, row - PageRows / 3);
            UpdateScroll();
        }
        private void UpdateScroll()
        {
            long first = topRow * columns;
            columns = Half >= Theme.D(537) ? 16 : 8;
            topRow = Math.Min(MaxTopRow, first / columns);
            syncing = true;
            try
            {
                long max = MaxTopRow;
                scroll.Visible = max > 0;
                scroll.Maximum = (int)Math.Min(ScrollLimit, max) + PageRows - 1;
                scroll.LargeChange = PageRows;
                scroll.Value = max <= ScrollLimit ? (int)topRow : (int)Math.Round((decimal)topRow * ScrollLimit / max);
            }
            finally { syncing = false; }
            Invalidate();
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (scroll != null && mono != null) UpdateScroll();
        }
        private void ScrollRows(long row)
        {
            topRow = Math.Max(0, Math.Min(MaxTopRow, row));
            UpdateScroll();
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int lines = SystemInformation.MouseWheelScrollLines;
            ScrollRows(topRow - (long)Math.Sign(e.Delta) * Math.Max(1, Math.Abs(e.Delta) / 120) * (lines < 0 ? PageRows : lines));
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            switch (key)
            {
                case Keys.Up: ScrollRows(topRow - 1); break;
                case Keys.Down: ScrollRows(topRow + 1); break;
                case Keys.PageUp: ScrollRows(topRow - PageRows); break;
                case Keys.PageDown: ScrollRows(topRow + PageRows); break;
                case Keys.Home: case Keys.Control | Keys.Home: ScrollRows(0); break;
                case Keys.End: case Keys.Control | Keys.End: ScrollRows(MaxTopRow); break;
                default: return base.ProcessCmdKey(ref msg, key);
            }
            return true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.Clear(BackColor);
            if (comparison == null)
            {
                Theme.Text(g, "Choose two files to compare.", Font, Theme.Muted, ClientRectangle, TextFormatFlags.HorizontalCenter);
                return;
            }
            int rows = Math.Max(0, (Height - HeaderHeight + RowHeight - 1) / RowHeight);
            long first = topRow * columns;
            byte[] a, b;
            try
            {
                a = comparison.Left.ReadBytes(Math.Min(first, comparison.Left.Length), rows * columns);
                b = comparison.Right.ReadBytes(Math.Min(first, comparison.Right.Length), rows * columns);
            }
            catch (IOException ex) { if (ReadError != null) ReadError(ex); return; }

            Theme.Fill(g, Theme.Surface, new Rectangle(0, 0, Gutter, Height));
            Theme.Fill(g, Theme.Surface, new Rectangle(0, 0, Width, HeaderHeight));
            Theme.Text(g, "OFFSET (h)", header, Theme.Muted, new Rectangle(Theme.D(11), 0, Gutter - Theme.D(13), HeaderHeight));
            int count = Math.Max(a.Length, b.Length);
            for (int i = 0; i < count; i += columns)
            {
                string address = (first + i).ToString(comparison.Length > uint.MaxValue ? "X16" : "X8");
                Theme.Text(g, address, small, Theme.Muted,
                           new Rectangle(Theme.D(11), HeaderHeight + i / columns * RowHeight, Gutter - Theme.D(13), RowHeight));
            }
            DrawSide(g, Gutter, a, b, first);
            DrawSide(g, Gutter + Half, b, a, first);
            Theme.Rule(g, Theme.Line, 0, HeaderHeight - 1, Width, HeaderHeight - 1);
            Theme.Rule(g, Theme.Line, Gutter, 0, Gutter, Height);
            Theme.Rule(g, Theme.Line, Gutter + Half, 0, Gutter + Half, Height);
        }
        private void DrawSide(Graphics g, int origin, byte[] data, byte[] other, long first)
        {
            int asciiPitch = Theme.D(9), asciiX = origin + Half - Theme.D(7) - columns * asciiPitch;
            int hexX = origin + Theme.D(9), pitch = Math.Min(Theme.D(32), (asciiX - Theme.D(8) - hexX) / columns);
            for (int c = 0; c < columns; c++)
                Theme.Text(g, c.ToString("X2"), small, Theme.Muted, new Rectangle(hexX + c * pitch, 0, pitch, HeaderHeight));
            Theme.Text(g, "ASCII", header, Theme.Teal, new Rectangle(asciiX, 0, columns * asciiPitch, HeaderHeight));
            int count = Math.Max(data.Length, other.Length);
            for (int i = 0; i < count; i++)
            {
                int col = i % columns, y = HeaderHeight + i / columns * RowHeight;
                var hex = new Rectangle(hexX + col * pitch, y, pitch, RowHeight);
                var ascii = new Rectangle(asciiX + col * asciiPitch, y, asciiPitch, RowHeight);
                bool missing = i >= data.Length;
                bool changed = missing || i >= other.Length || data[i] != other[i];
                if (changed)
                {
                    Theme.Fill(g, Pink, new Rectangle(hex.X - 2, y + 1, pitch, RowHeight - 2));
                    Theme.Fill(g, Pink, new Rectangle(ascii.X, y + 1, asciiPitch, RowHeight - 2));
                    if (first + i == HighlightOffset)
                    {
                        Theme.Fill(g, ChangedInk, new Rectangle(hex.X - 2, hex.Bottom - Theme.D(3), pitch, Theme.D(2)));
                        Theme.Fill(g, ChangedInk, new Rectangle(ascii.X, ascii.Bottom - Theme.D(3), asciiPitch, Theme.D(2)));
                    }
                }
                byte value = missing ? (byte)0 : data[i];
                bool printable = value >= 32 && value <= 126;
                Theme.Text(g, missing ? "--" : value.ToString("X2"), mono, changed ? ChangedInk : Theme.Ink, hex);
                Theme.Text(g, missing ? "–" : printable ? ((char)value).ToString() : "·", mono,
                           changed ? ChangedInk : printable ? Theme.Teal : Theme.Faint, ascii);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { mono.Dispose(); small.Dispose(); header.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
