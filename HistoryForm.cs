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
        private ComboBox undoScope = new ComboBox();
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
            list.MultiSelect = true;
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
            TableLayoutPanel footer = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FlowLayoutPanel scopeTools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            scopeTools.Controls.Add(new Label { Text = "되돌릴 범위", AutoSize = true,
                Padding = new Padding(0, 7, 0, 0) });
            undoScope.DropDownStyle = ComboBoxStyle.DropDownList; undoScope.Width = 170;
            undoScope.Items.AddRange(new object[] { "선택한 이력", "같은 작업 전체", "현재 목록 전체" });
            undoScope.SelectedIndex = 0;
            undoScope.SelectedIndexChanged += delegate { ShowDetails(); };
            scopeTools.Controls.Add(undoScope);
            footer.Controls.Add(scopeTools, 0, 0);
            FlowLayoutPanel buttons = new FlowLayoutPanel {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            Button close = new SoftButton { Text = "닫기", Width = 90, Height = 34 };
            close.Click += delegate { Close(); };
            undoButton.Text = "선택한 이력 되돌리기"; undoButton.Width = 210; undoButton.Height = 34;
            ((SoftButton)undoButton).Primary = true;
            undoButton.Click += UndoSelected;
            buttons.Controls.Add(close); buttons.Controls.Add(undoButton);
            footer.Controls.Add(buttons, 1, 0);
            layout.Controls.Add(footer, 0, 3);

            helpTips.SetToolTip(filter, "전체 이력이나 파일·폴더 이력만 골라 봅니다.");
            helpTips.SetToolTip(search, "파일·폴더 이름 또는 변경 전후 경로로 이력을 찾습니다.");
            helpTips.SetToolTip(list, "Ctrl·Shift로 여러 이력을 선택할 수 있습니다. 아래에서 되돌릴 범위를 확인하세요.");
            helpTips.SetToolTip(undoScope, "선택한 이력, 같은 작업의 모든 이력, 현재 검색·필터 결과 전체 중 고릅니다.");
            helpTips.SetToolTip(undoButton, "선택한 범위의 이력을 한 번에 되돌립니다. 충돌한 항목은 그대로 두고 결과를 알려줍니다.");
            helpTips.SetToolTip(close, "변경 이력 창을 닫습니다.");
        }

        private static string DisplayKind(ChangeRecord record)
        {
            if (record.Kind == "파일 이동" && record.Reason == "파일 이름 변경") return "이름 변경";
            if (record.Kind == "폴더 이동" && record.Reason == ChangeJournal.RootRenameReason)
                return ChangeJournal.RootRenameReason;
            return record.Kind;
        }

        private void RefreshVisible()
        {
            if (list.IsDisposed) return;
            string query = search.Text.Trim();
            string kind = filter.SelectedItem == null ? "전체" : filter.SelectedItem.ToString();
            list.SelectedIndices.Clear();
            visible = journal.Records.AsEnumerable().Reverse().Where(record =>
                (kind == "전체" || (kind == "파일" ? record.Kind.StartsWith("파일", StringComparison.Ordinal) :
                    !record.Kind.StartsWith("파일", StringComparison.Ordinal))) &&
                (query == "" || record.From.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                 record.To.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                 record.Kind.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                 DisplayKind(record).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0))
                .ToList();
            list.VirtualListSize = visible.Count;
            list.Invalidate();
            details.Text = visible.Count + "개 이력이 있습니다. Ctrl·Shift로 여러 항목을 선택할 수 있습니다.";
            UpdateUndoButton();
        }

        private List<ChangeRecord> SelectedRecords()
        {
            return list.SelectedIndices.Cast<int>().Where(index => index >= 0 && index < visible.Count)
                .Select(index => visible[index]).Distinct().ToList();
        }

        private List<ChangeRecord> UndoTargets()
        {
            List<ChangeRecord> selected = SelectedRecords();
            if (undoScope.SelectedIndex == 2)
                return visible.Where(record => !record.Undone).ToList();
            if (undoScope.SelectedIndex == 1)
            {
                HashSet<string> groups = new HashSet<string>(selected.Where(record =>
                    record.GroupId != "").Select(record => record.GroupId));
                return journal.Records.Where(record => !record.Undone &&
                    (selected.Contains(record) || (record.GroupId != "" &&
                    groups.Contains(record.GroupId)))).ToList();
            }
            return selected.Where(record => !record.Undone).ToList();
        }

        private void UpdateUndoButton()
        {
            int count = UndoTargets().Count;
            undoButton.Text = count > 1 ? count + "개 이력 되돌리기" : "선택한 이력 되돌리기";
        }

        private void ShowDetails()
        {
            List<ChangeRecord> selected = SelectedRecords();
            UpdateUndoButton();
            if (selected.Count == 0)
            {
                details.Text = undoScope.SelectedIndex == 2 ?
                    "현재 목록의 되돌리지 않은 이력 " + UndoTargets().Count + "개를 대상으로 합니다." :
                    "Ctrl·Shift로 이력을 선택하세요.";
                return;
            }
            if (selected.Count > 1)
            {
                details.Text = selected.Count + "개 이력을 선택했습니다. 되돌릴 범위: " +
                    undoScope.SelectedItem + " · 실제 대상 " + UndoTargets().Count + "개\n" +
                    "충돌한 항목은 그대로 두고 가능한 항목부터 되돌립니다.";
                return;
            }
            ChangeRecord record = selected[0];
            details.Text = DisplayKind(record) + "  ·  " + record.When + "\n" +
                (record.Kind == "파일 복사" ? "원본: " : "이전: ") +
                (record.From == "" ? "(없음)" : record.From) + "\n" +
                (record.Kind == "파일 복사" ? "복사본: " : "변경: ") +
                (record.To == "" ? "(없음)" : record.To) +
                (record.Current != "" && record.Current != record.To ? "\n현재: " + record.Current : "") +
                (journal.PendingRenameGroup(record).Count > 1 ? "\n같은 작업에서 바꾼 이름 " +
                    journal.PendingRenameGroup(record).Count + "개를 '같은 작업 전체'로 되돌릴 수 있습니다." : "");
        }

        private void UndoSelected(object sender, EventArgs e)
        {
            if (working) return;
            List<ChangeRecord> targets = UndoTargets();
            if (targets.Count == 0) { details.Text = "되돌릴 이력을 선택하세요."; return; }
            string examples = String.Join("\n", targets.Take(4).Select(record =>
                "• " + DisplayKind(record) + " · " + Path.GetFileName(record.To != "" ?
                    record.To : record.From)).ToArray());
            string confirmation = "범위: " + undoScope.SelectedItem + "\n대상: " + targets.Count +
                "개 이력\n\n" + examples + (targets.Count > 4 ? "\n• 그 밖에 " +
                (targets.Count - 4) + "개" : "") +
                (targets.Any(record => record.Kind == "파일 복사") ?
                    "\n\n파일 복사 이력은 복사본만 제거합니다." : "") +
                "\n\n이전 상태로 돌릴까요? 충돌한 항목은 그대로 두고 결과를 알려줍니다.";
            if (MessageBox.Show(this, confirmation, "일괄 되돌리기 확인",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            working = true; undoButton.Enabled = false; filter.Enabled = false;
            undoScope.Enabled = false; list.Enabled = false;
            search.ReadOnly = true; details.Text = "선택한 항목을 되돌리는 중입니다...";
            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += delegate(object workSender, DoWorkEventArgs workArgs)
            {
                workArgs.Result = journal.UndoMany(targets, persistChanges,
                    delegate(int completed, int total, string name) {
                        worker.ReportProgress(total == 0 ? 0 : completed * 100 / total,
                            completed + "/" + total + "개 되돌리는 중 · " + name);
                    });
            };
            worker.ProgressChanged += delegate(object progressSender, ProgressChangedEventArgs progressArgs)
            { if (!IsDisposed) details.Text = (string)progressArgs.UserState; };
            worker.RunWorkerCompleted += delegate(object workSender, RunWorkerCompletedEventArgs result)
            {
                working = false; undoButton.Enabled = true; filter.Enabled = true;
                undoScope.Enabled = true; list.Enabled = true; search.ReadOnly = false;
                if (result.Error != null)
                {
                    details.Text = result.Error.Message;
                    MessageBox.Show(this, result.Error.Message, "되돌리기 불가", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                UndoBatchOutcome outcome = (UndoBatchOutcome)result.Result;
                string callbackError = "";
                foreach (ChangeRecord changed in outcome.Undone)
                {
                    try { if (afterUndo != null) afterUndo(changed); }
                    catch (Exception ex) { if (callbackError == "") callbackError = ex.Message; }
                }
                RefreshVisible();
                details.Text = outcome.Undone.Count + "개 되돌림, " + outcome.Failed.Count +
                    "개 충돌" + (outcome.SaveError == "" ? "" : " · 이력 저장 실패") + ".";
                if (outcome.Failed.Count > 0)
                {
                    string failures = String.Join("\n", outcome.Failed.Take(3).Select(pair =>
                        "• " + Path.GetFileName(pair.Key.To != "" ? pair.Key.To : pair.Key.From) +
                        ": " + pair.Value).ToArray());
                    MessageBox.Show(this, outcome.Failed.Count + "개 이력을 되돌리지 못했습니다.\n" +
                        failures + (outcome.Failed.Count > 3 ? "\n그 밖의 실패는 이력을 다시 확인하세요." : ""),
                        "일부 이력 유지", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                if (outcome.SaveError != "" || callbackError != "")
                    MessageBox.Show(this, "되돌린 항목의 기록을 저장하거나 화면에 반영하지 못했습니다: " +
                        (outcome.SaveError != "" ? outcome.SaveError : callbackError),
                        "이력 저장 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            worker.RunWorkerAsync();
        }
    }
}
