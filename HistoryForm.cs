using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    public class HistoryForm : Form
    {
        private readonly ChangeJournal journal;
        private readonly Action<ChangeRecord> afterUndo;
        private readonly Action persistChanges;
        private List<ChangeRecord> visible = new List<ChangeRecord>();
        private ComboBox filter = new ComboBox();
        private TextBox search = new TextBox();
        private ListView list = new ListView();
        private Label details = new Label();
        private Button undoButton = new SoftButton();
        private readonly ToolTip helpTips = new ToolTip {
            InitialDelay = 450, ReshowDelay = 150, AutoPopDelay = 10000, ShowAlways = true
        };
        private bool working;

        public HistoryForm(ChangeJournal changes, Action<ChangeRecord> onUndo,
            Action persist = null)
        {
            journal = changes; afterUndo = onUndo; persistChanges = persist;
            Text = "파일·폴더 변경 이력";
            Width = 1050; Height = 620; MinimumSize = new Size(760, 460);
            Font = new Font("Malgun Gothic", 9F);
            StartPosition = FormStartPosition.CenterParent;
            BuildUi();
            SoftUi.Apply(this);
            RefreshVisible();
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (working) e.Cancel = true;
            };
            Disposed += delegate { helpTips.Dispose(); };
        }

        private void BuildUi()
        {
            TableLayoutPanel layout = new TableLayoutPanel {
                Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            Controls.Add(layout);
            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            tools.Controls.Add(new Label { Text = "표시", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
            filter.DropDownStyle = ComboBoxStyle.DropDownList; filter.Width = 120;
            filter.Items.AddRange(new object[] { "전체", "파일", "폴더" }); filter.SelectedIndex = 0;
            filter.SelectedIndexChanged += delegate { RefreshVisible(); };
            tools.Controls.Add(filter);
            tools.Controls.Add(new Label { Text = "검색", AutoSize = true, Padding = new Padding(18, 7, 0, 0) });
            search.Width = 330; search.TextChanged += delegate { RefreshVisible(); };
            tools.Controls.Add(search);
            layout.Controls.Add(tools, 0, 0);

            list.Dock = DockStyle.Fill; list.View = View.Details; list.FullRowSelect = true;
            list.HideSelection = false; list.VirtualMode = true;
            list.Columns.Add("시간", 135); list.Columns.Add("종류", 90);
            list.Columns.Add("항목", 190); list.Columns.Add("이전 위치", 225);
            list.Columns.Add("변경 위치", 225); list.Columns.Add("상태", 90);
            list.RetrieveVirtualItem += delegate(object sender, RetrieveVirtualItemEventArgs e)
            {
                if (e.ItemIndex < 0 || e.ItemIndex >= visible.Count)
                { e.Item = new ListViewItem(""); return; }
                ChangeRecord record = visible[e.ItemIndex];
                string path = record.Reason == "파일 이름 변경" && record.Undone ?
                    record.Current : record.To != "" ? record.To : record.From;
                e.Item = new ListViewItem(new string[] {
                    record.When, DisplayKind(record), Path.GetFileName(path),
                    record.From, record.To, record.Undone ? "되돌림" : "적용됨"
                });
                if ((e.ItemIndex & 1) != 0) e.Item.BackColor = Color.FromArgb(249, 251, 254);
            };
            list.SelectedIndexChanged += delegate { ShowDetails(); };
            layout.Controls.Add(list, 0, 1);

            details.Dock = DockStyle.Fill; details.BorderStyle = BorderStyle.None;
            details.BackColor = SoftUi.AccentSoft;
            details.Padding = new Padding(12); details.Text = "이력을 선택하면 변경 전후 경로가 표시됩니다.";
            layout.Controls.Add(details, 0, 2);
            FlowLayoutPanel buttons = new FlowLayoutPanel {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft
            };
            Button close = new SoftButton { Text = "닫기", Width = 90, Height = 34 };
            close.Click += delegate { Close(); };
            undoButton.Text = "선택한 이력 되돌리기"; undoButton.Width = 175; undoButton.Height = 34;
            ((SoftButton)undoButton).Primary = true;
            undoButton.Click += UndoSelected;
            buttons.Controls.Add(close); buttons.Controls.Add(undoButton);
            layout.Controls.Add(buttons, 0, 3);

            helpTips.SetToolTip(filter, "전체 이력이나 파일·폴더 이력만 골라 봅니다.");
            helpTips.SetToolTip(search, "파일·폴더 이름 또는 변경 전후 경로로 이력을 찾습니다.");
            helpTips.SetToolTip(list, "되돌릴 이력을 선택하면 아래에 변경 전후 위치가 표시됩니다.");
            helpTips.SetToolTip(undoButton, "선택한 변경을 되돌립니다. 일괄 이름 변경은 같은 작업의 나머지 파일을 함께 되돌릴 수 있습니다.");
            helpTips.SetToolTip(close, "변경 이력 창을 닫습니다.");
        }

        private static string DisplayKind(ChangeRecord record)
        {
            return record.Kind == "파일 이동" && record.Reason == "파일 이름 변경" ?
                "이름 변경" : record.Kind;
        }

        private void RefreshVisible()
        {
            if (list.IsDisposed) return;
            string query = search.Text.Trim();
            string kind = filter.SelectedItem == null ? "전체" : filter.SelectedItem.ToString();
            visible = journal.Records.AsEnumerable().Reverse().Where(record =>
                (kind == "전체" || (kind == "파일" ? record.Kind == "파일 이동" : record.Kind != "파일 이동")) &&
                (query == "" || record.From.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                 record.To.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                 record.Kind.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                 DisplayKind(record).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0))
                .ToList();
            list.VirtualListSize = visible.Count;
            list.Invalidate();
            details.Text = visible.Count + "개 이력이 있습니다. 파일이나 폴더의 이전 위치를 확인하고 되돌릴 수 있습니다.";
        }

        private ChangeRecord Selected()
        {
            if (list.SelectedIndices.Count == 0) return null;
            int index = list.SelectedIndices[0];
            return index >= 0 && index < visible.Count ? visible[index] : null;
        }

        private void ShowDetails()
        {
            ChangeRecord record = Selected();
            if (record == null) return;
            details.Text = DisplayKind(record) + "  ·  " + record.When + "\n" +
                "이전: " + (record.From == "" ? "(없음)" : record.From) + "\n" +
                "변경: " + (record.To == "" ? "(없음)" : record.To) +
                (record.Current != "" && record.Current != record.To ? "\n현재: " + record.Current : "") +
                (journal.PendingRenameGroup(record).Count > 1 ? "\n같은 작업에서 바꾼 이름 " +
                    journal.PendingRenameGroup(record).Count + "개를 함께 되돌릴 수 있습니다." : "");
        }

        private void UndoSelected(object sender, EventArgs e)
        {
            if (working) return;
            ChangeRecord record = Selected();
            if (record == null) { details.Text = "되돌릴 이력을 선택하세요."; return; }
            List<ChangeRecord> pendingGroup = journal.PendingRenameGroup(record);
            bool groupUndo = false;
            if (pendingGroup.Count > 1 || (record.Undone && pendingGroup.Count > 0))
            {
                DialogResult choice = MessageBox.Show(this,
                    "같은 작업에서 이름을 바꾼 파일 " + pendingGroup.Count + "개가 아직 남아 있습니다.\n\n" +
                    "예: 남은 파일 모두 되돌리기\n아니요: 선택한 파일 하나만 되돌리기\n취소: 그대로 두기",
                    "이름 변경 되돌리기", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (choice == DialogResult.Cancel) return;
                groupUndo = choice == DialogResult.Yes;
            }
            if (!groupUndo)
            {
                if (record.Undone) { details.Text = "이 파일 이름은 이미 되돌렸습니다."; return; }
                if (MessageBox.Show(this, DisplayKind(record) + "\n" + record.From + "\n→ " + record.To +
                    "\n\n이 항목을 이전 상태로 돌릴까요?", "이력 되돌리기 확인",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            working = true; undoButton.Enabled = false; filter.Enabled = false; list.Enabled = false;
            search.ReadOnly = true; details.Text = "선택한 항목을 되돌리는 중입니다...";
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate(object workSender, DoWorkEventArgs workArgs)
            {
                List<ChangeRecord> affected;
                string message;
                if (groupUndo)
                {
                    affected = journal.UndoRenameGroup(record);
                    message = "파일 " + affected.Count + "개의 이름을 원래대로 되돌렸습니다.";
                }
                else
                {
                    message = journal.Undo(record);
                    affected = new List<ChangeRecord> { record };
                }
                string warning = "";
                try { if (persistChanges != null) persistChanges(); }
                catch (Exception ex) { warning = ex.Message; }
                workArgs.Result = Tuple.Create(message, warning, affected);
            };
            worker.RunWorkerCompleted += delegate(object workSender, RunWorkerCompletedEventArgs result)
            {
                working = false; undoButton.Enabled = true; filter.Enabled = true;
                list.Enabled = true; search.ReadOnly = false;
                if (result.Error != null)
                {
                    details.Text = result.Error.Message;
                    MessageBox.Show(this, result.Error.Message, "되돌리기 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Tuple<string, string, List<ChangeRecord>> outcome =
                    (Tuple<string, string, List<ChangeRecord>>)result.Result;
                try { foreach (ChangeRecord changed in outcome.Item3) afterUndo(changed); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "항목은 되돌렸지만 이력 저장에 실패했습니다: " + ex.Message,
                        "이력 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                RefreshVisible();
                details.Text = outcome.Item1;
                if (outcome.Item2 != "")
                    MessageBox.Show(this, "항목은 되돌렸지만 이력 저장에 실패했습니다: " + outcome.Item2,
                        "이력 저장 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            worker.RunWorkerAsync();
        }
    }
}
