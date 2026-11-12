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
            Color parentBackground = Parent == null ? SoftUi.Background : Parent.BackColor;
            if (parentBackground == Color.Transparent) parentBackground = SoftUi.Background;
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
