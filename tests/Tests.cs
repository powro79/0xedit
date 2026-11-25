using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using ComTypes = System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32.SafeHandles;

namespace ZeroXEdit
{
    internal static class Tests
    {
        private static void Check(bool ok, string message)
        {
            if (!ok)
                throw new Exception(message);
        }
        private static void Equal(byte[] expected, BinaryDocument doc)
        {
            Check(doc.Length == expected.Length && doc.ReadBytes(0, expected.Length).SequenceEqual(expected),
                  "Document differs from expected bytes");
        }
        private static void Case(string name, Action test)
        {
            test();
            Console.WriteLine("PASS  " + name);
        }
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--ui")
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Theme.Initialize();
                    Case("Desktop startup, keyboard editing, search, tab switching, and inspector",
                         () => Desktop(args));
                    return 0;
                }
                Case("Randomized edit, undo, redo, and branching history", RandomEdits);
                Case("File-backed edits, saves, saved revisions, and failed writes", SaveAndReopen);
                Case("64-bit offsets in a sparse file larger than 4 GiB", LargeFile);
                Case("Forward, reverse, overlapping, chunk-boundary, and wrapped searches", Searches);
                Case("Replace All across read boundaries, grouped undo, and interrupted scans", BulkReplacements);
                Case("Hex parsing, offsets, and endian-sensitive data interpretation", Conversions);
                Case("Native clipboard payload, alignment padding, and binary round trips", ClipboardPayload);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }
        private static void RandomEdits()
        {
            var random = new Random(3719);
            var history = new List<byte[]> { new byte[50] };
            int rev = 0;
            using (var doc = BinaryDocument.Create("test", history[0], false))
            {
                for (int step = 0; step < 4500; step++)
                {
                    int op = random.Next(5);
                    if (op == 0 && doc.CanUndo)
                    {
                        doc.Undo();
                        rev--;
                    }
                    else if (op == 1 && doc.CanRedo)
                    {
                        doc.Redo();
                        rev++;
                    }
                    else
                    {
                        var expected = history[rev].ToList();
                        int at = random.Next(expected.Count + 1);
                        int remove = random.Next(Math.Min(10, expected.Count - at) + 1);
                        var add = new byte[random.Next(13)];
                        random.NextBytes(add);
                        bool changed = doc.Replace(at, remove, add);
                        expected.RemoveRange(at, remove);
                        expected.InsertRange(at, add);
                        if (changed)
                        {
                            history.RemoveRange(rev + 1, history.Count - rev - 1);
                            history.Add(expected.ToArray());
                            rev++;
                        }
                    }
                    Equal(history[rev], doc);
                    Check(doc.CanUndo == (rev != 0) && doc.CanRedo == (rev + 1 < history.Count), "History flags");
                }
            }
        }
        private static void SaveAndReopen()
        {
            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "original.bin"), copy = Path.Combine(dir, "copy.bin");
            var original = new byte[1048601];
            new Random(33).NextBytes(original);
            try
            {
                File.WriteAllBytes(path, original);
                using (var doc = BinaryDocument.Open(path))
                {
                    doc.Replace(500, 900000, new byte[] { 0x55, 0xFF });
                    Check(doc.Dirty && File.ReadAllBytes(path).SequenceEqual(original), "Edit touched original");
                    doc.WriteRange(copy, 499, 4);
                    Check(File.ReadAllBytes(copy).SequenceEqual(
                              new byte[] { original[499], 0x55, 0xFF, original[900500] }) &&
                              doc.FilePath == path && doc.Dirty,
                          "Selection export changed document or wrote the wrong range");
                    var changed =
                        original.Take(500).Concat(new byte[] { 0x55, 0xFF }).Concat(original.Skip(900500)).ToArray();
                    doc.Save(path);
                    Check(!doc.Dirty && !doc.ChangedOnDisk(), "Saved revision");
                    using (var disk = BinaryDocument.Open(path)) Equal(changed, disk);
                    doc.Undo();
                    Equal(original, doc);
                    Check(doc.Dirty, "Undo across save");
                    doc.Redo();
                    Check(!doc.Dirty, "Redo to saved revision");
                    doc.Replace(0, 0, new byte[] { 1 });
                    doc.Undo();
                    doc.Undo();
                    doc.Replace(0, 1, new byte[] { 19 });
                    Check(doc.Dirty && !doc.CanRedo, "Branch after undo");
                    doc.Save(copy);
                    Check(!doc.Dirty && File.ReadAllBytes(copy)[0] == 19, "Save as");
                    doc.Replace(doc.Length, 0, new byte[] { 42 });
                    var before = File.ReadAllBytes(copy);
                    bool failed = false;
                    using (var locked = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        try
                        {
                            doc.Save(copy);
                        }
                        catch (IOException)
                        {
                            failed = true;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            failed = true;
                        }
                    }
                    Check(failed && doc.Dirty && before.SequenceEqual(File.ReadAllBytes(copy)),
                          "Failed save damaged original");
                    Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "Leaked temporary file");
                    File.AppendAllText(copy, "test");
                    Check(doc.ChangedOnDisk(), "External change");
                }
            }
            finally
            {
                File.Delete(path);
                File.Delete(copy);
                Directory.Delete(dir);
            }
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle file, uint code, IntPtr input, int inputSize,
                                                   IntPtr output, int outputSize, out int bytes, IntPtr overlapped);
        private static void LargeFile()
        {
            string path = Path.GetTempFileName();
            long at = (1L << 32) + 2;
            try
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Write))
                {
                    int count;
                    if (!DeviceIoControl(file.SafeFileHandle, 0x900C4, IntPtr.Zero, 0, IntPtr.Zero, 0, out count,
                                         IntPtr.Zero))
                        throw new IOException("Large-file test requires a sparse-file capable temporary directory.");
                    file.Position = at;
                    file.WriteByte(222);
                    file.WriteByte(173);
                }
                using (var doc = BinaryDocument.Open(path))
                {
                    Check(doc.Length == at + 2 && doc.ReadBytes(at, 2).SequenceEqual(new byte[] { 222, 173 }),
                          "64-bit read");
                    doc.Replace(at, 2, new byte[] { 15 });
                    Check(doc.ReadBytes(at, 1)[0] == 15, "64-bit edit");
                    doc.Undo();
                    Check(doc.ReadBytes(at, 1)[0] == 222, "64-bit undo");
                    var search = new PatternSearch(doc, new byte[] { 222, 173 }, at - 4, false);
                    Check(search.Step(16) && search.Result == at, "64-bit search");
                    doc.Replace(3, at - 3, new byte[0]);
                    Equal(new byte[] { 0, 0, 0, 222, 173 }, doc);
                    doc.Undo();
                    Check(doc.Length == at + 2, "Large deletion could not be undone");
                }
            }
            finally
            {
                File.Delete(path);
            }
        }
        private static long Search(BinaryDocument doc, byte[] value, long pos, bool reverse, int budget)
        {
            var search = new PatternSearch(doc, value, pos, reverse);
            while (!search.Step(budget))
            {
            }
            return search.Result;
        }
        private static void Searches()
        {
            byte[] data = Encoding.ASCII.GetBytes("aaabaaaaab");
            using (var doc = BinaryDocument.Create("test", data, false))
            {
                foreach (var term in new[] { "a", "aa", "aaa", "aaab", "aaaa", "ba", "x", "aaabaaaaab", "aaabaaaaaba" })
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(term);
                    var hits = Enumerable.Range(0, data.Length)
                                   .Where(i => i + bytes.Length <= data.Length &&
                                               data.Skip(i).Take(bytes.Length).SequenceEqual(bytes))
                                   .ToList();
                    for (int pos = -1; pos <= data.Length + 1; pos++)
                    {
                        int next = hits.FindIndex(x => x >= pos), prev = hits.FindLastIndex(x => x <= pos);
                        long f = hits.Count == 0 ? -1 : hits[next < 0 ? 0 : next];
                        long b = hits.Count == 0 ? -1 : hits[prev < 0 ? hits.Count - 1 : prev];
                        Check(Search(doc, bytes, pos, false, 2) == f, "Forward " + term + " " + pos);
                        Check(Search(doc, bytes, pos, true, 2) == b, "Backward " + term + " " + pos);
                    }
                }
            }
            var block = new byte[140000];
            byte[] marker = { 3, 7, 11, 13 };
            Array.Copy(marker, 0, block, 65534, marker.Length);
            using (var doc = BinaryDocument.Create("test", block, false))
            {
                Check(Search(doc, marker, 0, false, 90000) == 65534, "Crossed buffer boundary");
                Check(Search(doc, marker, doc.Length, true, 7) == 65534, "Crossed reverse budget");
            }
        }
        private static void BulkReplacements()
        {
            var source = new byte[150000];
            byte[] pattern = { 0xAB, 0xCD, 0xEF };
            Array.Copy(pattern, 0, source, 65535, pattern.Length);
            Array.Copy(pattern, 0, source, 149997, pattern.Length);
            using (var doc = BinaryDocument.Create("test", source, false))
            {
                doc.Replace(65536, 1, new byte[] { 0xCC });
                pattern[1] = 0xCC;
                doc.Replace(149998, 1, new byte[] { 0xCC });
                var before = doc.ReadBytes(0, source.Length);
                var deletion = new BinaryDocument.ReplaceAllOperation(doc, pattern, new byte[0]);
                Check(!deletion.Step(65536) && !deletion.Step(4) && deletion.Count == 1, "Staged boundary match");
                Equal(before, doc);
                while (!deletion.Step(16384)) { }
                Check(deletion.Count == 2 && deletion.FirstOffset == 65535, "Both edited patterns found");
                Equal(new byte[source.Length - 6], doc);
                doc.Undo();
                Equal(before, doc);
                var miss = new BinaryDocument.ReplaceAllOperation(doc, new byte[] { 0x77 }, new byte[] { 1 });
                Check(miss.Step(source.Length + 1) && miss.Count == 0 && doc.CanRedo, "No matches preserve redo");
                doc.Redo();
                Equal(new byte[source.Length - 6], doc);

                var stale = new BinaryDocument.ReplaceAllOperation(doc, new byte[] { 0 }, new byte[] { 1 });
                Check(!stale.Step(2048), "Scan still running");
                doc.Replace(0, 1, new byte[] { 2 });
                bool rejected = false;
                try { stale.Step(2048); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected && doc.ReadBytes(0, 2).SequenceEqual(new byte[] { 2, 0 }), "Stale scan discarded");
            }
            using (var dense = BinaryDocument.Create("zeros", new byte[150001], false))
            {
                byte[] patternPair = { 0, 0 }, longer = { 0, 0, 0xA5 };
                var growth = new BinaryDocument.ReplaceAllOperation(dense, patternPair, longer);
                while (!growth.Step(32767)) { }
                var expected = new byte[225001];
                for (int i = 2; i < expected.Length; i += 3) expected[i] = 0xA5;
                Equal(expected, dense);
                Check(growth.Count == 75000 && dense.Dirty, "Non-overlapping matches in original input");
                dense.Undo();
                Check(!dense.Dirty && !dense.CanUndo, "One edit restores the saved revision");
                Equal(new byte[150001], dense);
                dense.Redo();
                Equal(expected, dense);
            }
            string path = Path.GetTempFileName();
            try
            {
                byte[] original = Encoding.ASCII.GetBytes("before foo / foo after");
                File.WriteAllBytes(path, original);
                using (var document = BinaryDocument.Open(path))
                {
                    var operation = new BinaryDocument.ReplaceAllOperation(document, Encoding.ASCII.GetBytes("foo"),
                                                                           Encoding.ASCII.GetBytes("bar!"));
                    while (!operation.Step(3)) { }
                    Check(File.ReadAllBytes(path).SequenceEqual(original), "Replacement remains in memory until save");
                    document.Save(path);
                    Check(File.ReadAllText(path) == "before bar! / bar! after", "Saved replaced file");
                    document.Undo();
                    Equal(original, document);
                    Check(document.Dirty, "Undo across a save marks the document modified");
                    document.Redo();
                    Check(!document.Dirty, "Redo restores the saved revision");
                }
            }
            finally { File.Delete(path); }
        }

        private static void Conversions()
        {
            byte[] bytes = ByteTools.ParseHex("fF 00\r\n7a");
            Check(ByteTools.Hex(bytes) == "FF 00 7A", "Hex conversion");
            Check(ByteTools.Interpret(bytes, 2, true, 's') == "-256", "Big signed");
            Check(ByteTools.Interpret(bytes, 2, false, 'u') == "255", "Little unsigned");
            Check(ByteTools.Interpret(bytes, 1, false, 's') == "-1", "Signed byte");
            Check(ByteTools.Interpret(new byte[] { 0x3F, 0x80, 0, 0 }, 4, true, 'f') == "1", "Float32");
            Check(ByteTools.Interpret(bytes, 8, true, 'u') == "—", "Incomplete value");
            Check(ByteTools.ParseOffset("0x100000001", false, 1L << 40) == 4294967297L, "Wide offset");
            foreach (var invalid in new[] { "0", "0a gh" })
            {
                bool failed = false;
                try
                {
                    ByteTools.ParseHex(invalid);
                }
                catch (FormatException)
                {
                    failed = true;
                }
                Check(failed, "Invalid hex");
            }
        }
        [DllImport("ole32.dll")]
        private static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM medium);
        private static void ClipboardPayload()
        {
            byte[] binary = { 0, 0x80, 0xFF };
            var data = BinaryClipboard.CreateData(binary, false);
            Check(data.GetText() == "00 80 FF", "Plain text clipboard output");
            var format = new ComTypes.FORMATETC { cfFormat = (short)DataFormats.GetFormat("0xEdit.Bytes.v1").Id,
                                                  dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT, lindex = -1,
                                                  tymed = ComTypes.TYMED.TYMED_HGLOBAL };
            ComTypes.STGMEDIUM medium;
            ((ComTypes.IDataObject)data).GetData(ref format, out medium);
            try
            {
                Check(binary.SequenceEqual(BinaryClipboard.ReadHandle(medium.unionmember)),
                      "Native byte clipboard round trip");
            }
            finally
            {
                ReleaseStgMedium(ref medium);
            }
            var ptr = Marshal.AllocHGlobal(8);
            try
            {
                Marshal.WriteInt32(ptr, 1);
                Marshal.WriteByte(ptr, 4, 0xEF);
                Check(BinaryClipboard.ReadPayload(ptr, 8).SequenceEqual(new byte[] { 0xEF }), "Clipboard padding");
                Marshal.WriteInt32(ptr, 5);
                bool invalid = false;
                try
                {
                    BinaryClipboard.ReadPayload(ptr, 8);
                }
                catch (FormatException)
                {
                    invalid = true;
                }
                Check(invalid, "Malformed clipboard length");
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        // Drive the real controls without sending keystrokes to the user's desktop.
        private static object Invoke(object owner, string name, params object[] args)
        {
            return owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(owner, args);
        }
        private static T Field<T>(object owner, string name)
        {
            return (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);
        }
        private static void Key(MainForm form, Keys keys)
        {
            Invoke(form, "ProcessCmdKey", new Message(), keys);
        }
        private static void EncodedTextSearches(MainForm form)
        {
            Key(form, Keys.Control | Keys.N);
            Key(form, Keys.Control | Keys.F);
            var tab = Field<List<EditorTab>>(form, "tabs").Last();
            var grid = Field<HexView>(form, "grid");
            var format = Field<ComboBox>(form, "findType");
            var query = Field<TextBox>(form, "query");
            var message = Field<Label>(form, "findMessage");

            const string unicode = "é\U0001F600";
            var cases = new[] {
                new { Name = "ASCII", Text = "Ax", Bytes = "41 78" },
                new { Name = "Windows-1252", Text = "€é", Bytes = "80 E9" },
                new { Name = "UTF-8", Text = unicode, Bytes = "C3 A9 F0 9F 98 80" },
                new { Name = "UTF-16 LE", Text = unicode, Bytes = "E9 00 3D D8 00 DE" },
                new { Name = "UTF-16 BE", Text = unicode, Bytes = "00 E9 D8 3D DE 00" },
                new { Name = "UTF-32 LE", Text = unicode, Bytes = "E9 00 00 00 00 F6 01 00" },
                new { Name = "UTF-32 BE", Text = unicode, Bytes = "00 00 00 E9 00 01 F6 00" }
            };
            foreach (var sample in cases)
            {
                // Prefix and separate the two matches so they can start at unaligned offsets.
                byte[] bytes = ByteTools.ParseHex(sample.Bytes);
                grid.SelectRange(0, tab.Document.Length);
                grid.Put(ByteTools.ParseHex("FF " + sample.Bytes + " FF " + sample.Bytes));
                grid.Navigate(0, false);
                format.SelectedIndex = format.FindStringExact(sample.Name);
                Check(format.Text == sample.Name, sample.Name + " is selectable");
                query.Text = sample.Text;
                Key(form, Keys.F3);
                Check(tab.Start == 1 && tab.Selected == bytes.Length, sample.Name + " forward match");
                Key(form, Keys.Shift | Keys.F3);
                Check(tab.Start == bytes.Length + 2 && tab.Selected == bytes.Length, sample.Name + " reverse wrap");
                Key(form, Keys.F3);
                Check(tab.Start == 1 && tab.Selected == bytes.Length, sample.Name + " forward wrap");
                Key(form, Keys.Control | Keys.H);
                var replacement = Field<TextBox>(form, "replacement");
                replacement.Text = sample.Text + sample.Text;
                replacement.Parent.Controls.OfType<Button>().Single(b => b.Text == "Replace all").PerformClick();
                string doubled = sample.Bytes + " " + sample.Bytes;
                Equal(ByteTools.ParseHex("FF " + doubled + " FF " + doubled), tab.Document);
            }

            grid.SelectRange(0, tab.Document.Length);
            grid.Put(new byte[] { 0x3F, 0x3F });
            grid.Navigate(0, false);
            foreach (string name in new[] { "ASCII", "Windows-1252" })
            {
                format.SelectedIndex = format.FindStringExact(name);
                query.Text = name == "ASCII" ? "é" : "\U0001F600";
                Key(form, Keys.F3);
                Check(tab.Selected == 0 && message.Text.Contains(name + " cannot encode"),
                      name + " reports unrepresentable characters");
            }
        }
        private static void Desktop(string[] args)
        {
            string path = Assembly.GetExecutingAssembly().Location;
            using (var fromPath = new MainForm(new[] { path }))
                Check(Field<List<EditorTab>>(fromPath, "tabs").Single().Document.FilePath == path, "Startup file path");

            using (var form = new MainForm(new string[0]))
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-10000, -10000);
                form.Size = new Size(Theme.D(1240), Theme.D(832));
                form.Show();
                Application.DoEvents();
                var grid = Field<HexView>(form, "grid");
                var tabs = Field<List<EditorTab>>(form, "tabs");
                var commands = Field<Dictionary<string, ToolStripMenuItem>>(form, "commands");
                var toolbar = Field<Panel>(form, "toolbar").Controls.OfType<ActionButton>().ToArray();
                Check(tabs.Count == 0 && form.Text == "0xEdit" && !grid.Visible, "Empty startup workspace");
                Check(commands["open"].Enabled && !commands["save"].Enabled && !commands["find"].Enabled &&
                      toolbar.Where(button => button.Enabled).Select(button => button.GlyphName)
                             .OrderBy(name => name).SequenceEqual(new[] { "new", "open" }), "Empty workspace commands");
                Key(form, Keys.Control | Keys.N);
                Check(tabs.Count == 1 && tabs[0].Document.Length == 0 && grid.Visible && commands["save"].Enabled,
                      "New file from empty workspace");
                commands["example"].PerformClick();
                Invoke(form, "CloseTab", 0);
                var tab = tabs[0];
                Check(tab.Document.IsExample &&
                          tab.Document.ReadBytes(0, 4).SequenceEqual(new byte[] { 137, 80, 78, 71 }),
                      "Explicitly opened example");
                if (args.Length > 1)
                {
                    using (var shot = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                    {
                        // Capture each client surface at its real location, excluding window chrome.
                        using (var g = Graphics.FromImage(shot))
                        {
                            foreach (Control control in form.Controls)
                                using (var bitmap = new Bitmap(control.Width, control.Height))
                                {
                                    control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                                    g.DrawImageUnscaled(bitmap, control.Location);
                                }
                        }
                        shot.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                Key(form, Keys.Control | Keys.F);
                Field<TextBox>(form, "query").Text = "50 4E 47";
                Key(form, Keys.F3);
                Check(tab.Start == 1 && tab.Selected == 3, "Search from UI");
                using (var dialog = new HashDialog(tab))
                {
                    dialog.ShowInTaskbar = false;
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = form.Location;
                    dialog.Show(form);
                    var digest = dialog.Controls.OfType<TextBox>().Single();
                    var limit = System.Diagnostics.Stopwatch.StartNew();
                    while (digest.Text.Length != 64 && limit.ElapsedMilliseconds < 2000)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(5);
                    }
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                        Check(digest.Text == BitConverter.ToString(sha.ComputeHash(Encoding.ASCII.GetBytes("PNG")))
                                                 .Replace("-", "")
                                                 .ToLowerInvariant(),
                              "Selection SHA-256");
                }
                using (var dialog = new OffsetDialog(1, tab.Document.Length, 3))
                {
                    ((Button)dialog.AcceptButton).CreateControl();
                    dialog.ShowInTaskbar = false;
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = form.Location;
                    dialog.Show(form);
                    ((Button)dialog.AcceptButton).PerformClick();
                    Check(dialog.Offset == 1 && dialog.Count == 3 && dialog.DialogResult == DialogResult.OK,
                          "Block dialog");
                }
                Key(form, Keys.Escape);
                Key(form, Keys.Control | Keys.N);
                var fresh = tabs[1];
                Invoke(grid, "OnKeyPress", new KeyPressEventArgs('A'));
                Invoke(grid, "OnKeyPress", new KeyPressEventArgs('B'));
                Equal(new byte[] { 0xAB }, fresh.Document);
                Key(form, Keys.Control | Keys.Z);
                Equal(new byte[0], fresh.Document);
                Key(form, Keys.Control | Keys.Y);
                Equal(new byte[] { 0xAB }, fresh.Document);
                Invoke(grid, "OnKeyDown", new KeyEventArgs(Keys.Back));
                Equal(new byte[0], fresh.Document);
                Invoke(grid, "OnKeyDown", new KeyEventArgs(Keys.Tab));
                Invoke(grid, "OnKeyPress", new KeyPressEventArgs('a'));
                Invoke(grid, "OnKeyPress", new KeyPressEventArgs('b'));
                Invoke(grid, "OnKeyDown", new KeyEventArgs(Keys.Home));
                Invoke(grid, "OnKeyDown", new KeyEventArgs(Keys.Insert));
                Invoke(grid, "OnKeyPress", new KeyPressEventArgs('c'));
                Equal(Encoding.ASCII.GetBytes("cab"), fresh.Document);
                Key(form, Keys.Control | Keys.Tab);
                Check(form.Text.Contains("aurora.png"), "Tab switch");
                Field<ComboBox>(form, "columns").SelectedIndex = 2;
                Check(tab.Columns == 32, "Row width");
                form.Size = new Size(Theme.D(790), Theme.D(540));
                Application.DoEvents();
                Check(!Field<Inspector>(form, "inspector").Visible && grid.Width > 0, "Compact layout");
                Case("All seven search encodings, byte order, wraparound, and unrepresentable text",
                     () => EncodedTextSearches(form));
                Case("Replace controls, selection, encodings, deletion, validation, and cancellation",
                     () => ReplaceFromBar(form));
            }
        }
        private static void ReplaceFromBar(MainForm form)
        {
            Key(form, Keys.Control | Keys.N);
            var tab = Field<List<EditorTab>>(form, "tabs").Last();
            var grid = Field<HexView>(form, "grid");
            grid.Put(ByteTools.ParseHex("80 3F 80"));
            grid.Navigate(2, false);
            Key(form, Keys.Control | Keys.H);
            var query = Field<TextBox>(form, "query");
            var replacement = Field<TextBox>(form, "replacement");
            var format = Field<ComboBox>(form, "findType");
            var message = Field<Label>(form, "findMessage");
            var buttons = replacement.Parent.Controls.OfType<Button>();
            var one = buttons.Single(b => b.Text == "Replace");
            var all = buttons.Single(b => b.Text == "Replace all");
            Check(replacement.Visible && one.Visible && all.Visible, "Ctrl+H opens replace controls");

            format.SelectedIndex = format.FindStringExact("Windows-1252");
            query.Text = "€";
            replacement.Text = "éé";
            Key(form, Keys.F3);
            Check(tab.Start == 2 && tab.Selected == 1, "Selected match before Replace");
            one.PerformClick();
            Equal(ByteTools.ParseHex("80 3F E9 E9"), tab.Document);
            Check(tab.Caret == 4 && tab.Selected == 0, "Caret follows replacement");
            one.PerformClick();
            Equal(ByteTools.ParseHex("E9 E9 3F E9 E9"), tab.Document);
            grid.Focus();
            Key(form, Keys.Control | Keys.Z);
            Equal(ByteTools.ParseHex("80 3F E9 E9"), tab.Document);
            replacement.Text = "\U0001F600";
            all.PerformClick();
            Check(message.Text.Contains("replacement") && message.Text.Contains("cannot encode") && tab.Document.CanRedo,
                  "Invalid encoded replacement preserves history");

            format.SelectedIndex = format.FindStringExact("Hex bytes");
            query.Text = "E9";
            replacement.Text = "F";
            all.PerformClick();
            Check(message.Text.Contains("Invalid replacement") && tab.Document.CanRedo, "Invalid hex replacement");
            replacement.Text = "e9";
            all.PerformClick();
            Check(tab.Document.CanRedo && message.Text.Contains("identical"), "Identical bytes preserve history");
            replacement.Clear();
            all.PerformClick();
            Equal(ByteTools.ParseHex("80 3F"), tab.Document);
            Check(message.Text.Contains("2 matches"), "Replacement count");
            Key(form, Keys.Control | Keys.Z);
            Equal(ByteTools.ParseHex("80 3F E9 E9"), tab.Document);
            query.Clear();
            all.PerformClick();
            Check(tab.Document.CanRedo && message.Text == "Enter a search value.", "Empty search preserves history");

            grid.SelectRange(0, tab.Document.Length);
            grid.Put(new byte[300000]);
            query.Text = "00"; replacement.Text = "FF";
            all.PerformClick();
            Check(message.Text.Contains("Esc"), "Long scan pending");
            var timer = Field<Timer>(form, "searchTimer");
            using (var modal = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = form.Location })
            {
                bool paused = false;
                modal.Shown += delegate { paused = !timer.Enabled; modal.Close(); };
                modal.ShowDialog(form);
                Check(paused && timer.Enabled, "Pending replacement pauses while a modal dialog is open");
            }
            Key(form, Keys.Escape);
            Invoke(form, "SearchTick");
            Equal(new byte[300000], tab.Document);
            Check(!replacement.Visible, "Escape cancels the scan");

            Key(form, Keys.Control | Keys.H);
            all.PerformClick();
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (timer.Enabled && timeout.ElapsedMilliseconds < 3000)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(5);
            }
            Check(!timer.Enabled && tab.Document.ReadBytes(0, 300000).All(b => b == 0xFF),
                  "Replace All finishes on a later timer tick");
            grid.Focus();
            Key(form, Keys.Control | Keys.Z);
            Equal(new byte[300000], tab.Document);
        }
    }
}
