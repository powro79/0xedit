using System;
using System.Drawing;
using System.Windows.Forms;

namespace ZeroXEdit
{
    internal sealed class EditorTab
    {
        internal readonly BinaryDocument Document;
        internal long Caret, Anchor, FirstRow;
        internal bool Ascii, Insert;
        internal int Columns = 16;
        internal EditorTab(BinaryDocument document)
        {
            Document = document;
        }
        internal long Start
        {
            get {
                return Math.Min(Caret, Anchor);
            }
        }
        internal long Selected
        {
            get {
                return Math.Max(Caret, Anchor) - Start;
            }
        }
        internal string OffsetText
        {
            get {
                return Start.ToString(Document.Length > uint.MaxValue ? "X16" : "X8");
            }
        }
    }

    internal sealed class HexView : Control
    {
        private readonly VScrollBar vertical = new VScrollBar();
        private readonly HScrollBar horizontal = new HScrollBar();
        private readonly Font mono = Theme.Mono(10.5f), small = Theme.Mono(9), labels = Theme.Ui(8, true);
        private static readonly string[] Hex = MakeHex();
        private EditorTab tab;
        private bool syncing, lowNibble, amend, dragging;
        private long mouseAnchor;
        internal event Action ViewChanged, BytesChanged;
        internal bool LowNibble
        {
            get {
                return lowNibble;
            }
        }
        internal HexView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable,
                     true);
            BackColor = Theme.Paper;
            TabStop = true;
            AccessibleName = "Hex and ASCII editor";
            AccessibleRole = AccessibleRole.Text;
            Controls.Add(vertical);
            Controls.Add(horizontal);
            vertical.TabStop = false;
            horizontal.TabStop = false;
            vertical.ValueChanged += delegate
            {
                if (syncing || tab == null)
                    return;
                long max = MaxFirst;
                int limit = vertical.Maximum - vertical.LargeChange + 1;
                tab.FirstRow = max <= 1000000000 ? vertical.Value
                                                 : (long)Math.Round((decimal)vertical.Value * max / Math.Max(1, limit));
                Invalidate();
            };
            horizontal.ValueChanged += delegate
            {
                Invalidate();
            };
        }
        private static string[] MakeHex()
        {
            var result = new string[256];
            for (int i = 0; i < 256; i++)
                result[i] = i.ToString("X2");
            return result;
        }
        internal void Bind(EditorTab value)
        {
            tab = value;
            CancelNibble();
            horizontal.Value = 0;
            UpdateScroll();
            Invalidate();
        }
        private int Header
        {
            get {
                return Theme.D(36);
            }
        }
        private int Row
        {
            get {
                return Math.Max(Theme.D(25), mono.Height + Theme.D(3));
            }
        }
        private int Gutter
        {
            get {
                return Theme.D(tab != null && tab.Document.Length > uint.MaxValue ? 164 : 100);
            }
        }
        private int Pitch
        {
            get {
                return Theme.D(29);
            }
        }
        private int HexX(int col)
        {
            return Gutter + Theme.D(15) + col * Pitch + col / 8 * Theme.D(10);
        }
        private int AsciiLeft
        {
            get {
                return HexX(tab.Columns - 1) + Pitch + Theme.D(13);
            }
        }
        private int AsciiPitch
        {
            get {
                return Theme.D(10);
            }
        }
        private int AsciiX(int col)
        {
            return AsciiLeft + Theme.D(13) + AsciiPitch * col;
        }
        private int RowsVisible
        {
            get {
                return Math.Max(1, (Height - Header - (horizontal.Visible ? horizontal.Height : 0)) / Row);
            }
        }
        private long MaxFirst
        {
            get {
                return Math.Max(0, tab.Document.Length / tab.Columns + 1 - RowsVisible);
            }
        }
        private void UpdateScroll()
        {
            if (tab == null)
            {
                vertical.Visible = horizontal.Visible = false;
                return;
            }
            syncing = true;
            try
            {
                int sw = SystemInformation.VerticalScrollBarWidth, sh = SystemInformation.HorizontalScrollBarHeight;
                int full = AsciiX(tab.Columns) + Theme.D(14);
                bool needsV = tab.Document.Length / tab.Columns + 1 > Math.Max(1, (Height - Header) / Row);
                bool needsH = full > Width - (needsV ? sw : 0);
                needsV =
                    tab.Document.Length / tab.Columns + 1 > Math.Max(1, (Height - Header - (needsH ? sh : 0)) / Row);
                needsH = full > Width - (needsV ? sw : 0);
                int w = Math.Max(1, Width - (needsV ? sw : 0)), h = Math.Max(1, Height - (needsH ? sh : 0));
                horizontal.SetBounds(0, h, w, sh);
                vertical.SetBounds(w, Header, sw, Math.Max(1, h - Header));
                horizontal.Visible = needsH;
                vertical.Visible = needsV;
                horizontal.Maximum = Math.Max(0, full - 1);
                horizontal.LargeChange = Math.Min(full, w);
                horizontal.SmallChange = Pitch;
                if (!needsH)
                    horizontal.Value = 0;
                else
                    horizontal.Value = Math.Min(horizontal.Value, Math.Max(0, full - w));
                long max = MaxFirst;
                tab.FirstRow = Math.Max(0, Math.Min(max, tab.FirstRow));
                int limit = (int)Math.Min(1000000000, max);
                vertical.Maximum = limit + RowsVisible - 1;
                vertical.LargeChange = RowsVisible;
                vertical.SmallChange = 1;
                vertical.Value =
                    max <= 1000000000 ? (int)tab.FirstRow : (int)Math.Round((decimal)tab.FirstRow * limit / max);
            }
            finally
            {
                syncing = false;
            }
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScroll();
            Invalidate();
        }
        internal void SetColumns(int count)
        {
            tab.Columns = count;
            UpdateScroll();
            Reveal(tab.Start);
            Changed();
        }
        internal void CancelNibble()
        {
            lowNibble = false;
            amend = false;
        }
        internal void SelectRange(long start, long end)
        {
            CancelNibble();
            tab.Anchor = start;
            tab.Caret = end;
            Reveal(start);
            Changed();
        }
        internal void Navigate(long pos, bool extend)
        {
            CancelNibble();
            tab.Caret = Math.Max(0, Math.Min(tab.Document.Length, pos));
            if (!extend)
                tab.Anchor = tab.Caret;
            Reveal(tab.Caret);
            Changed();
        }
        private void Reveal(long pos)
        {
            long row = pos / tab.Columns;
            if (row < tab.FirstRow)
                tab.FirstRow = row;
            if (row >= tab.FirstRow + RowsVisible)
                tab.FirstRow = row - RowsVisible + 1;
            UpdateScroll();
            if (horizontal.Visible)
            {
                int col = (int)(pos % tab.Columns), x = tab.Ascii ? AsciiX(col) : HexX(col);
                int view = horizontal.Width, value = horizontal.Value;
                if (x < value)
                    value = x - Theme.D(12);
                else if (x + Pitch > value + view)
                    value = x + Pitch - view;
                horizontal.Value = Math.Max(0, Math.Min(horizontal.Maximum - horizontal.LargeChange + 1, value));
            }
        }
        private void Changed()
        {
            Invalidate();
            if (ViewChanged != null)
                ViewChanged();
        }
        private void Edited(bool changed)
        {
            tab.Caret = Math.Min(tab.Caret, tab.Document.Length);
            tab.Anchor = Math.Min(tab.Anchor, tab.Document.Length);
            if (changed && BytesChanged != null)
                BytesChanged();
            UpdateScroll();
        }
        internal void Put(byte[] bytes)
        {
            if (bytes.Length == 0)
                return;
            CancelNibble();
            long pos = tab.Start;
            long remove = tab.Selected > 0 ? tab.Selected
                          : tab.Insert     ? 0
                                           : Math.Min(bytes.Length, tab.Document.Length - pos);
            Edited(tab.Document.Replace(pos, remove, bytes));
            Navigate(pos + bytes.Length, false);
        }
        internal void Delete(bool back)
        {
            long pos = tab.Start, count = tab.Selected;
            if (count == 0)
            {
                if (back && pos > 0)
                    pos--;
                else if (back || pos == tab.Document.Length)
                    return;
                count = 1;
            }
            CancelNibble();
            Edited(tab.Document.Replace(pos, count, new byte[0]));
            Navigate(pos, false);
        }
        internal void History(bool forward)
        {
            if (forward ? !tab.Document.CanRedo : !tab.Document.CanUndo)
                return;
            long pos = forward ? tab.Document.Redo() : tab.Document.Undo();
            Edited(true);
            Navigate(pos, false);
        }
        internal void ToggleInsert()
        {
            CancelNibble();
            tab.Insert = !tab.Insert;
            Changed();
        }
        internal void TypeByte(char ch)
        {
            if (tab.Ascii)
            {
                if (ch >= 32 && ch <= 126)
                    Put(new[] { (byte)ch });
                return;
            }
            if (!Uri.IsHexDigit(ch))
                return;
            int digit = Convert.ToInt32(ch.ToString(), 16);
            long pos = tab.Start;
            if (lowNibble)
            {
                byte high = (byte)(tab.Document.ReadBytes(pos, 1)[0] & 0xF0);
                Edited(tab.Document.Replace(pos, 1, new[] { (byte)(high | digit) }, amend));
                Navigate(pos + 1, false);
                return;
            }
            long remove = tab.Selected > 0 ? tab.Selected : tab.Insert || pos == tab.Document.Length ? 0 : 1;
            int low = remove > 0 ? tab.Document.ReadBytes(pos, 1)[0] & 15 : 0;
            amend = tab.Document.Replace(pos, remove, new[] { (byte)(digit * 16 + low) });
            Edited(amend);
            lowNibble = true;
            tab.Caret = tab.Anchor = pos;
            Reveal(pos);
            Changed();
        }
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (tab != null && (ModifierKeys & (Keys.Control | Keys.Alt)) == 0)
            {
                TypeByte(e.KeyChar);
                e.Handled = true;
            }
            base.OnKeyPress(e);
        }
        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
            case Keys.Left:
            case Keys.Right:
            case Keys.Up:
            case Keys.Down:
            case Keys.Tab:
            case Keys.Home:
            case Keys.End:
            case Keys.PageUp:
            case Keys.PageDown:
                return true;
            }
            return base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (tab == null || e.Alt)
                return;
            long pos = tab.Caret;
            int cols = tab.Columns;
            switch (e.KeyCode)
            {
            case Keys.Left:
                pos = !e.Shift && tab.Selected > 0 ? tab.Start : pos - 1;
                break;
            case Keys.Right:
                pos = !e.Shift && tab.Selected > 0 ? tab.Start + tab.Selected : pos + 1;
                break;
            case Keys.Up:
                pos -= cols;
                break;
            case Keys.Down:
                pos += cols;
                break;
            case Keys.Home:
                pos = e.Control ? 0 : pos - pos % cols;
                break;
            case Keys.End:
                pos = e.Control ? tab.Document.Length : Math.Min(tab.Document.Length, pos - pos % cols + cols - 1);
                break;
            case Keys.PageUp:
                pos -= (long)cols * RowsVisible;
                break;
            case Keys.PageDown:
                pos += (long)cols * RowsVisible;
                break;
            case Keys.Tab:
                if (e.Control)
                    return;
                tab.Ascii = !tab.Ascii;
                CancelNibble();
                Reveal(tab.Start);
                Changed();
                goto done;
            case Keys.Insert:
                ToggleInsert();
                goto done;
            case Keys.Delete:
                Delete(false);
                goto done;
            case Keys.Back:
                Delete(true);
                goto done;
            case Keys.Escape:
                Navigate(tab.Start, false);
                goto done;
            default:
                return;
            }
            Navigate(pos, e.Shift);
        done:
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        private bool Hit(int x, int y, out long pos, out bool ascii)
        {
            pos = 0;
            ascii = false;
            if (tab == null || y < Header)
                return false;
            x += horizontal.Value;
            int col;
            if (x >= AsciiX(0) - Theme.D(2))
            {
                ascii = true;
                col = Math.Max(0, (x - AsciiX(0) + Theme.D(2)) / AsciiPitch);
                if (col >= tab.Columns)
                    return false;
            }
            else
            {
                if (x < HexX(0) - Theme.D(4) || x > HexX(tab.Columns - 1) + Pitch)
                    return false;
                col = 0;
                while (col + 1 < tab.Columns && x >= HexX(col + 1) - Theme.D(4))
                    col++;
            }
            long row = tab.FirstRow + (y - Header) / Row;
            pos = Math.Min(tab.Document.Length, row * tab.Columns + col);
            return row <= tab.Document.Length / tab.Columns || dragging;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            long pos;
            bool ascii;
            if (!Hit(e.X, e.Y, out pos, out ascii) || e.Button != MouseButtons.Left)
                return;
            tab.Ascii = ascii;
            bool shift = (ModifierKeys & Keys.Shift) != 0;
            mouseAnchor = shift ? tab.Anchor : pos;
            Navigate(pos, shift);
            dragging = true;
            Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (tab == null)
                return;
            long pos;
            bool ascii;
            if (dragging && e.Y < Header)
            {
                tab.FirstRow = Math.Max(0, tab.FirstRow - 1);
                UpdateScroll();
            }
            if (dragging && e.Y >= Header + RowsVisible * Row)
            {
                tab.FirstRow = Math.Min(MaxFirst, tab.FirstRow + 1);
                UpdateScroll();
            }
            if (!Hit(e.X, dragging ? Math.Max(Header, e.Y) : e.Y, out pos, out ascii))
                return;
            if (dragging && (pos != mouseAnchor || tab.Selected != 0))
            {
                tab.Anchor = pos < mouseAnchor ? Math.Min(tab.Document.Length, mouseAnchor + 1) : mouseAnchor;
                tab.Caret = pos >= mouseAnchor ? Math.Min(tab.Document.Length, pos + 1) : pos;
                Changed();
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
            Capture = false;
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture)
                dragging = false;
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (tab == null)
                return;
            int lines = SystemInformation.MouseWheelScrollLines;
            tab.FirstRow = Math.Min(
                MaxFirst, Math.Max(0, tab.FirstRow - (long)Math.Sign(e.Delta) * (lines < 0 ? RowsVisible : lines) *
                                                         Math.Max(1, Math.Abs(e.Delta) / 120)));
            UpdateScroll();
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.Clear(Theme.Paper);
            if (tab == null)
                return;
            int dx = horizontal.Value, end = Width - (vertical.Visible ? vertical.Width : 0);
            int bottom = Height - (horizontal.Visible ? horizontal.Height : 0);
            g.SetClip(new Rectangle(0, 0, Math.Max(0, end), Math.Max(0, bottom)));
            Theme.Fill(g, Theme.Surface, new Rectangle(0, 0, Math.Max(0, Gutter - dx), bottom));
            Theme.Fill(g, Theme.Surface, new Rectangle(0, 0, end, Header));
            Theme.Text(g, "OFFSET (h)", labels, Theme.Muted,
                       new Rectangle(Theme.D(16) - dx, 0, Gutter - Theme.D(18), Header));
            for (int i = 0; i < tab.Columns; i++)
                Theme.Text(g, i.ToString("X2"), small, Theme.Muted, new Rectangle(HexX(i) - dx, 0, Pitch, Header));
            Theme.Text(g, "ASCII", labels, Theme.Teal, new Rectangle(AsciiX(0) - dx, 0, Theme.D(80), Header));
            Theme.Rule(g, Theme.Line, 0, Header - 1, end, Header - 1);
            long first = tab.FirstRow * tab.Columns;
            int rows = Math.Max(0, (bottom - Header + Row - 1) / Row);
            var bytes = tab.Document.ReadBytes(first, rows * tab.Columns);
            long active = tab.Start, last = tab.Start + tab.Selected;
            for (int r = 0; r < rows; r++)
            {
                long start = first + (long)r * tab.Columns;
                if (start > tab.Document.Length)
                    break;
                int y = Header + r * Row;
                bool current = active >= start && active - start < tab.Columns;
                if (current)
                {
                    Theme.Fill(g, Color.FromArgb(245, 248, 255),
                               new Rectangle(Gutter - dx, y, Math.Max(0, end - Gutter + dx), Row));
                    Theme.Fill(g, Theme.BlueLight, new Rectangle(-dx, y, Gutter, Row));
                    Theme.Fill(g, Theme.Blue, new Rectangle(-dx, y, Theme.D(3), Row));
                }
                Theme.Text(g, start.ToString(tab.Document.Length > uint.MaxValue ? "X16" : "X8"), small,
                           current ? Theme.Blue : Theme.Muted,
                           new Rectangle(Theme.D(16) - dx, y, Gutter - Theme.D(18), Row));
                for (int col = 0; col < tab.Columns; col++)
                {
                    int i = r * tab.Columns + col;
                    long pos = start + col;
                    if (pos > tab.Document.Length)
                        break;
                    bool marked = tab.Selected == 0 ? pos == active : pos >= active && pos < last;
                    int x = HexX(col) - dx, ax = AsciiX(col) - dx;
                    if (marked)
                    {
                        Theme.Fill(g, tab.Ascii ? Theme.BlueLight : Theme.Blue,
                                   new Rectangle(x - Theme.D(3), y + 1, Pitch - Theme.D(2), Row - 2));
                        Theme.Fill(g, tab.Ascii ? Theme.Blue : Theme.BlueLight,
                                   new Rectangle(ax - 1, y + 1, AsciiPitch, Row - 2));
                    }
                    if (i >= bytes.Length)
                        break;
                    byte b = bytes[i];
                    bool printable = b >= 32 && b <= 126;
                    Color hc = marked ? (tab.Ascii ? Theme.Blue : Color.White) : b == 0 ? Theme.Faint : Theme.Ink;
                    Color ac = marked ? (tab.Ascii ? Color.White : Theme.Blue) : printable ? Theme.Teal : Theme.Faint;
                    Theme.Text(g, Hex[b], mono, hc, new Rectangle(x, y, Pitch, Row));
                    Theme.Text(g, printable ? ((char)b).ToString() : "·", mono, ac,
                               new Rectangle(ax, y, AsciiPitch + 2, Row));
                    if (marked && lowNibble)
                        Theme.Rule(g, Color.White, x + Theme.D(9), y + Row - Theme.D(4), x + Theme.D(16),
                                   y + Row - Theme.D(4));
                }
            }
            Theme.Rule(g, Theme.Line, Gutter - dx, 0, Gutter - dx, bottom);
            Theme.Rule(g, Theme.Line, AsciiLeft - dx, 0, AsciiLeft - dx, bottom);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                mono.Dispose();
                small.Dispose();
                labels.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
