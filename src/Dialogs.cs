using System;
using System.Drawing;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace ZeroXEdit
{
    internal sealed class OffsetDialog : Form
    {
        internal long Offset { get; private set; }
        internal long Count { get; private set; }
        internal OffsetDialog(long current, long length, long? selectionLength = null)
        {
            bool block = selectionLength.HasValue;
            int extra = block ? 67 : 0;
            Text = block ? "Select block" : "Go to offset";
            Font = Theme.Ui(9);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.D(370), Theme.D(176 + extra));
            BackColor = Theme.Paper;
            var prompt = new Label { Text = block ? "Start offset" : "File offset", Font = Theme.Ui(10, true),
                                     Bounds = Theme.Box(18, 15, 220, 24) };
            var field = new TextBox { Text = current.ToString("X"), Font = Theme.Mono(11),
                                      Bounds = Theme.Box(18, 46, 185, 28), AccessibleName = "File offset" };
            var radix = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = Theme.Box(215, 46, 136, 28),
                                       AccessibleName = "Number base" };
            radix.Items.AddRange(new object[] { "Hexadecimal", "Decimal" });
            radix.SelectedIndex = 0;
            var size = new TextBox { Bounds = Theme.Box(18, 113, 185, 28), Font = Theme.Mono(11),
                                     AccessibleName = "Block length in bytes",
                                     Text = selectionLength.GetValueOrDefault().ToString("X") };
            if (block)
                Controls.AddRange(
                    new Control[] { new Label { Text = "Length in bytes", Bounds = Theme.Box(18, 82, 220, 26),
                                                Font = Theme.Ui(10, true) },
                                    size });
            var message = new Label { ForeColor = Theme.Muted, Bounds = Theme.Box(18, 82 + extra, 335, 34),
                                      Text = "End of file   0x" + length.ToString("X") };
            var go = new Button { Text = block ? "Select" : "Go to", Bounds = Theme.Box(267, 128 + extra, 84, 30) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel,
                                      Bounds = Theme.Box(177, 128 + extra, 84, 30) };
            radix.SelectedIndexChanged += delegate
            {
                try
                {
                    long n = ByteTools.ParseOffset(field.Text, radix.SelectedIndex != 0, length);
                    field.Text = n.ToString(radix.SelectedIndex == 0 ? "X" : "D");
                }
                catch (FormatException)
                {
                }
                if (block)
                    try
                    {
                        long n = ByteTools.ParseOffset(size.Text, radix.SelectedIndex != 0, length);
                        size.Text = n.ToString(radix.SelectedIndex == 0 ? "X" : "D");
                    }
                    catch (FormatException)
                    {
                    }
            };
            go.Click += delegate
            {
                try
                {
                    Offset = ByteTools.ParseOffset(field.Text, radix.SelectedIndex == 0, length);
                    if (block)
                    {
                        Count = ByteTools.ParseOffset(size.Text, radix.SelectedIndex == 0, length);
                        if (Count == 0 || Count > length - Offset)
                            throw new FormatException(
                                "The block must contain at least one byte and fit within the file.");
                    }
                    DialogResult = DialogResult.OK;
                }
                catch (FormatException ex)
                {
                    message.ForeColor = Color.Firebrick;
                    message.Text = ex.Message;
                    field.Focus();
                    field.SelectAll();
                }
            };
            Controls.AddRange(new Control[] { prompt, field, radix, message, go, cancel });
            AcceptButton = go;
            CancelButton = cancel;
            Shown += delegate
            {
                field.Focus();
                field.SelectAll();
            };
            if (!block)
                size.Dispose();
        }
    }

    internal sealed class HashDialog : Form
    {
        private readonly Timer timer = new Timer();
        private readonly SHA256 hash = SHA256.Create();
        private readonly byte[] buffer = new byte[256 * 1024];
        internal HashDialog(EditorTab tab)
        {
            Text = "SHA-256";
            Font = Theme.Ui(9);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.D(548), Theme.D(178));
            BackColor = Theme.Paper;
            long offset = tab.Selected == 0 ? 0 : tab.Start,
                 count = tab.Selected == 0 ? tab.Document.Length : tab.Selected, processed = 0;
            var title = new Label { Text = tab.Document.Name, Font = Theme.Ui(11, true),
                                    Bounds = Theme.Box(18, 12, 510, 25), AutoEllipsis = true };
            var range = new Label { Text = (tab.Selected == 0 ? "Entire file" : "Selection at 0x" + tab.OffsetText) +
                                           "   ·   " + count.ToString("N0") + " bytes",
                                    ForeColor = Theme.Muted, Bounds = Theme.Box(18, 45, 510, 24) };
            var digest = new TextBox { ReadOnly = true, Font = Theme.Mono(9), Bounds = Theme.Box(18, 80, 511, 28),
                                       AccessibleName = "SHA-256 digest" };
            var close =
                new Button { Text = "Close", DialogResult = DialogResult.Cancel, Bounds = Theme.Box(444, 127, 85, 30) };
            CancelButton = close;
            Controls.AddRange(new Control[] { title, range, digest, close });
            timer.Interval = 15;
            timer.Tick += delegate
            {
                try
                {
                    for (int i = 0; i < 8 && processed < count; i++)
                    {
                        int n = tab.Document.Read(offset + processed, buffer, 0,
                                                  (int)Math.Min(count - processed, buffer.Length));
                        hash.TransformBlock(buffer, 0, n, buffer, 0);
                        processed += n;
                    }
                    if (processed < count)
                    {
                        digest.Text = "Calculating…  " + ((int)(100.0 * processed / count)) + "%";
                        return;
                    }
                    timer.Stop();
                    hash.TransformFinalBlock(new byte[0], 0, 0);
                    digest.Text = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                    digest.Focus();
                    digest.SelectAll();
                }
                catch (Exception ex)
                {
                    timer.Stop();
                    digest.Text = ex.Message;
                }
            };
            Shown += delegate
            {
                timer.Start();
            };
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                hash.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
