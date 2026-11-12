using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal sealed class DragGhost : Form
    {
        private readonly string headline;
        private readonly string initialDetail;
        private readonly bool folder;
        private readonly int count;
        private readonly Font titleFont;
        private string detail;
        private Color accent = SoftUi.Accent;

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000 | 0x00000020 | 0x00000080;
                return parameters;
            }
        }

        internal DragGhost(List<TreeNode> items)
        {
            count = items.Count;
            folder = count == 1 && items[0].Tag is PlanTag &&
                ((PlanTag)items[0].Tag).Kind == PlanKind.Folder;
            headline = count == 1 ? items[0].Text : count + "개 파일 · " + items[0].Text;
            initialDetail = folder ? "폴더 이동" : count == 1 ? "파일 이동" :
                "선택한 파일 " + count + "개 이동";
            detail = initialDetail;
            Width = 282; Height = 72;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = SoftUi.Surface;
            Opacity = 0.94;
            Font = new Font("Malgun Gothic", 9F);
            titleFont = new Font(Font, FontStyle.Bold);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint, true);
            using (GraphicsPath shape = Rounded(new Rectangle(0, 0, Width, Height), 11))
                Region = new Region(shape);
            Disposed += delegate { titleFont.Dispose(); };
        }

        private static GraphicsPath Rounded(Rectangle box, int radius)
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

        internal void SetTarget(string target, bool allowed, string reason)
        {
            string next = allowed ? "→ " + target : reason == "" ? initialDetail : reason;
            Color nextAccent = allowed ? Color.FromArgb(46, 143, 91) :
                reason == "" ? SoftUi.Accent : Color.FromArgb(194, 111, 54);
            if (detail == next && accent == nextAccent) return;
            detail = next; accent = nextAccent;
            Invalidate();
        }

        internal void ClearTarget()
        {
            SetTarget("", false, "");
        }

        internal void FollowCursor()
        {
            Point cursor = Cursor.Position;
            Rectangle area = Screen.FromPoint(cursor).WorkingArea;
            int x = cursor.X + 20;
            int y = cursor.Y + 18;
            if (x + Width > area.Right) x = cursor.X - Width - 14;
            if (y + Height > area.Bottom) y = cursor.Y - Height - 12;
            Point next = new Point(Math.Max(area.Left, x), Math.Max(area.Top, y));
            if (Location != next) Location = next;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(SoftUi.Surface);
            using (GraphicsPath outline = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10))
            using (Pen pen = new Pen(SoftUi.Border)) e.Graphics.DrawPath(pen, outline);
            using (SolidBrush marker = new SolidBrush(accent))
                e.Graphics.FillRectangle(marker, 0, 8, 4, Height - 16);
            Rectangle icon = new Rectangle(17, 18, 30, 30);
            if (folder)
            {
                using (SolidBrush tab = new SolidBrush(Color.FromArgb(170, 203, 231)))
                using (SolidBrush body = new SolidBrush(Color.FromArgb(104, 158, 212)))
                {
                    e.Graphics.FillRectangle(tab, icon.Left + 2, icon.Top + 4, 13, 6);
                    e.Graphics.FillRectangle(body, icon.Left + 2, icon.Top + 9, 26, 18);
                }
            }
            else
            {
                using (SolidBrush paper = new SolidBrush(Color.White))
                using (Pen line = new Pen(SoftUi.Accent, 1.3F))
                {
                    if (count > 1)
                    {
                        e.Graphics.FillRectangle(paper, icon.Left + 2, icon.Top + 6, 18, 22);
                        e.Graphics.DrawRectangle(line, icon.Left + 2, icon.Top + 6, 18, 22);
                        e.Graphics.FillRectangle(paper, icon.Left + 5, icon.Top + 3, 18, 22);
                        e.Graphics.DrawRectangle(line, icon.Left + 5, icon.Top + 3, 18, 22);
                    }
                    e.Graphics.FillRectangle(paper, icon.Left + 8, icon.Top, 19, 25);
                    e.Graphics.DrawRectangle(line, icon.Left + 8, icon.Top, 19, 25);
                    e.Graphics.DrawLine(line, icon.Left + 12, icon.Top + 9, icon.Left + 23, icon.Top + 9);
                    e.Graphics.DrawLine(line, icon.Left + 12, icon.Top + 14, icon.Left + 23, icon.Top + 14);
                }
            }
            Rectangle titleBox = new Rectangle(57, 9, Width - 70, 26);
            Rectangle detailBox = new Rectangle(57, 38, Width - 70, 22);
            TextRenderer.DrawText(e.Graphics, headline, titleFont,
                titleBox, SoftUi.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(e.Graphics, detail, Font, detailBox, accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }
}
