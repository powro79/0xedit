using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ZeroXEdit
{
    internal sealed class CompareDialog : Form
    {
        private readonly ComboBox left = new ComboBox(), right = new ComboBox();
        private readonly ComparisonGrid grid = new ComparisonGrid { Dock = DockStyle.Fill };
        private readonly Label summary = new Label(), detail = new Label(), swatch = new Label();
        private readonly Button previous = new Button { Text = "Previous" }, next = new Button { Text = "Next" };
        private readonly Timer timer = new Timer { Interval = 15 };
        private readonly ToolTip tips = new ToolTip();
        private readonly List<BinaryDocument> diskFiles = new List<BinaryDocument>();
        private FileComparison comparison;
        private long range = -1;

        internal CompareDialog(IList<EditorTab> tabs, int active)
        {
            Text = "Compare files — 0xEdit";
            Font = Theme.Ui(9);
            BackColor = Theme.Paper;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimumSize = new Size(Theme.D(750), Theme.D(380));
            var area = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(area.Width, Theme.D(1220)), Math.Min(area.Height, Theme.D(700)));

            var pickers = new TableLayoutPanel {
                Dock = DockStyle.Top, Height = Theme.D(66), ColumnCount = 2, RowCount = 1, BackColor = Theme.Paper
            };
            pickers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pickers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pickers.Controls.Add(Picker("Left file", left), 0, 0);
            pickers.Controls.Add(Picker("Right file", right), 1, 0);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = Theme.D(56), BackColor = Theme.Surface };
            var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel };
            CancelButton = close;
            summary.AutoEllipsis = detail.AutoEllipsis = true;
            detail.ForeColor = Theme.Muted;
            swatch.BackColor = ComparisonGrid.Pink;
            swatch.Bounds = Theme.Box(12, 14, 11, 11);
            tips.SetToolTip(swatch, "Different bytes");
            tips.SetToolTip(previous, "Previous difference (Shift+F7)");
            tips.SetToolTip(next, "Next difference (F7)");
            previous.Click += delegate { MoveRange(-1); };
            next.Click += delegate { MoveRange(1); };
            footer.Controls.AddRange(new Control[] { swatch, summary, detail, previous, next, close });
            footer.Resize += delegate {
                int x = footer.Width - Theme.D(257);
                previous.SetBounds(x, Theme.D(13), Theme.D(82), Theme.D(30));
                next.SetBounds(x + Theme.D(88), Theme.D(13), Theme.D(68), Theme.D(30));
                close.SetBounds(x + Theme.D(171), Theme.D(13), Theme.D(74), Theme.D(30));
                summary.SetBounds(Theme.D(31), Theme.D(8), Math.Max(1, x - Theme.D(41)), Theme.D(20));
                detail.SetBounds(Theme.D(31), Theme.D(28), summary.Width, Theme.D(20));
            };
            Controls.Add(grid);
            Controls.Add(footer);
            Controls.Add(pickers);

            foreach (var tab in tabs) AddChoice(tab.Document, false);
            if (active >= 0)
            {
                left.SelectedIndex = active;
                if (tabs.Count > 1) right.SelectedIndex = active == 0 ? 1 : active - 1;
            }
            left.SelectedIndexChanged += delegate { CompareSelection(); };
            right.SelectedIndexChanged += delegate { CompareSelection(); };
            grid.ReadError += ShowReadError;
            timer.Tick += delegate { Scan(); };
            CompareSelection();
        }
        private Panel Picker(string name, ComboBox selector)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var label = new Label { Text = name, ForeColor = Theme.Muted, Bounds = Theme.Box(12, 4, 150, 20) };
            selector.AccessibleName = name;
            selector.DropDownStyle = ComboBoxStyle.DropDownList;
            selector.DisplayMember = "Key";
            var browse = new Button { Text = "Browse…", AccessibleName = "Browse for " + name.ToLowerInvariant() };
            browse.Click += delegate {
                using (var dialog = new OpenFileDialog { Title = "Choose " + name.ToLowerInvariant(),
                            Filter = "All files (*.*)|*.*", RestoreDirectory = true })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    try { OpenPath(selector, dialog.FileName); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open comparison file", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            };
            panel.Controls.AddRange(new Control[] { label, selector, browse });
            panel.Resize += delegate {
                selector.SetBounds(Theme.D(12), Theme.D(28), Math.Max(1, panel.Width - Theme.D(109)), selector.PreferredHeight);
                browse.SetBounds(panel.Width - Theme.D(90), Theme.D(25), Theme.D(78), Theme.D(29));
            };
            return panel;
        }
        private void AddChoice(BinaryDocument doc, bool disk)
        {
            string label = doc.Name + (doc.Dirty ? " *" : "") + (disk ? " (disk)" : " (open tab)");
            if (doc.FilePath != null) label += "  —  " + Path.GetDirectoryName(doc.FilePath);
            var choice = new KeyValuePair<string, BinaryDocument>(label, doc);
            left.Items.Add(choice);
            right.Items.Add(choice);
        }
        private void OpenPath(ComboBox selector, string path)
        {
            string full = Path.GetFullPath(path);
            var doc = diskFiles.Find(d => string.Equals(d.FilePath, full, StringComparison.OrdinalIgnoreCase));
            if (doc == null)
            {
                doc = BinaryDocument.Open(full);
                diskFiles.Add(doc);
                AddChoice(doc, true);
            }
            for (int i = 0; i < selector.Items.Count; i++)
                if (((KeyValuePair<string, BinaryDocument>)selector.Items[i]).Value == doc)
                {
                    selector.SelectedIndex = i;
                    break;
                }
        }
        private static BinaryDocument SelectedFile(ComboBox selector)
        {
            return selector.SelectedIndex < 0 ? null : ((KeyValuePair<string, BinaryDocument>)selector.SelectedItem).Value;
        }
        private void CompareSelection()
        {
            timer.Stop();
            var a = SelectedFile(left);
            var b = SelectedFile(right);
            comparison = a == null || b == null ? null : new FileComparison(a, b);
            range = -1;
            grid.Bind(comparison);
            tips.SetToolTip(left, a == null ? "" : (a.FilePath ?? a.Name) + "  ·  " + ByteTools.Size(a.Length));
            tips.SetToolTip(right, b == null ? "" : (b.FilePath ?? b.Name) + "  ·  " + ByteTools.Size(b.Length));
            UpdateStatus();
            if (comparison != null && !comparison.Complete) timer.Start();
        }
        private void Scan()
        {
            if (comparison == null) return;
            try
            {
                var elapsed = Stopwatch.StartNew();
                do { comparison.ScanBlock(); }
                while (!comparison.Complete && elapsed.ElapsedMilliseconds < 8);
                if (comparison.Complete) timer.Stop();
                if (range < 0 && comparison.Ranges > 0)
                {
                    range = 0;
                    grid.Reveal(comparison.RangeStart(range));
                }
                UpdateStatus();
            }
            catch (IOException ex) { ShowReadError(ex); }
        }
        private void ShowReadError(IOException ex)
        {
            timer.Stop();
            comparison = null;
            grid.Bind(null);
            UpdateStatus();
            summary.Text = "Could not read the comparison files.";
            detail.Text = ex.Message;
        }
        private void UpdateStatus()
        {
            swatch.Visible = comparison != null && comparison.DifferentBytes > 0;
            previous.Enabled = comparison != null && range >= 0 && (range > 0 || comparison.Complete);
            next.Enabled = comparison != null && range >= 0 && (range + 1 < comparison.Ranges || comparison.Complete);
            if (comparison == null) { summary.Text = detail.Text = ""; return; }
            long count = comparison.DifferentBytes;
            string difference = count.ToString("N0") + (count == 1 ? " differing byte" : " differing bytes") +
                                " in " + comparison.Ranges.ToString("N0") + (comparison.Ranges == 1 ? " range" : " ranges");
            if (!comparison.Complete)
                summary.Text = "Comparing… " + (int)(100.0 * comparison.Scanned / Math.Max(1, comparison.Length)) + "%  ·  " + difference;
            else
                summary.Text = count == 0 ? "Files are identical." : difference;
            detail.Text = range < 0 ? "" : "Range " + (range + 1).ToString("N0") + "  ·  Offset 0x" + grid.HighlightOffset.ToString("X");
            if (comparison.Left.Length != comparison.Right.Length)
                detail.Text += (range < 0 ? "" : "  ·  ") + "-- marks a missing byte";
        }
        private void MoveRange(int delta)
        {
            if (comparison == null || comparison.Ranges == 0) return;
            long target = range + delta;
            if (target < 0) target = comparison.Complete ? comparison.Ranges - 1 : 0;
            if (target >= comparison.Ranges) target = comparison.Complete ? 0 : comparison.Ranges - 1;
            try
            {
                grid.Reveal(comparison.RangeStart(target));
                range = target;
                UpdateStatus();
                grid.Focus();
            }
            catch (IOException ex) { ShowReadError(ex); }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            if (key == Keys.F7 || key == (Keys.Shift | Keys.F7))
            {
                MoveRange(key == Keys.F7 ? 1 : -1);
                return true;
            }
            return base.ProcessCmdKey(ref msg, key);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                tips.Dispose();
                foreach (var doc in diskFiles) doc.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
