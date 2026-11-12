using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    // Lets the user review suggested topics: tick, rename (F2 / double-click) and see how many
    // files each topic would actually receive with the same rule the structure editor uses.
    internal class TopicSuggestionForm : Form
    {
        private readonly List<string> stems;
        private readonly Dictionary<string, double[]> scores;
        private readonly ListView list = new ListView();
        private readonly Label summary = new Label();
        private bool ready;

        public List<string> ChosenLines { get; private set; }
        public bool ReplaceExisting { get; private set; }

        internal TopicSuggestionForm(List<TopicCandidate> candidates, List<string> fileStems,
            Dictionary<string, double[]> precomputed)
        {
            stems = fileStems;
            scores = precomputed ?? new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            ChosenLines = new List<string>();
            Text = "업무·주제 추천";
            Width = 900; Height = 560; MinimumSize = new Size(700, 420);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Malgun Gothic", 9F);
            BuildUi(candidates);
            SoftUi.Apply(this);
            ready = true;
            Recount();
        }

        internal static string TopicPath(string line)
        {
            return String.Join("\\", line.Replace("/", ">").Replace("\\", ">").Split('>')
                .Select(x => x.Trim()).Where(x => x != "").ToArray());
        }

        private void BuildUi(List<TopicCandidate> candidates)
        {
            TableLayoutPanel layout = new TableLayoutPanel {
                Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4, ColumnCount = 1
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            Controls.Add(layout);
            layout.Controls.Add(new Label {
                Text = "감시 폴더의 하위 폴더 이름과 파일 이름에 자주 함께 나오는 낱말로 주제를 골랐습니다. " +
                    "파일 내용은 읽지 않습니다. 쓸 주제만 체크하고, 이름은 F2나 두 번 클릭으로 고칠 수 있습니다. " +
                    "'상위 > 하위'는 하위 폴더로 만들어집니다.",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            list.Dock = DockStyle.Fill; list.View = View.Details; list.CheckBoxes = true;
            list.FullRowSelect = true; list.LabelEdit = true; list.HideSelection = false;
            list.Columns.Add("주제", 300); list.Columns.Add("자동 배치 예상", 110);
            list.Columns.Add("이름에 포함", 95); list.Columns.Add("예시 파일", 340);
            foreach (TopicCandidate candidate in candidates)
            {
                ListViewItem item = new ListViewItem(new string[] {
                    candidate.Line, "", candidate.FileCount + "개",
                    (candidate.MatchesExistingFolder ? "[같은 이름의 정리 폴더 있음] " : "") +
                    String.Join(", ", candidate.Examples.ToArray())
                });
                item.Checked = !candidate.MatchesExistingFolder;
                if (candidate.Line.Contains(">")) item.IndentCount = 1;
                list.Items.Add(item);
            }
            list.ItemChecked += delegate { if (ready) Recount(); };
            list.AfterLabelEdit += delegate(object sender, LabelEditEventArgs e)
            {
                if (e.Label == null) return;
                if (TopicPath(e.Label) == "") { e.CancelEdit = true; return; }
                BeginInvoke((MethodInvoker)Recount);
            };
            list.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F2 && list.SelectedItems.Count == 1)
                { list.SelectedItems[0].BeginEdit(); e.Handled = true; }
            };
            list.DoubleClick += delegate { if (list.SelectedItems.Count == 1) list.SelectedItems[0].BeginEdit(); };
            layout.Controls.Add(list, 0, 1);

            summary.Dock = DockStyle.Fill; summary.TextAlign = ContentAlignment.MiddleLeft;
            summary.ForeColor = Color.FromArgb(39, 70, 103);
            layout.Controls.Add(summary, 0, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false
            };
            Button cancel = new SoftButton { Text = "취소", Width = 90, Height = 34, DialogResult = DialogResult.Cancel };
            Button replace = new SoftButton { Text = "목록 바꾸기", Width = 120, Height = 34 };
            Button add = new SoftButton { Text = "목록에 추가", Width = 120, Height = 34 };
            ((SoftButton)add).Primary = true;
            add.Click += delegate { Finish(false); };
            replace.Click += delegate { Finish(true); };
            buttons.Controls.Add(cancel); buttons.Controls.Add(add); buttons.Controls.Add(replace);
            layout.Controls.Add(buttons, 0, 3);
            CancelButton = cancel;
            ToolTip tips = new ToolTip();
            tips.SetToolTip(add, "체크한 주제를 지금 목록 뒤에 붙입니다. 이미 있는 줄은 건너뜁니다.");
            tips.SetToolTip(replace, "지금 목록을 지우고 체크한 주제로 바꿉니다.");
            Disposed += delegate { tips.Dispose(); };
        }

        private double[] Scores(string leaf)
        {
            double[] column;
            if (!scores.TryGetValue(leaf, out column))
            {
                column = stems.Select(stem => TopicSuggester.TopicScore(stem, leaf)).ToArray();
                scores[leaf] = column;
            }
            return column;
        }

        private void Recount()
        {
            List<ListViewItem> chosen = list.Items.Cast<ListViewItem>().Where(i => i.Checked).ToList();
            List<string> paths = chosen.Select(i => TopicPath(i.Text)).ToList();
            List<double[]> columns = paths.Select(p => Scores(TopicSuggester.LeafName(p))).ToList();
            int[] counts = new int[paths.Count];
            int placed = 0;
            double[] row = new double[paths.Count];
            for (int f = 0; f < stems.Count; f++)
            {
                for (int t = 0; t < paths.Count; t++) row[t] = columns[t][f];
                int pick = TopicSuggester.Pick(paths, row);
                if (pick >= 0) { counts[pick]++; placed++; }
            }
            foreach (ListViewItem item in list.Items) item.SubItems[1].Text = item.Checked ? "" : "-";
            for (int t = 0; t < chosen.Count; t++) chosen[t].SubItems[1].Text = counts[t] + "개";
            summary.Text = "체크한 주제 " + chosen.Count + "개로 감시 폴더 파일 " + stems.Count + "개 중 " +
                placed + "개가 자동 배치될 예정입니다. 나머지는 그대로 두거나 직접 옮길 수 있습니다.";
        }

        private void Finish(bool replace)
        {
            ChosenLines = list.Items.Cast<ListViewItem>().Where(i => i.Checked)
                .Select(i => String.Join(" > ", TopicPath(i.Text).Split('\\')))
                .Where(x => x != "").ToList();
            if (ChosenLines.Count == 0)
            { summary.Text = "넣을 주제를 하나 이상 체크하세요."; return; }
            ReplaceExisting = replace;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
