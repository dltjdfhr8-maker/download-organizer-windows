using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal static class SoftUi
    {
        internal static readonly Color Background = Color.FromArgb(246, 248, 252);
        internal static readonly Color Surface = Color.White;
        internal static readonly Color Text = Color.FromArgb(40, 54, 74);
        internal static readonly Color Muted = Color.FromArgb(102, 119, 141);
        internal static readonly Color Border = Color.FromArgb(214, 224, 236);
        internal static readonly Color Accent = Color.FromArgb(57, 112, 181);
        internal static readonly Color AccentHover = Color.FromArgb(48, 101, 169);
        internal static readonly Color AccentPressed = Color.FromArgb(36, 84, 148);
        internal static readonly Color AccentSoft = Color.FromArgb(231, 240, 252);

        // First opaque background up the parent chain; layout panels are often transparent.
        internal static Color EffectiveBack(Control control)
        {
            for (Control cursor = control; cursor != null; cursor = cursor.Parent)
                if (cursor.BackColor.A == 255) return cursor.BackColor;
            return Background;
        }

        internal static void Apply(Form form)
        {
            form.BackColor = Background;
            form.ForeColor = Text;
            ApplyChildren(form);
        }

        private static void ApplyChildren(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                TextBox textBox = control as TextBox;
                if (textBox != null)
                {
                    textBox.BackColor = Surface;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                }
                ComboBox comboBox = control as ComboBox;
                if (comboBox != null)
                {
                    comboBox.BackColor = Surface;
                    comboBox.FlatStyle = FlatStyle.Flat;
                }
                ListView list = control as ListView;
                if (list != null)
                {
                    list.BackColor = Surface;
                    list.ForeColor = Text;
                    list.BorderStyle = BorderStyle.FixedSingle;
                }
                TreeView tree = control as TreeView;
                if (tree != null)
                {
                    tree.BackColor = Surface;
                    tree.ForeColor = Text;
                    tree.LineColor = Border;
                    tree.BorderStyle = BorderStyle.FixedSingle;
                }
                if (control.HasChildren) ApplyChildren(control);
            }
        }
    }

    internal static class SoftDraw
    {
        internal static readonly Color Green = Color.FromArgb(39, 150, 96);
        internal static readonly Color GreenSoft = Color.FromArgb(226, 245, 234);
        internal static readonly Color GreenInk = Color.FromArgb(24, 118, 70);
        internal static readonly Color BlueInk = Color.FromArgb(40, 96, 170);
        internal static readonly Color Gray = Color.FromArgb(238, 241, 246);
        internal static readonly Color GrayInk = Color.FromArgb(92, 106, 126);

        internal static GraphicsPath Round(Rectangle box, int radius)
        {
            int diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(box.Width, box.Height)));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
            path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
            path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void Card(Graphics graphics, Rectangle box, Color parent)
        {
            graphics.Clear(parent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle body = new Rectangle(box.Left, box.Top, box.Width - 1, box.Height - 4);
            using (GraphicsPath shadow = Round(new Rectangle(body.Left, body.Top + 3, body.Width, body.Height), 14))
            using (SolidBrush shade = new SolidBrush(Color.FromArgb(228, 233, 241)))
                graphics.FillPath(shade, shadow);
            using (GraphicsPath path = Round(body, 14))
            using (SolidBrush fill = new SolidBrush(SoftUi.Surface))
            using (Pen pen = new Pen(Color.FromArgb(226, 232, 241)))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(pen, path);
            }
        }

        internal static void Pill(Graphics graphics, Rectangle box, string text, Font font, Color fill, Color ink)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Round(box, box.Height / 2))
            using (SolidBrush brush = new SolidBrush(fill))
                graphics.FillPath(brush, path);
            TextRenderer.DrawText(graphics, text, font, box, ink, TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        internal static int PillWidth(string text, Font font)
        {
            return TextRenderer.MeasureText(text, font, new Size(1000, 40), TextFormatFlags.NoPadding).Width + 22;
        }

        internal static void FolderGlyph(Graphics graphics, Rectangle box, Color body, Color tab)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int w = box.Width, h = box.Height;
            using (SolidBrush tabBrush = new SolidBrush(tab))
            using (GraphicsPath tabPath = Round(new Rectangle(box.Left + w / 12, box.Top + h / 6, w * 5 / 12, h / 3), Math.Max(1, w / 10)))
                graphics.FillPath(tabBrush, tabPath);
            using (SolidBrush bodyBrush = new SolidBrush(body))
            using (GraphicsPath bodyPath = Round(new Rectangle(box.Left + w / 12, box.Top + h * 3 / 10, w * 10 / 12, h * 6 / 10), Math.Max(1, w / 8)))
                graphics.FillPath(bodyBrush, bodyPath);
        }
    }

    // White rounded card with a soft shadow; children sit on its white surface.
    internal class CardPanel : Panel
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = SoftUi.Surface;
            Padding = new Padding(20, 14, 20, 16);
            Margin = new Padding(0, 0, 0, 12);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            SoftDraw.Card(e.Graphics, ClientRectangle, SoftUi.EffectiveBack(Parent));
        }
    }

    internal sealed class PillLabel : Control
    {
        private Color fill = SoftDraw.Gray, ink = SoftDraw.GrayInk;

        public PillLabel(string text, Color fillColor, Color inkColor)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold);
            fill = fillColor; ink = inkColor;
            Text = text;
            Height = 24; Margin = new Padding(10, 4, 0, 0);
            Width = SoftDraw.PillWidth(text, Font);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            Width = SoftDraw.PillWidth(Text, Font); Invalidate(); base.OnTextChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(SoftUi.EffectiveBack(Parent));
            SoftDraw.Pill(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Text, Font, fill, ink);
        }
    }

    // One of the two "from → to" folder cards at the top of the structure editor.
    internal sealed class LocationCard : Control
    {
        private readonly Font captionFont = new Font("Malgun Gothic", 8F);
        private readonly Font titleFont = new Font("Malgun Gothic", 12F, FontStyle.Bold);
        private readonly Font pathFont = new Font("Malgun Gothic", 7.5F);
        private readonly Font badgeFont = new Font("Malgun Gothic", 8F, FontStyle.Bold);
        private string caption = "", title = "", path = "", badge = "";
        private readonly bool green;

        public LocationCard(bool greenAccent)
        {
            green = greenAccent;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Margin = new Padding(0);
        }

        internal void SetContent(string captionText, string titleText, string pathText, string badgeText)
        {
            caption = captionText; title = titleText; path = pathText; badge = badgeText;
            Invalidate();
        }

        internal string TitleText { get { return title; } }
        internal string PathText { get { return path; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            SoftDraw.Card(g, ClientRectangle, SoftUi.EffectiveBack(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color tile = green ? SoftDraw.GreenSoft : SoftUi.AccentSoft;
            Rectangle icon = new Rectangle(12, (Height - 4 - 38) / 2, 38, 38);
            using (GraphicsPath tilePath = SoftDraw.Round(icon, 10))
            using (SolidBrush brush = new SolidBrush(tile))
                g.FillPath(brush, tilePath);
            SoftDraw.FolderGlyph(g, Rectangle.Inflate(icon, -9, -9),
                green ? SoftDraw.Green : Color.FromArgb(76, 132, 222),
                green ? Color.FromArgb(118, 196, 150) : Color.FromArgb(146, 183, 240));
            int left = icon.Right + 12;
            int badgeWidth = badge == "" ? 0 : SoftDraw.PillWidth(badge, badgeFont);
            int right = Width - 14 - badgeWidth - 8;
            TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, caption, captionFont, new Rectangle(left, 7, right - left, 16), SoftUi.Muted, flags);
            TextRenderer.DrawText(g, title, titleFont, new Rectangle(left, 22, right - left, 25), SoftUi.Text, flags);
            TextRenderer.DrawText(g, path, pathFont, new Rectangle(left, 48, Width - 14 - left, 16),
                Color.FromArgb(130, 144, 163), flags | TextFormatFlags.PathEllipsis);
            if (badge != "")
                SoftDraw.Pill(g, new Rectangle(Width - 14 - badgeWidth, 9, badgeWidth, 22), badge, badgeFont,
                    green ? SoftDraw.GreenSoft : SoftUi.AccentSoft, green ? SoftDraw.GreenInk : SoftDraw.BlueInk);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { captionFont.Dispose(); titleFont.Dispose(); pathFont.Dispose(); badgeFont.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class ArrowBadge : Control
    {
        public ArrowBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Margin = new Padding(0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(SoftUi.EffectiveBack(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int size = Math.Min(Math.Min(Width, Height) - 4, 52);
            Rectangle circle = new Rectangle((Width - size) / 2, (Height - 4 - size) / 2, size, size);
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(226, 234, 247))) g.FillEllipse(brush, circle);
            using (Pen pen = new Pen(SoftUi.Accent, 2.6F) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                int cx = circle.Left + circle.Width / 2, cy = circle.Top + circle.Height / 2;
                g.DrawLine(pen, cx - 11, cy, cx + 10, cy);
                g.DrawLine(pen, cx + 4, cy - 6, cx + 10, cy);
                g.DrawLine(pen, cx + 4, cy + 6, cx + 10, cy);
            }
        }
    }

    // Top bar of a window: app tile and title.
    internal sealed class AppHeader : Control
    {
        private readonly Font titleFont = new Font("Malgun Gothic", 12F, FontStyle.Bold);

        public AppHeader(string title)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = title; Dock = DockStyle.Top; Height = 58; Margin = new Padding(0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(SoftUi.Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle tile = new Rectangle(28, (Height - 34) / 2, 34, 34);
            using (GraphicsPath path = SoftDraw.Round(tile, 9))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(62, 118, 226)))
                g.FillPath(brush, path);
            SoftDraw.FolderGlyph(g, Rectangle.Inflate(tile, -8, -8), Color.White, Color.FromArgb(205, 222, 250));
            TextRenderer.DrawText(g, Text, titleFont, new Point(tile.Right + 14, (Height - titleFont.Height) / 2), SoftUi.Text);
            using (Pen line = new Pen(Color.FromArgb(230, 235, 242))) g.DrawLine(line, 0, Height - 1, Width, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) titleFont.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class SoftButton : Button
    {
        private bool hovered, pressed;
        private bool primary;

        internal bool Primary
        {
            get { return primary; }
            set { if (primary != value) { primary = value; Invalidate(); } }
        }

        public SoftButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Margin = new Padding(4, 3, 4, 3);
        }

        protected override void OnMouseEnter(EventArgs e)
        { hovered = true; Invalidate(); base.OnMouseEnter(e); }

        protected override void OnMouseLeave(EventArgs e)
        { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnEnabledChanged(EventArgs e)
        { Invalidate(); base.OnEnabledChanged(e); }

        private static GraphicsPath RoundedRectangle(Rectangle box, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
            path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
            path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color parentBackground = SoftUi.EffectiveBack(Parent);
            e.Graphics.Clear(parentBackground);
            Rectangle box = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            if (box.Width < 18 || box.Height < 18) return;
            Color fill = !Enabled ? Color.FromArgb(240, 243, 247) : primary ?
                pressed ? SoftUi.AccentPressed : hovered ? SoftUi.AccentHover : SoftUi.Accent :
                pressed ? Color.FromArgb(220, 234, 250) : hovered ? SoftUi.AccentSoft : SoftUi.Surface;
            Color outline = !Enabled ? Color.FromArgb(227, 232, 239) : primary ? fill : SoftUi.Border;
            Color foreground = !Enabled ? Color.FromArgb(155, 165, 178) : primary ?
                Color.White : SoftUi.Text;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedRectangle(box, 8))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(outline))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            Rectangle textBounds = Rectangle.Inflate(box, -8, -2);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(box, -4, -4),
                    primary ? Color.White : SoftUi.Text, fill);
        }
    }
}
