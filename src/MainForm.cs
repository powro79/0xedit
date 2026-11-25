using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace ZeroXEdit
{
    internal sealed class MainForm : Form
    {
        private readonly List<EditorTab> tabs = new List<EditorTab>();
        private readonly HexView grid = new HexView();
        private readonly Inspector inspector = new Inspector();
        private readonly TabStrip strip;
        private readonly Panel brand = new Panel(), toolbar = new Panel(), workspace = new Panel(),
                               editor = new Panel(), viewHeader = new Panel(), findBar = new Panel(),
                               status = new Panel();
        private readonly MenuStrip menu = new MenuStrip();
        private readonly ToolTip tips = new ToolTip();
        private readonly Font wordmark = Theme.Ui(14, true), heading = Theme.Ui(12, true), statusFont = Theme.Ui(9),
                              monoStatus = Theme.Mono(9);
        private readonly TextBox query = new TextBox();
        private readonly ComboBox findType = new ComboBox(), columns = new ComboBox();
        private readonly Label findMessage = new Label();
        private readonly Timer searchTimer = new Timer();
        private readonly Dictionary<string, ToolStripMenuItem> commands = new Dictionary<string, ToolStripMenuItem>();
        private ActionButton undoButton, redoButton, modeButton, inspectorButton;
        private int active = -1, untitled, patternLength;
        private bool showInspector = true;
        private PatternSearch search;
        private EditorTab Tab
        {
            get {
                return active < 0 ? null : tabs[active];
            }
        }

        internal MainForm(string[] paths)
        {
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Ui(9);
            BackColor = Theme.Paper;
            Text = "0xEdit";
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            MinimumSize = new Size(Theme.D(720), Theme.D(480));
            var screen = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(screen.Width, Theme.D(1240)), Math.Min(screen.Height, Theme.D(832)));
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            AllowDrop = true;
            strip = new TabStrip(tabs);
            strip.NewTab += NewFile;
            strip.CloseTab += CloseTab;
            strip.ActivateTab += Activate;
            BuildMenus();
            BuildToolbar();
            BuildWorkspace();
            BuildFind();
            BuildStatus();
            brand.Dock = toolbar.Dock = strip.Dock = DockStyle.Top;
            status.Dock = DockStyle.Bottom;
            workspace.Dock = DockStyle.Fill;
            Controls.Add(workspace);
            Controls.Add(status);
            Controls.Add(strip);
            Controls.Add(toolbar);
            Controls.Add(brand);
            grid.ViewChanged += RefreshUI;
            grid.BytesChanged += delegate
            {
                ResetFind();
                inspector.RefreshDocument(Tab);
            };
            searchTimer.Interval = 15;
            searchTimer.Tick += delegate
            {
                SearchTick();
            };
            DragEnter += delegate(object sender, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effect = DragDropEffects.Copy;
            };
            DragDrop += delegate(object sender, DragEventArgs e)
            {
                OpenPaths((string[])e.Data.GetData(DataFormats.FileDrop));
            };
            if (paths.Length > 0)
                OpenPaths(paths);
            if (Tab == null)
            {
                if (paths.Length == 0)
                    OpenExample();
                else
                    NewFile();
            }
            Shown += delegate
            {
                LayoutInspector();
                grid.Focus();
            };
        }
        private void BuildMenus()
        {
            brand.Height = Theme.D(47);
            brand.BackColor = Theme.Paper;
            brand.Paint += delegate(object s, PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Theme.Round(g, Theme.Blue, Theme.Box(15, 9, 29, 29), Theme.D(6));
                Theme.Glyph(g, "brand", Color.White, Theme.Box(18, 12, 23, 23));
                Theme.Text(g, "0xEdit", wordmark, Theme.Ink, Theme.Box(53, 0, 73, 46));
                Theme.Rule(g, Theme.Line, Theme.D(130), Theme.D(13), Theme.D(130), Theme.D(34));
                Theme.Rule(g, Theme.Line, 0, brand.Height - 1, brand.Width, brand.Height - 1);
            };
            menu.Dock = DockStyle.None;
            menu.AutoSize = true;
            menu.Location = new Point(Theme.D(140), Theme.D(10));
            menu.Font = Theme.Ui(9);
            menu.BackColor = Theme.Paper;
            menu.ForeColor = Theme.Ink;
            menu.RenderMode = ToolStripRenderMode.System;
            brand.Controls.Add(menu);
            MainMenuStrip = menu;
            var file = AddMenu("&File");
            var edit = AddMenu("&Edit");
            var searchMenu = AddMenu("&Search");
            var view = AddMenu("&View");
            var help = AddMenu("&Help");
            Item(file, "new", "&New", "Ctrl+N", NewFile);
            Item(file, "open", "&Open…", "Ctrl+O", OpenDialog);
            Item(file, "example", "Open &example", "", OpenExample);
            Sep(file);
            Item(file, "save", "&Save", "Ctrl+S", () => Save(Tab, false));
            Item(file, "saveas", "Save &as…", "Ctrl+Shift+S", () => Save(Tab, true));
            Item(file, "export", "Save se&lection as…", "", ExportSelection);
            Sep(file);
            Item(file, "close", "&Close tab", "Ctrl+W", () => CloseTab(active));
            Item(file, "exit", "E&xit", "Alt+F4", Close);
            Item(edit, "undo", "&Undo", "Ctrl+Z", () => grid.History(false));
            Item(edit, "redo", "&Redo", "Ctrl+Y", () => grid.History(true));
            Sep(edit);
            Item(edit, "cut", "Cu&t", "Ctrl+X", () => Copy(true));
            Item(edit, "copy", "&Copy", "Ctrl+C", () => Copy(false));
            Item(edit, "paste", "&Paste", "Ctrl+V", Paste);
            Item(edit, "delete", "&Delete", "Del", () => grid.Delete(false));
            Sep(edit);
            Item(edit, "all", "Select &all", "Ctrl+A", () => grid.SelectRange(0, Tab.Document.Length));
            Item(edit, "block", "Select &block…", "Ctrl+E", SelectBlock);
            Item(edit, "mode", "&Insert mode", "Ins", grid.ToggleInsert);
            Item(searchMenu, "find", "&Find…", "Ctrl+F", ShowFind);
            Item(searchMenu, "next", "Find &next", "F3", () => Find(false));
            Item(searchMenu, "prev", "Find &previous", "Shift+F3", () => Find(true));
            Sep(searchMenu);
            Item(searchMenu, "goto", "&Go to offset…", "Ctrl+G", GoTo);
            Item(view, "hash", "Calculate SHA-&256…", "",
                 () =>
                 { using (var dialog = new HashDialog(Tab)) dialog.ShowDialog(this); });
            Sep(view);
            Item(view, "inspector", "Data &inspector", "", ToggleInspector);
            Item(view, "nexttab", "&Next tab", "Ctrl+Tab", () => Cycle(1));
            Item(view, "prevtab", "&Previous tab", "Ctrl+Shift+Tab", () => Cycle(-1));
            Item(help, "about", "&About 0xEdit", "",
                 () => MessageBox.Show(
                     this, "0xEdit\r\nOpen-source binary editor for Windows.\r\n\r\nReleased under the MIT License.",
                     "About 0xEdit", MessageBoxButtons.OK, MessageBoxIcon.Information));
        }
        private ToolStripMenuItem AddMenu(string label)
        {
            var item = new ToolStripMenuItem(label);
            menu.Items.Add(item);
            return item;
        }
        private void Item(ToolStripMenuItem parent, string id, string label, string shortcut, Action run)
        {
            var item = new ToolStripMenuItem(label);
            item.ShortcutKeyDisplayString = shortcut;
            item.Click += delegate
            {
                run();
            };
            parent.DropDownItems.Add(item);
            commands[id] = item;
        }
        private static void Sep(ToolStripMenuItem item)
        {
            item.DropDownItems.Add(new ToolStripSeparator());
        }
        private ActionButton ButtonAt(Panel panel, string icon, string label, int x, int y, int width, string tip,
                                      Action action, bool primary = false)
        {
            var button = new ActionButton(icon, label, width, action, primary);
            button.Location = new Point(Theme.D(x), Theme.D(y));
            panel.Controls.Add(button);
            tips.SetToolTip(button, tip);
            return button;
        }
        private void BuildToolbar()
        {
            toolbar.Height = Theme.D(53);
            toolbar.BackColor = Theme.Paper;
            ButtonAt(toolbar, "open", "Open file", 15, 10, 99, "Open file (Ctrl+O)", OpenDialog, true);
            ButtonAt(toolbar, "new", "New", 124, 10, 69, "New file (Ctrl+N)", NewFile);
            ButtonAt(toolbar, "save", "Save", 196, 10, 71, "Save file (Ctrl+S)", () => Save(Tab, false));
            undoButton = ButtonAt(toolbar, "undo", "", 285, 10, 35, "Undo (Ctrl+Z)", () => grid.History(false));
            redoButton = ButtonAt(toolbar, "redo", "", 322, 10, 35, "Redo (Ctrl+Y)", () => grid.History(true));
            ButtonAt(toolbar, "find", "Find", 375, 10, 67, "Find bytes or text (Ctrl+F)", ShowFind);
            ButtonAt(toolbar, "goto", "Go to", 446, 10, 75, "Go to offset (Ctrl+G)", GoTo);
            modeButton = ButtonAt(toolbar, "edit", "Overwrite", 530, 10, 110, "Switch insert / overwrite (Insert)",
                                  () =>
                                  {
                                      grid.ToggleInsert();
                                      grid.Focus();
                                  });
            inspectorButton = ButtonAt(toolbar, "panel", "", 650, 10, 35, "Toggle data inspector", ToggleInspector);
            toolbar.Resize += delegate
            {
                inspectorButton.Left = toolbar.Width - Theme.D(49);
                modeButton.Left = inspectorButton.Left - Theme.D(119);
                modeButton.Visible = toolbar.Width >= Theme.D(710);
            };
            toolbar.Paint += delegate(object s, PaintEventArgs e)
            {
                foreach (int x in new[] { 276, 365 })
                    Theme.Rule(e.Graphics, Theme.Line, Theme.D(x), Theme.D(18), Theme.D(x), Theme.D(35));
                Theme.Rule(e.Graphics, Theme.Line, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
            };
        }
        private void BuildWorkspace()
        {
            inspector.Dock = DockStyle.Right;
            inspector.Width = Theme.D(280);
            editor.Dock = DockStyle.Fill;
            grid.Dock = DockStyle.Fill;
            viewHeader.Height = Theme.D(53);
            viewHeader.Dock = DockStyle.Top;
            viewHeader.BackColor = Theme.Paper;
            viewHeader.Paint += delegate(object s, PaintEventArgs e)
            {
                Theme.Glyph(e.Graphics, "grid", Theme.Blue, Theme.Box(16, 18, 19, 18));
                Theme.Text(e.Graphics, "Hex view", heading, Theme.Ink, Theme.Box(44, 0, 160, 53));
                Theme.Rule(e.Graphics, Theme.Line, 0, viewHeader.Height - 1, viewHeader.Width, viewHeader.Height - 1);
            };
            columns.DropDownStyle = ComboBoxStyle.DropDownList;
            columns.FlatStyle = FlatStyle.Flat;
            columns.Font = Theme.Ui(9);
            columns.AccessibleName = "Bytes per row";
            columns.Items.AddRange(new object[] { "8 bytes / row", "16 bytes / row", "32 bytes / row" });
            columns.Width = Theme.D(120);
            columns.SelectedIndex = 1;
            columns.SelectedIndexChanged += delegate
            {
                if (Tab != null && Tab.Columns != (8 << columns.SelectedIndex))
                    grid.SetColumns(8 << columns.SelectedIndex);
            };
            viewHeader.Controls.Add(columns);
            viewHeader.Resize += delegate
            {
                columns.Location =
                    new Point(viewHeader.Width - columns.Width - Theme.D(16), (viewHeader.Height - columns.Height) / 2);
            };
            editor.Controls.Add(grid);
            editor.Controls.Add(findBar);
            editor.Controls.Add(viewHeader);
            workspace.Controls.Add(editor);
            workspace.Controls.Add(inspector);
            workspace.Resize += delegate
            {
                LayoutInspector();
            };
            var popup = new ContextMenuStrip();
            popup.Items.Add("Cut", null, delegate { Copy(true); });
            popup.Items.Add("Copy", null, delegate { Copy(false); });
            popup.Items.Add("Paste", null, delegate { Paste(); });
            popup.Items.Add("Select all", null, delegate { grid.SelectRange(0, Tab.Document.Length); });
            popup.Opening += delegate
            {
                popup.Items[0].Enabled = popup.Items[1].Enabled = Tab != null && Tab.Selected > 0;
            };
            grid.ContextMenuStrip = popup;
        }
        private void LayoutInspector()
        {
            inspector.Visible = showInspector && workspace.Width >= Theme.D(1090);
            if (commands.ContainsKey("inspector"))
                commands["inspector"].Checked = inspector.Visible;
        }
        private void ToggleInspector()
        {
            if (workspace.Width < Theme.D(1090))
            {
                using (var dialog = new Form { Text = "Data inspector", StartPosition = FormStartPosition.CenterParent,
                                               ClientSize = new Size(Theme.D(300), Theme.D(590)), MinimizeBox = false,
                                               MaximizeBox =
                                                   false }) using (var detail = new Inspector { Dock = DockStyle.Fill })
                {
                    detail.RefreshDocument(Tab);
                    dialog.Controls.Add(detail);
                    dialog.ShowDialog(this);
                }
            }
            else
            {
                showInspector = !showInspector;
                LayoutInspector();
            }
        }
        private void BuildFind()
        {
            findBar.Dock = DockStyle.Top;
            findBar.Height = Theme.D(71);
            findBar.BackColor = Theme.Surface;
            findBar.Visible = false;
            query.SetBounds(Theme.D(16), Theme.D(9), Theme.D(195), Theme.D(28));
            query.Font = Theme.Mono(10);
            query.AccessibleName = "Search value";
            findType.SetBounds(Theme.D(218), Theme.D(9), Theme.D(107), Theme.D(26));
            findType.DropDownStyle = ComboBoxStyle.DropDownList;
            findType.Items.AddRange(new object[] { "Hex bytes", "Text (UTF-8)" });
            findType.SelectedIndex = 0;
            findType.AccessibleName = "Search format";
            findMessage.ForeColor = Theme.Muted;
            findMessage.Bounds = Theme.Box(16, 40, 600, 24);
            findBar.Controls.AddRange(new Control[] { query, findType, findMessage });
            var prev =
                new Button { Text = "Previous", Bounds = Theme.Box(335, 7, 77, 29), AccessibleName = "Previous match" };
            var next = new Button { Text = "Next", Bounds = Theme.Box(418, 7, 65, 29), AccessibleName = "Next match" };
            prev.Click += delegate
            {
                Find(true);
            };
            next.Click += delegate
            {
                Find(false);
            };
            findBar.Controls.AddRange(new Control[] { prev, next });
            var close = ButtonAt(findBar, "close", "", 491, 6, 32, "Close search (Esc)", HideFind);
            findBar.Resize += delegate
            {
                close.Left = findBar.Width - close.Width - Theme.D(8);
                findMessage.Width = Math.Max(1, findBar.Width - Theme.D(30));
            };
            query.TextChanged += delegate
            {
                ResetFind();
            };
            findType.SelectedIndexChanged += delegate
            {
                ResetFind();
            };
            query.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    Find(e.Shift);
                    e.SuppressKeyPress = true;
                }
            };
        }
        private void BuildStatus()
        {
            status.Height = Theme.D(30);
            status.BackColor = Theme.Navy;
            status.Paint += delegate(object s, PaintEventArgs e)
            {
                if (Tab == null)
                    return;
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                string state = Tab.Document.Dirty              ? "Modified"
                               : Tab.Document.IsExample        ? "Example file"
                               : Tab.Document.FilePath == null ? "New file"
                                                               : "Saved";
                using (var dot = new SolidBrush(Tab.Document.Dirty ? Color.FromArgb(255, 197, 100)
                                                                   : Color.FromArgb(111, 211, 184)))
                    g.FillEllipse(dot, Theme.D(16), Theme.D(12), Theme.D(7), Theme.D(7));
                Theme.Text(g, state, statusFont, Color.White, Theme.Box(31, 0, 95, 30));
                Theme.Text(g, ByteTools.Size(Tab.Document.Length), monoStatus, Color.FromArgb(191, 205, 225),
                           Theme.Box(134, 0, 100, 30));
                string info = "Offset  0x" + Tab.OffsetText + "     |     Selection  " + Tab.Selected.ToString("N0") +
                              " B     |     " + (Tab.Ascii ? "ASCII" : "HEX");
                Theme.Text(g, info, monoStatus, Color.FromArgb(223, 232, 247),
                           new Rectangle(Theme.D(240), 0, Math.Max(1, status.Width - Theme.D(255)), status.Height),
                           TextFormatFlags.Right);
            };
        }
        private void Add(BinaryDocument document)
        {
            tabs.Add(new EditorTab(document));
            Activate(tabs.Count - 1);
        }
        private void NewFile()
        {
            Add(BinaryDocument.Create("Untitled-" + (++untitled), new byte[0], false));
        }
        private void OpenExample()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                       "ZeroXEdit.aurora.png")) using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                Add(BinaryDocument.Create("aurora.png", buffer.ToArray(), true));
            }
        }
        private void Activate(int index)
        {
            if (index < 0 || index >= tabs.Count)
                return;
            StopSearch();
            active = index;
            grid.Bind(Tab);
            inspector.RefreshDocument(Tab);
            columns.SelectedIndex = Tab.Columns == 8 ? 0 : Tab.Columns == 16 ? 1 : 2;
            ResetFind();
            RefreshUI();
            grid.Focus();
        }
        private void Cycle(int delta)
        {
            if (tabs.Count > 0)
                Activate((active + delta + tabs.Count) % tabs.Count);
        }
        private void OpenDialog()
        {
            using (var dialog = new OpenFileDialog {
                Title = "Open binary file", Filter = "All files (*.*)|*.*", Multiselect = true, RestoreDirectory = true
            }) if (dialog.ShowDialog(this) == DialogResult.OK) OpenPaths(dialog.FileNames);
        }
        private int FindPath(string path)
        {
            return tabs.FindIndex(t => string.Equals(t.Document.FilePath, path, StringComparison.OrdinalIgnoreCase));
        }
        private void OpenPaths(string[] paths)
        {
            foreach (string path in paths)
                try
                {
                    string full = Path.GetFullPath(path);
                    int index = FindPath(full);
                    if (index >= 0)
                        Activate(index);
                    else
                        Add(BinaryDocument.Open(full));
                }
                catch (Exception ex)
                {
                    Error("Open file", ex);
                }
        }
        private bool Save(EditorTab tab, bool saveAs)
        {
            if (tab == null)
                return false;
            grid.CancelNibble();
            grid.Invalidate();
            try
            {
                string path = tab.Document.FilePath;
                if (saveAs || path == null)
                {
                    using (var dialog = new SaveFileDialog { Title = "Save binary file", Filter = "All files (*.*)|*.*",
                                                             FileName = tab.Document.Name, OverwritePrompt = true,
                                                             AddExtension = false, RestoreDirectory = true })
                    {
                        if (path != null)
                            dialog.InitialDirectory = Path.GetDirectoryName(path);
                        if (dialog.ShowDialog(this) != DialogResult.OK)
                            return false;
                        path = Path.GetFullPath(dialog.FileName);
                    }
                    int index = FindPath(path);
                    if (index >= 0 && tabs[index] != tab)
                        throw new IOException(
                            "This file is open in another tab. Close that tab or choose a different name.");
                }
                else if (tab.Document.ChangedOnDisk() &&
                         MessageBox.Show(this, "The file on disk has changed. Replace it with the bytes in this tab?",
                                         "File changed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                                         MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return false;
                tab.Document.Save(path);
                RefreshUI();
                return true;
            }
            catch (Exception ex)
            {
                Error("Save file", ex);
                return false;
            }
        }
        private bool CanClose(EditorTab tab)
        {
            if (!tab.Document.Dirty)
                return true;
            var result = MessageBox.Show(this, "Save changes to " + tab.Document.Name + "?", "0xEdit",
                                         MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            return result == DialogResult.No || (result == DialogResult.Yes && Save(tab, false));
        }
        private void ExportSelection()
        {
            if (Tab.Selected == 0)
                return;
            using (var dialog = new SaveFileDialog { Title = "Save selected bytes", Filter = "All files (*.*)|*.*",
                                                     FileName = "selection.bin", RestoreDirectory = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                try
                {
                    if (FindPath(Path.GetFullPath(dialog.FileName)) >= 0)
                        throw new IOException("Choose a file that is not open in the editor.");
                    Tab.Document.WriteRange(dialog.FileName, Tab.Start, Tab.Selected);
                }
                catch (Exception ex)
                {
                    Error("Save selection", ex);
                }
            }
        }
        private void CloseTab(int index)
        {
            if (index < 0 || index >= tabs.Count)
                return;
            var closing = tabs[index];
            if (!CanClose(closing))
                return;
            var previous = Tab;
            StopSearch();
            tabs.RemoveAt(index);
            active = -1;
            grid.Bind(null);
            closing.Document.Dispose();
            if (tabs.Count == 0)
                NewFile();
            else
                Activate(previous == closing ? Math.Min(index, tabs.Count - 1) : tabs.IndexOf(previous));
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel)
                return;
            foreach (var tab in tabs)
                if (!CanClose(tab))
                {
                    e.Cancel = true;
                    return;
                }
        }
        private void RefreshUI()
        {
            if (Tab == null)
                return;
            var doc = Tab.Document;
            Text = (doc.Dirty ? "*" : "") + doc.Name + " — 0xEdit";
            strip.RefreshTabs(active);
            inspector.RefreshValues();
            status.Invalidate();
            undoButton.Enabled = commands["undo"].Enabled = doc.CanUndo;
            redoButton.Enabled = commands["redo"].Enabled = doc.CanRedo;
            commands["cut"].Enabled = commands["copy"].Enabled = Tab.Selected > 0;
            commands["export"].Enabled = Tab.Selected > 0;
            commands["block"].Enabled = doc.Length > 0;
            commands["delete"].Enabled = Tab.Selected > 0 || Tab.Caret < doc.Length;
            commands["mode"].Checked = Tab.Insert;
            modeButton.Text = Tab.Insert ? "Insert" : "Overwrite";
        }
        private void GoTo()
        {
            using (var dialog =
                       new OffsetDialog(Tab.Start, Tab.Document.Length)) if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                grid.Navigate(dialog.Offset, false);
                grid.Focus();
            }
        }
        private void SelectBlock()
        {
            if (Tab.Document.Length == 0)
                return;
            long start = Math.Min(Tab.Start, Tab.Document.Length - 1);
            using (var dialog =
                       new OffsetDialog(start, Tab.Document.Length,
                                        Math.Max(1, Tab.Selected))) if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                grid.SelectRange(dialog.Offset, dialog.Offset + dialog.Count);
                grid.Focus();
            }
        }
        private void ShowFind()
        {
            findBar.Visible = true;
            ResetFind();
            query.Focus();
            query.SelectAll();
        }
        private void HideFind()
        {
            StopSearch();
            findBar.Visible = false;
            grid.Focus();
        }
        private void StopSearch()
        {
            searchTimer.Stop();
            search = null;
        }
        private void ResetFind()
        {
            StopSearch();
            findMessage.Text = findType.SelectedIndex == 0 ? "Enter a byte sequence, such as 89 50 4E 47."
                                                           : "Enter case-sensitive UTF-8 text.";
        }
        private void Find(bool backward)
        {
            if (query.Text.Length == 0)
            {
                ShowFind();
                return;
            }
            StopSearch();
            findBar.Visible = true;
            try
            {
                byte[] pattern =
                    findType.SelectedIndex == 0 ? ByteTools.ParseHex(query.Text) : Encoding.UTF8.GetBytes(query.Text);
                long start = Tab.Start + (Tab.Selected > 0 ? (backward ? -1 : 1) : 0);
                search = new PatternSearch(Tab.Document, pattern, start, backward);
                patternLength = pattern.Length;
                findMessage.Text = "Searching…";
                SearchTick();
                if (search != null)
                    searchTimer.Start();
            }
            catch (Exception ex)
            {
                findMessage.Text = ex.Message;
                StopSearch();
            }
        }
        private void SearchTick()
        {
            if (search == null)
                return;
            try
            {
                if (!search.Step(262144))
                    return;
                long found = search.Result;
                StopSearch();
                if (found < 0)
                {
                    findMessage.Text = "No matches in this file.";
                    return;
                }
                grid.SelectRange(found, found + patternLength);
                findMessage.Text = "Match at 0x" + Tab.OffsetText + "   ·   " + patternLength + " byte" +
                                   (patternLength == 1 ? "" : "s");
            }
            catch (Exception ex)
            {
                StopSearch();
                findMessage.Text = ex.Message;
            }
        }
        private void Copy(bool cut)
        {
            if (Tab.Selected == 0)
                return;
            try
            {
                if (Tab.Selected > BinaryClipboard.Limit)
                    throw new InvalidOperationException("Select up to 16 MiB to copy to the clipboard.");
                byte[] bytes = Tab.Document.ReadBytes(Tab.Start, (int)Tab.Selected);
                BinaryClipboard.Write(bytes, Tab.Ascii);
                if (cut)
                    grid.Delete(false);
            }
            catch (Exception ex)
            {
                Error("Clipboard", ex);
            }
        }
        private void Paste()
        {
            try
            {
                byte[] bytes = BinaryClipboard.Read(Tab.Ascii);
                grid.Put(bytes);
                grid.Focus();
            }
            catch (Exception ex)
            {
                Error("Paste bytes", ex);
            }
        }
        private void Error(string title, Exception ex)
        {
            MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            if (Tab == null)
                return base.ProcessCmdKey(ref msg, key);
            switch (key)
            {
            case Keys.Control | Keys.N:
                NewFile();
                return true;
            case Keys.Control | Keys.O:
                OpenDialog();
                return true;
            case Keys.Control | Keys.S:
                Save(Tab, false);
                return true;
            case Keys.Control | Keys.Shift | Keys.S:
                Save(Tab, true);
                return true;
            case Keys.Control | Keys.W:
                CloseTab(active);
                return true;
            case Keys.Control | Keys.F:
                ShowFind();
                return true;
            case Keys.Control | Keys.G:
                GoTo();
                return true;
            case Keys.Control | Keys.E:
                SelectBlock();
                return true;
            case Keys.F3:
                Find(false);
                return true;
            case Keys.Shift | Keys.F3:
                Find(true);
                return true;
            case Keys.Control | Keys.Tab:
                Cycle(1);
                return true;
            case Keys.Control | Keys.Shift | Keys.Tab:
                Cycle(-1);
                return true;
            case Keys.Escape:
                if (findBar.Visible)
                {
                    HideFind();
                    return true;
                }
                break;
            }
            if (!query.Focused)
                switch (key)
                {
                case Keys.Control | Keys.Z:
                    grid.History(false);
                    return true;
                case Keys.Control | Keys.Y:
                case Keys.Control | Keys.Shift | Keys.Z:
                    grid.History(true);
                    return true;
                case Keys.Control | Keys.A:
                    grid.SelectRange(0, Tab.Document.Length);
                    return true;
                case Keys.Control | Keys.X:
                    Copy(true);
                    return true;
                case Keys.Control | Keys.C:
                    Copy(false);
                    return true;
                case Keys.Control | Keys.V:
                    Paste();
                    return true;
                }
            return base.ProcessCmdKey(ref msg, key);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                searchTimer.Dispose();
                tips.Dispose();
                wordmark.Dispose();
                heading.Dispose();
                statusFont.Dispose();
                monoStatus.Dispose();
                foreach (var tab in tabs)
                    tab.Document.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
