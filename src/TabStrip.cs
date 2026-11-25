using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ZeroXEdit
{
    internal sealed class TabStrip : Control
    {
        private readonly IList<EditorTab> tabs;
        private readonly Font tabFont = Theme.Ui(9, true);
        private readonly ToolTip tip = new ToolTip();
        private int first, current;
        internal event Action<int> ActivateTab, CloseTab;
        internal event Action NewTab;
        internal TabStrip(IList<EditorTab> tabs)
        {
            this.tabs = tabs;
            Height = Theme.D(39);
            DoubleBuffered = true;
            BackColor = Theme.Tabs;
            AccessibleName = "Open files";
            Cursor = Cursors.Hand;
        }
        private int TabWidth
        {
            get {
                return Theme.D(178);
            }
        }
        private int Slots
        {
            get {
                return Math.Max(1, (Width - Theme.D(87)) / TabWidth);
            }
        }
        internal void RefreshTabs(int active)
        {
            current = active;
            first = Math.Max(0, Math.Min(first, tabs.Count - Slots));
            if (current < first)
                first = current;
            if (current >= first + Slots)
                first = current - Slots + 1;
            first = Math.Max(0, first);
            Invalidate();
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (tabs != null)
                RefreshTabs(current);
        }
        private int VisibleCount
        {
            get {
                return Math.Min(Slots, tabs.Count - first);
            }
        }
        private int StartX
        {
            get {
                return Theme.D(10);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.Rule(g, Theme.Line, 0, Height - 1, Width, Height - 1);
            for (int i = 0; i < VisibleCount; i++)
            {
                int index = first + i, x = StartX + i * TabWidth;
                bool active = index == current;
                if (active)
                {
                    Theme.Fill(g, Theme.Paper, new Rectangle(x, 1, TabWidth, Height - 1));
                    Theme.Fill(g, Theme.Blue, new Rectangle(x, 0, TabWidth, Theme.D(3)));
                }
                Theme.Rule(g, Theme.Line, x + TabWidth - 1, Theme.D(9), x + TabWidth - 1, Height - Theme.D(8));
                Theme.Glyph(g, "file", active ? Theme.Blue : Theme.Muted,
                            new Rectangle(x + Theme.D(13), Theme.D(11), Theme.D(16), Theme.D(16)));
                var doc = tabs[index].Document;
                Theme.Text(g, doc.Name, tabFont, active ? Theme.Ink : Theme.Muted,
                           new Rectangle(x + Theme.D(36), 1, TabWidth - Theme.D(70), Height - 1));
                if (doc.Dirty)
                {
                    using (var brush = new SolidBrush(Theme.Blue))
                        g.FillEllipse(brush, x + TabWidth - Theme.D(25), Theme.D(17), Theme.D(7), Theme.D(7));
                }
                else
                    Theme.Glyph(g, "close", Theme.Muted,
                                new Rectangle(x + TabWidth - Theme.D(31), Theme.D(12), Theme.D(15), Theme.D(15)));
            }
            Theme.Glyph(
                g, "plus", Theme.Muted,
                new Rectangle(StartX + VisibleCount * TabWidth + Theme.D(11), Theme.D(11), Theme.D(17), Theme.D(17)));
            if (tabs.Count > Slots)
                Theme.Glyph(g, "down", Theme.Ink,
                            new Rectangle(Width - Theme.D(30), Theme.D(11), Theme.D(18), Theme.D(18)));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int local = e.X - StartX;
            int index = first + Math.Max(0, local) / TabWidth;
            string text = "";
            if (local >= 0 && index < first + VisibleCount)
                text = local % TabWidth >= TabWidth - Theme.D(34)
                           ? "Close tab"
                           : tabs[index].Document.FilePath ?? tabs[index].Document.Name;
            else if (e.X >= StartX + VisibleCount * TabWidth && e.X < StartX + VisibleCount * TabWidth + Theme.D(38))
                text = "New file";
            else if (tabs.Count > Slots && e.X > Width - Theme.D(38))
                text = "All open files";
            if (tip.GetToolTip(this) != text)
                tip.SetToolTip(this, text);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int local = e.X - StartX;
            if (local < 0)
                return;
            int index = first + local / TabWidth;
            if (index < first + VisibleCount)
            {
                if (e.Button == MouseButtons.Middle ||
                    (e.Button == MouseButtons.Left && local % TabWidth >= TabWidth - Theme.D(34)))
                    CloseTab(index);
                else if (e.Button == MouseButtons.Left)
                    ActivateTab(index);
            }
            else if (e.Button == MouseButtons.Left && local < VisibleCount * TabWidth + Theme.D(38))
                NewTab();
            else if (e.Button == MouseButtons.Left && tabs.Count > Slots && e.X > Width - Theme.D(38))
            {
                var menu = new ContextMenuStrip();
                for (int i = 0; i < tabs.Count; i++)
                {
                    int selected = i;
                    var entry = menu.Items.Add(tabs[i].Document.Name, null, delegate { ActivateTab(selected); });
                    ((ToolStripMenuItem)entry).Checked = i == current;
                }
                menu.Closed += delegate
                {
                    menu.Dispose();
                };
                menu.Show(this, new Point(e.X, Height));
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tip.Dispose();
                tabFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
