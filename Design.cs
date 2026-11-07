// UI direction informed by unslop, Damir Sokolovsky (@heydamir), CC BY 4.0.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace BrightnessUtility
{
    internal sealed class Palette
    {
        internal readonly Color Background, Surface, Raised, Border, Text, Secondary, Muted, Accent, Rail;
        internal Palette(bool dark)
        {
            Background = Hex(dark ? 0x161717 : 0xF7F7F5);
            Surface = Hex(dark ? 0x202121 : 0xFFFFFF);
            Raised = Hex(dark ? 0x2A2B2A : 0xEFEFEB);
            Border = Hex(dark ? 0x353633 : 0xDEDFD9);
            Text = Hex(dark ? 0xEEEFEA : 0x232520);
            Secondary = Hex(dark ? 0xB1B4AC : 0x555B51);
            Muted = Hex(dark ? 0x94998F : 0x6C7365);
            Accent = Hex(dark ? 0xE4B56B : 0x8F5D14);
            Rail = Hex(dark ? 0x454840 : 0xD7DAD2);
        }
        private static Color Hex(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }
    }

    internal static class Art
    {
        internal static GraphicsPath Round(RectangleF rect, float radius)
        {
            float d = radius * 2; var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        internal static Icon LoadIcon(int size)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Brightness.ico"))
            {
                if (stream == null) return (Icon)SystemIcons.Application.Clone();
                using (var icon = new Icon(stream, size, size)) return (Icon)icon.Clone();
            }
        }
        internal static void Mark(Graphics g, RectangleF rect, Color color)
        {
            var state = g.Save(); g.TranslateTransform(rect.X, rect.Y); g.ScaleTransform(rect.Width / 32, rect.Height / 32);
            using (var p = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                using (var path = Round(new RectangleF(2, 4, 28, 21), 3)) g.DrawPath(p, path);
                g.DrawLine(p, 16, 25, 16, 29); g.DrawLine(p, 10, 29, 22, 29);
                g.DrawEllipse(p, 12, 10, 8, 8);
                for (int n = 0; n < 8; n++) { double a = n * Math.PI / 4; g.DrawLine(p, 16 + (float)Math.Cos(a) * 7, 14 + (float)Math.Sin(a) * 7, 16 + (float)Math.Cos(a) * 8.5f, 14 + (float)Math.Sin(a) * 8.5f); }
            }
            g.Restore(state);
        }
        // Small line glyphs follow Lucide geometry (ISC license, see THIRD-PARTY-NOTICES.txt).
        internal static void Glyph(Graphics g, string name, RectangleF rect, Color color)
        {
            var state = g.Save(); g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(rect.X, rect.Y); g.ScaleTransform(rect.Width / 24, rect.Height / 24);
            using (var p = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (name == "sun" || name == "dim")
                {
                    g.DrawEllipse(p, 8, 8, 8, 8);
                    if (name == "sun") for (int n = 0; n < 8; n++) { double a = n * Math.PI / 4; g.DrawLine(p, 12 + (float)Math.Cos(a) * 8, 12 + (float)Math.Sin(a) * 8, 12 + (float)Math.Cos(a) * 10, 12 + (float)Math.Sin(a) * 10); }
                }
                else if (name == "moon") { using (var path = new GraphicsPath()) { path.AddBezier(20, 13, 17, 15, 9, 15, 9, 4); path.AddBezier(9, 4, -2, 8, 5, 25, 16, 20); path.AddBezier(16, 20, 19, 19, 20, 16, 20, 13); g.DrawPath(p, path); } }
                else if (name == "dusk") { g.DrawArc(p, 6, 8, 12, 12, 180, 180); g.DrawLine(p, 3, 15, 21, 15); g.DrawLine(p, 5, 20, 19, 20); g.DrawLine(p, 12, 2, 12, 5); g.DrawLine(p, 3, 7, 5, 9); g.DrawLine(p, 21, 7, 19, 9); }
                else if (name == "refresh") { g.DrawArc(p, 4, 4, 16, 16, 45, 280); g.DrawLines(p, new PointF[] { new PointF(21, 3), new PointF(21, 9), new PointF(15, 9) }); }
                else if (name == "save") { g.DrawLine(p, 12, 3, 12, 15); g.DrawLines(p, new PointF[] { new PointF(7, 10), new PointF(12, 15), new PointF(17, 10) }); g.DrawLines(p, new PointF[] { new PointF(4, 16), new PointF(4, 21), new PointF(20, 21), new PointF(20, 16) }); }
                else if (name == "edit") { g.DrawPolygon(p, new PointF[] { new PointF(4, 16), new PointF(16, 4), new PointF(20, 8), new PointF(8, 20), new PointF(3, 21) }); g.DrawLine(p, 13, 7, 17, 11); }
                else if (name == "monitor") { using (var path = Round(new RectangleF(2, 3, 20, 14), 2)) g.DrawPath(p, path); g.DrawLine(p, 12, 17, 12, 21); g.DrawLine(p, 8, 21, 16, 21); }
                else if (name == "x") { g.DrawLine(p, 6, 6, 18, 18); g.DrawLine(p, 18, 6, 6, 18); }
                else if (name == "arrow") { g.DrawLine(p, 4, 12, 20, 12); g.DrawLines(p, new PointF[] { new PointF(14, 6), new PointF(20, 12), new PointF(14, 18) }); }
                else if (name == "settings") { g.DrawLine(p, 3, 6, 21, 6); g.DrawLine(p, 3, 18, 21, 18); g.DrawEllipse(p, 6, 3, 6, 6); g.DrawEllipse(p, 13, 15, 6, 6); }
            }
            g.Restore(state);
        }
    }

    internal class SurfacePanel : Panel
    {
        internal Palette Colors = new Palette(true);
        internal SurfacePanel() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        internal void SetTheme(bool dark) { Colors = new Palette(dark); BackColor = Colors.Background; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Art.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 12 * e.Graphics.DpiX / 96f))
            using (var fill = new SolidBrush(Colors.Surface)) using (var line = new Pen(Colors.Border)) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(line, path); }
            base.OnPaint(e);
        }
    }

    internal sealed class DesignButton : Button
    {
        internal string Glyph;
        internal bool Dark = true, Quiet;
        private bool hover, pressed;
        internal DesignButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand; Font = new Font("Segoe UI", 10); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var c = new Palette(Dark); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = g.DpiX / 96f;
            g.Clear(BackColor);
            using (var path = Art.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8 * s))
            using (var brush = new SolidBrush(pressed || hover ? c.Raised : Quiet ? BackColor : c.Surface))
            using (var pen = new Pen(Focused && ShowFocusCues ? c.Accent : c.Border)) { g.FillPath(brush, path); if (!Quiet || hover || Focused) g.DrawPath(pen, path); }
            Color ink = Enabled ? c.Text : c.Muted;
            int textWidth = TextRenderer.MeasureText(Text, Font).Width;
            int iconSize = (int)(18 * s), gap = (int)(26 * s);
            int group = String.IsNullOrEmpty(Glyph) ? textWidth : Text.Length == 0 ? iconSize : textWidth + gap;
            int left = (Width - group) / 2;
            if (!String.IsNullOrEmpty(Glyph)) Art.Glyph(g, Glyph, new RectangleF(left, (Height - iconSize) / 2, iconSize, iconSize), Enabled ? c.Secondary : c.Muted);
            if (Text.Length > 0) TextRenderer.DrawText(g, Text, Font, new Rectangle(String.IsNullOrEmpty(Glyph) ? 0 : left + gap, 0, String.IsNullOrEmpty(Glyph) ? Width : textWidth, Height), ink, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class DesignCheckBox : CheckBox
    {
        internal bool Dark = true;
        internal DesignCheckBox()
        {
            // ButtonBase's themed background painting can leave unrelated pixels in
            // the shared buffer on partial invalidation. This switch owns every pixel.
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, false);
            BackColor = new Palette(true).Surface;
            AutoSize = false; Cursor = Cursors.Hand;
        }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var c = new Palette(Dark); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            float s = g.DpiX / 96f, y = (Height - 18 * s) / 2f;
            using (var path = Art.Round(new RectangleF(0, y, 32 * s, 18 * s), 9 * s)) using (var fill = new SolidBrush(Checked ? c.Accent : c.Rail)) g.FillPath(fill, path);
            using (var dot = new SolidBrush(Checked ? c.Background : c.Surface)) g.FillEllipse(dot, (Checked ? 17 : 3) * s, y + 3 * s, 12 * s, 12 * s);
            int textStart = (int)(44 * s);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(textStart, 0, Width - textStart, Height), Enabled ? c.Text : c.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(textStart - 4, 2, Width - textStart, Height - 4), c.Accent, BackColor);
        }
    }

    internal sealed class BrandMark : Control
    {
        internal bool Dark = true;
        internal BrandMark() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Art.Mark(e.Graphics, new RectangleF(0, 0, Width, Height), new Palette(Dark).Accent); }
    }

    internal sealed class ShortcutHint : Control
    {
        internal bool Dark = true;
        internal ShortcutHint() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); AccessibleName = "Ctrl + Alt + PgUp или PgDn, либо колёсико: яркость всех мониторов с шагом 5 процентов"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var c = new Palette(Dark); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            string[] keys = { "Ctrl", "Alt", "PgUp / PgDn" }; int x = 0;
            using (var font = new Font("Segoe UI", 8.5f))
            foreach (string key in keys)
            {
                int width = TextRenderer.MeasureText(key, font).Width + 12;
                using (var path = Art.Round(new RectangleF(x, 2, width, 24), 4)) using (var fill = new SolidBrush(c.Surface)) using (var pen = new Pen(c.Border)) { g.FillPath(fill, path); g.DrawPath(pen, path); }
                TextRenderer.DrawText(g, key, font, new Rectangle(x, 2, width, 24), c.Secondary, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter); x += width + 8;
            }
            TextRenderer.DrawText(g, "или колёсико · шаг 5%", Font, new Rectangle(x + 4, 0, Width - x - 4, Height), c.Secondary, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class QuickForm : Form
    {
        internal Palette Colors;
        internal QuickForm(Palette colors) { Colors = colors; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; BackColor = colors.Background; ForeColor = colors.Text; DoubleBuffered = true; KeyPreview = true; }
        protected override void OnPaint(PaintEventArgs e) { using (var pen = new Pen(Colors.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); base.OnPaint(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } base.OnKeyDown(e); }
    }

    internal sealed class ThemeMenuRenderer : ToolStripProfessionalRenderer
    {
        internal Palette Colors = new Palette(true);
        internal ThemeMenuRenderer() { RoundedEdges = false; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Colors.Surface); }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) { if (e.Item.Selected) using (var b = new SolidBrush(Colors.Raised)) e.Graphics.FillRectangle(b, new Rectangle(4, 0, e.Item.Width - 8, e.Item.Height)); }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Colors.Text; base.OnRenderItemText(e); }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) { using (var pen = new Pen(Colors.Border)) e.Graphics.DrawLine(pen, 12, 3, e.Item.Width - 12, 3); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using (var pen = new Pen(Colors.Border)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }
    }
}
