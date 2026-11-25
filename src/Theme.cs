using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZeroXEdit
{
    internal static class Theme
    {
        internal static readonly Color Ink = Color.FromArgb(28, 39, 57), Muted = Color.FromArgb(86, 102, 124),
                                       Faint = Color.FromArgb(122, 136, 154), Line = Color.FromArgb(218, 225, 233),
                                       Paper = Color.White, Surface = Color.FromArgb(246, 248, 251),
                                       Tabs = Color.FromArgb(237, 241, 246), Blue = Color.FromArgb(43, 83, 211),
                                       BlueLight = Color.FromArgb(229, 237, 255), Teal = Color.FromArgb(25, 112, 111),
                                       Navy = Color.FromArgb(31, 46, 70);
        private static float scale = 1;
        private static string uiFamily = "Segoe UI", monoFamily = "Consolas";
        internal static void Initialize()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            uiFamily = AvailableFamily("Inter", uiFamily);
            monoFamily = AvailableFamily("IBM Plex Mono", monoFamily);
        }
        private static string AvailableFamily(string preferred, string fallback)
        {
            using (var probe = new Font(preferred, 9))
                return string.Equals(probe.Name, preferred, StringComparison.OrdinalIgnoreCase) ? preferred : fallback;
        }
        internal static int D(float value)
        {
            return (int)Math.Round(value * scale);
        }
        internal static Font Ui(float size, bool bold = false)
        {
            return new Font(uiFamily, size, bold ? FontStyle.Bold : FontStyle.Regular);
        }
        internal static Font Mono(float size, bool bold = false)
        {
            return new Font(monoFamily, size, bold ? FontStyle.Bold : FontStyle.Regular);
        }
        internal static Rectangle Box(int x, int y, int w, int h)
        {
            return new Rectangle(D(x), D(y), D(w), D(h));
        }
        internal static void Text(Graphics g, string text, Font font, Color color, Rectangle rect,
                                  TextFormatFlags align = TextFormatFlags.Left)
        {
            TextRenderer.DrawText(g, text, font, rect, color,
                                  align | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                                      TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                                      TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);
        }
        internal static void Fill(Graphics g, Color color, Rectangle r)
        {
            using (var brush = new SolidBrush(color)) g.FillRectangle(brush, r);
        }
        internal static void Rule(Graphics g, Color color, int x1, int y1, int x2, int y2)
        {
            using (var pen = new Pen(color)) g.DrawLine(pen, x1, y1, x2, y2);
        }
        internal static void Round(Graphics g, Color color, Rectangle rect, int radius)
        {
            int d = Math.Min(Math.Min(rect.Width, rect.Height), 2 * radius);
            if (d < 2)
            {
                Fill(g, color, rect);
                return;
            }
            using (var path = new GraphicsPath()) using (var brush = new SolidBrush(color))
            {
                path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }
        internal static void Glyph(Graphics g, string name, Color color, Rectangle rect)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(rect.X, rect.Y);
            g.ScaleTransform(rect.Width / 24f, rect.Height / 24f);
            using (var p = new Pen(color, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round,
                                                   LineJoin = LineJoin.Round })
            {
                Action<int, int, int, int> line = (x, y, a, b) => g.DrawLine(p, x, y, a, b);
                switch (name)
                {
                case "brand":
                    line(7, 6, 4, 12);
                    line(4, 12, 7, 18);
                    line(17, 6, 20, 12);
                    line(20, 12, 17, 18);
                    line(10, 17, 14, 7);
                    break;
                case "open":
                    line(2, 17, 2, 6);
                    line(2, 6, 9, 6);
                    line(9, 6, 11, 9);
                    line(11, 9, 20, 9);
                    line(20, 9, 20, 11);
                    g.DrawPolygon(p,
                                  new[] { new Point(2, 18), new Point(5, 11), new Point(22, 11), new Point(19, 18) });
                    break;
                case "new":
                case "file":
                    g.DrawPolygon(p, new[] { new Point(5, 3), new Point(14, 3), new Point(19, 8), new Point(19, 21),
                                             new Point(5, 21) });
                    line(14, 3, 14, 8);
                    line(14, 8, 19, 8);
                    if (name == "new")
                    {
                        line(9, 14, 15, 14);
                        line(12, 11, 12, 17);
                    }
                    else
                    {
                        line(8, 13, 15, 13);
                        line(8, 16, 13, 16);
                    }
                    break;
                case "save":
                    g.DrawPolygon(p, new[] { new Point(4, 4), new Point(16, 4), new Point(20, 8), new Point(20, 20),
                                             new Point(4, 20) });
                    g.DrawRectangle(p, 8, 4, 7, 6);
                    g.DrawRectangle(p, 8, 14, 8, 6);
                    break;
                case "undo":
                case "redo":
                    if (name == "redo")
                    {
                        g.TranslateTransform(24, 0);
                        g.ScaleTransform(-1, 1);
                    }
                    line(4, 10, 9, 5);
                    line(4, 10, 9, 15);
                    line(5, 10, 14, 10);
                    g.DrawBezier(p, 14, 10, 20, 10, 21, 16, 18, 19);
                    break;
                case "find":
                    g.DrawEllipse(p, 3, 3, 12, 12);
                    line(14, 14, 21, 21);
                    break;
                case "goto":
                    line(3, 12, 18, 12);
                    line(13, 7, 18, 12);
                    line(18, 12, 13, 17);
                    line(21, 5, 21, 19);
                    break;
                case "grid":
                    g.DrawRectangle(p, 3, 4, 18, 16);
                    line(3, 10, 21, 10);
                    line(10, 4, 10, 20);
                    break;
                case "panel":
                    g.DrawRectangle(p, 3, 4, 18, 16);
                    line(14, 4, 14, 20);
                    break;
                case "close":
                    line(6, 6, 18, 18);
                    line(18, 6, 6, 18);
                    break;
                case "plus":
                    line(5, 12, 19, 12);
                    line(12, 5, 12, 19);
                    break;
                case "down":
                    line(6, 9, 12, 15);
                    line(12, 15, 18, 9);
                    break;
                case "edit":
                    line(4, 19, 8, 18);
                    line(4, 19, 5, 15);
                    line(5, 15, 16, 4);
                    line(8, 18, 19, 7);
                    line(16, 4, 19, 7);
                    line(14, 6, 17, 9);
                    break;
                }
            }
            g.Restore(state);
        }
    }

    internal sealed class ActionButton : Button
    {
        internal readonly string GlyphName;
        private readonly bool primary;
        private bool hover;
        internal ActionButton(string icon, string label, int width, Action action, bool primary = false)
        {
            GlyphName = icon;
            Text = label;
            this.primary = primary;
            Size = new Size(Theme.D(width), Theme.D(32));
            Font = Theme.Ui(9);
            AccessibleName = label.Length > 0 ? label : icon;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            TabStop = false;
            Click += delegate
            {
                action();
            };
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
        }
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover = true;
            Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = false;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (primary || hover || Focused)
                Theme.Round(g, primary ? (hover ? Color.FromArgb(31, 66, 182) : Theme.Blue) : Theme.Tabs,
                            ClientRectangle, Theme.D(5));
            Color color = !Enabled ? Theme.Faint : primary ? Color.White : Theme.Ink;
            int x = Text.Length == 0 ? (Width - Theme.D(18)) / 2 : Theme.D(9);
            Theme.Glyph(g, GlyphName, color, new Rectangle(x, (Height - Theme.D(18)) / 2, Theme.D(18), Theme.D(18)));
            if (Text.Length > 0)
                Theme.Text(g, Text, Font, color, new Rectangle(x + Theme.D(25), 0, Width - x - Theme.D(26), Height));
        }
    }
}
