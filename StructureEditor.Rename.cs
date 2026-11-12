using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    public partial class StructureEditor
    {
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.F2 && (current.ContainsFocus || proposed.ContainsFocus))
            {
                RenameSelected();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        private static bool ValidFileName(string name)
        {
            if (String.IsNullOrWhiteSpace(name) || name == "." || name == ".." ||
                name.Length > 255 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.TrimEnd(' ', '.') != name) return false;
            string device = name.Split('.')[0].ToUpperInvariant();
            return device != "CON" && device != "PRN" && device != "AUX" && device != "NUL" &&
                !(device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) &&
                  device[3] >= '1' && device[3] <= '9');
        }

        private TreeNode FocusedPlannedNode()
        {
            if (lastChosenNode != null && lastChosenNode.TreeView == proposed)
                return lastChosenNode;
            if (lastChosenNode != null && lastChosenNode.TreeView == current)
            {
                TreeNode chosenCounterpart = Counterpart(proposed, NodeTag(lastChosenNode));
                if (chosenCounterpart != null) return chosenCounterpart;
            }
            if (current.ContainsFocus && current.SelectedNode != null)
            {
                TreeNode counterpart = Counterpart(proposed, NodeTag(current.SelectedNode));
                if (counterpart != null) return counterpart;
            }
            TreeNode selected = proposed.SelectedNode;
            if (selected != null) return selected;
            return current.SelectedNode == null ? null : Counterpart(proposed, NodeTag(current.SelectedNode));
        }

        internal bool TryRenamePlannedFolder(TreeNode node, string name, out string error)
        {
            error = "";
            if (!Loaded || working || loading || node == null || node.TreeView != proposed ||
                NodeTag(node) == null || (NodeTag(node).Kind != PlanKind.Folder &&
                NodeTag(node).Kind != PlanKind.Root))
            { error = "오른쪽 구조에서 이름을 바꿀 폴더를 선택하세요."; return false; }
            if (!ValidFileName(name))
            { error = "사용할 수 없는 폴더 이름입니다."; return false; }
            if (node.Text == name)
            { error = "바뀌는 이름이 없습니다."; return false; }
            PlanTag tag = NodeTag(node);
            if (tag.Kind == PlanKind.Root)
            {
                string parent = Path.GetDirectoryName(root);
                if (parent == null)
                { error = "드라이브의 맨 위 폴더는 여기서 이름을 바꿀 수 없습니다."; return false; }
                if (Path.GetFileName(root).Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(root) != name)
                { error = "대소문자만 바꾸는 상위 폴더 이름 변경은 현재 지원하지 않습니다."; return false; }
                string destination = Path.Combine(parent, name);
                if (Directory.Exists(destination) || File.Exists(destination))
                { error = "같은 위치에 이미 같은 이름의 파일이나 폴더가 있습니다."; return false; }
            }
            if (tag.OriginalPath != "" &&
                Path.GetFileName(tag.OriginalPath).Equals(name, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(tag.OriginalPath) != name)
            { error = "대소문자만 바꾸는 폴더 이름 변경은 현재 지원하지 않습니다."; return false; }
            if (node.Parent != null && node.Parent.Nodes.Cast<TreeNode>().Any(other => other != node &&
                other.Text.Equals(name, StringComparison.OrdinalIgnoreCase)))
            { error = "같은 위치에 같은 이름의 파일이나 폴더가 있습니다."; return false; }
            node.Text = name;
            edited = true;
            RefreshPlanVisuals();
            status.Text = "폴더 이름을 오른쪽 계획에서 바꿨습니다. 실제 변경은 '변경 내용 확인·적용'에서 진행합니다.";
            return true;
        }

        private void ShowFolderRenameDialog(TreeNode node)
        {
            using (Form dialog = new Form {
                Text = "폴더 이름 변경", Width = 480, Height = 225,
                MinimumSize = new Size(440, 225), StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
                MinimizeBox = false, Font = new Font("Malgun Gothic", 9F)
            })
            {
                TableLayoutPanel layout = new TableLayoutPanel {
                    Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 4
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                dialog.Controls.Add(layout);
                layout.Controls.Add(new Label { Text = "현재 이름", Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
                layout.Controls.Add(new Label { Text = node.Text, Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 1, 0);
                layout.Controls.Add(new Label { Text = "새 이름", Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
                TextBox input = new TextBox { Dock = DockStyle.Fill, Text = node.Text };
                layout.Controls.Add(input, 1, 1);
                Label errorLabel = new Label { Dock = DockStyle.Fill,
                    ForeColor = Color.Firebrick, TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true };
                layout.Controls.Add(errorLabel, 0, 2);
                layout.SetColumnSpan(errorLabel, 2);
                FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft };
                Button confirm = new SoftButton { Text = "이름 변경", Width = 100, Height = 32 };
                Button cancel = new SoftButton { Text = "취소", Width = 80, Height = 32,
                    DialogResult = DialogResult.Cancel };
                confirm.Click += delegate {
                    string error;
                    if (TryRenamePlannedFolder(node, input.Text, out error)) dialog.DialogResult = DialogResult.OK;
                    else errorLabel.Text = error;
                };
                buttons.Controls.Add(confirm); buttons.Controls.Add(cancel);
                layout.Controls.Add(buttons, 1, 3);
                dialog.AcceptButton = confirm; dialog.CancelButton = cancel;
                SoftUi.Apply(dialog);
                dialog.Shown += delegate { input.SelectAll(); input.Focus(); };
                dialog.ShowDialog(this);
            }
        }

        private List<TreeNode> PlannedSelectedFiles()
        {
            HashSet<TreeNode> selected = selectedProposedFiles.Count > 0 ? selectedProposedFiles :
                new HashSet<TreeNode>(selectedCurrentFiles.Select(n => Counterpart(proposed, NodeTag(n)))
                    .Where(n => n != null));
            List<TreeNode> result = AllNodes(proposed).Where(n => selected.Contains(n) &&
                NodeTag(n) != null && NodeTag(n).Kind == PlanKind.File).ToList();
            TreeNode focused = FocusedPlannedNode();
            if (result.Count == 0 && NodeTag(focused) != null && NodeTag(focused).Kind == PlanKind.File)
                result.Add(focused);
            return result;
        }

        internal bool TryRenamePlannedFiles(IDictionary<TreeNode, string> names, out string error)
        {
            error = "";
            if (!Loaded || working || loading || names == null || names.Count == 0)
            { error = "이름을 바꿀 파일을 선택하세요."; return false; }
            foreach (KeyValuePair<TreeNode, string> entry in names)
            {
                TreeNode node = entry.Key;
                string name = entry.Value;
                if (node == null || node.TreeView != proposed || NodeTag(node) == null ||
                    NodeTag(node).Kind != PlanKind.File)
                { error = "오른쪽 구조에서 파일을 선택하세요."; return false; }
                if (!ValidFileName(name))
                { error = "사용할 수 없는 파일 이름입니다: " + name; return false; }
                if (!Path.GetExtension(name).Equals(Path.GetExtension(node.Text),
                    StringComparison.OrdinalIgnoreCase))
                { error = "확장자는 그대로 두고 이름만 바꿔 주세요: " + name; return false; }
                if (node.Text.Equals(name, StringComparison.OrdinalIgnoreCase) && node.Text != name)
                { error = "대소문자만 바꾸는 이름 변경은 현재 지원하지 않습니다."; return false; }
                if (node.Parent.Nodes.Cast<TreeNode>().Any(other => other != node &&
                    (names.ContainsKey(other) ? names[other] : other.Text).Equals(name,
                        StringComparison.OrdinalIgnoreCase)))
                { error = "같은 위치에 같은 이름의 파일이나 폴더가 있습니다: " + name; return false; }
            }
            int changed = 0;
            proposed.BeginUpdate();
            try
            {
                foreach (KeyValuePair<TreeNode, string> entry in names)
                {
                    if (entry.Key.Text == entry.Value) continue;
                    entry.Key.Text = entry.Value;
                    changed++;
                }
            }
            finally { proposed.EndUpdate(); }
            if (changed == 0)
            { error = "바뀌는 이름이 없습니다."; return false; }
            edited = true;
            RefreshPlanVisuals();
            status.Text = "파일 " + changed + "개의 이름을 오른쪽 계획에서 바꿨습니다. 실제 파일은 적용할 때 변경됩니다.";
            return true;
        }

        private void RenameSelected()
        {
            if (!Loaded || loading || working) return;
            TreeNode node = FocusedPlannedNode();
            PlanTag tag = NodeTag(node);
            if (tag != null && tag.Kind == PlanKind.SourceFolder)
            { status.Text = "감시 폴더의 하위 폴더는 표시용입니다. 정리 폴더의 하위 폴더를 선택하세요."; return; }
            if (tag == null || (tag.Kind != PlanKind.File && tag.Kind != PlanKind.Folder &&
                tag.Kind != PlanKind.Root))
            { status.Text = "이름을 바꿀 파일이나 폴더를 선택하세요."; return; }
            if (tag.Kind == PlanKind.Folder || tag.Kind == PlanKind.Root)
            { ShowFolderRenameDialog(node); return; }

            string extension = Path.GetExtension(node.Text);
            using (Form dialog = new Form {
                Text = "파일 이름 변경", Width = 500, Height = 220,
                MinimumSize = new Size(440, 220), StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
                MinimizeBox = false, Font = new Font("Malgun Gothic", 9F)
            })
            {
                TableLayoutPanel layout = new TableLayoutPanel {
                    Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 4
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                dialog.Controls.Add(layout);
                layout.Controls.Add(new Label { Text = "기존 이름", Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
                layout.Controls.Add(new Label { Text = node.Text, Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 1, 0);
                layout.Controls.Add(new Label { Text = "새 이름", Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
                TextBox input = new TextBox { Dock = DockStyle.Fill,
                    Text = Path.GetFileNameWithoutExtension(node.Text) };
                layout.Controls.Add(input, 1, 1);
                Label preview = new Label { Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
                layout.Controls.Add(new Label { Text = "변경 후", Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
                layout.Controls.Add(preview, 1, 2);
                input.TextChanged += delegate {
                    preview.Text = input.Text + extension;
                    preview.ForeColor = SystemColors.ControlText;
                };
                preview.Text = input.Text + extension;
                FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft };
                Button confirm = new SoftButton { Text = "이름 변경", Width = 100, Height = 32 };
                Button cancel = new SoftButton { Text = "취소", Width = 80, Height = 32,
                    DialogResult = DialogResult.Cancel };
                confirm.Click += delegate {
                    string error;
                    if (TryRenamePlannedFiles(new Dictionary<TreeNode, string> {
                        { node, input.Text + extension } }, out error)) dialog.DialogResult = DialogResult.OK;
                    else { preview.Text = error; preview.ForeColor = Color.Firebrick; }
                };
                buttons.Controls.Add(confirm); buttons.Controls.Add(cancel);
                layout.Controls.Add(buttons, 0, 3); layout.SetColumnSpan(buttons, 2);
                dialog.AcceptButton = confirm; dialog.CancelButton = cancel;
                SoftUi.Apply(dialog);
                dialog.Shown += delegate { input.SelectAll(); input.Focus(); };
                dialog.ShowDialog(this);
            }
        }

        private static Dictionary<TreeNode, string> BatchNames(IEnumerable<TreeNode> nodes,
            string prefix, string suffix)
        {
            Dictionary<TreeNode, string> names = new Dictionary<TreeNode, string>();
            foreach (TreeNode node in nodes)
                names.Add(node, prefix + Path.GetFileNameWithoutExtension(node.Text) +
                    suffix + Path.GetExtension(node.Text));
            return names;
        }

        private void ShowBatchRename()
        {
            if (!Loaded || loading || working) return;
            List<TreeNode> selected = PlannedSelectedFiles();
            if (selected.Count == 0)
            { status.Text = "이름을 바꿀 파일을 먼저 선택하세요. Ctrl·Shift로 여러 파일을 선택할 수 있습니다."; return; }
            using (Form dialog = new Form {
                Text = "파일 이름 일괄 변경", Width = 560, Height = 370,
                MinimumSize = new Size(500, 340), StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
                MinimizeBox = false, Font = new Font("Malgun Gothic", 9F)
            })
            {
                TableLayoutPanel layout = new TableLayoutPanel {
                    Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 5
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                dialog.Controls.Add(layout);
                layout.Controls.Add(new Label { Text = selected.Count + "개 파일 선택됨 · 확장자는 유지됩니다.",
                    Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
                layout.SetColumnSpan(layout.GetControlFromPosition(0, 0), 2);
                layout.Controls.Add(new Label { Text = "앞에 붙이기", Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
                TextBox prefix = new TextBox { Dock = DockStyle.Fill };
                layout.Controls.Add(prefix, 1, 1);
                layout.Controls.Add(new Label { Text = "뒤에 붙이기", Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
                TextBox suffix = new TextBox { Dock = DockStyle.Fill };
                layout.Controls.Add(suffix, 1, 2);
                ListBox preview = new ListBox { Dock = DockStyle.Fill };
                layout.Controls.Add(preview, 0, 3); layout.SetColumnSpan(preview, 2);
                Action refresh = delegate {
                    preview.Items.Clear();
                    foreach (TreeNode file in selected.Take(8))
                        preview.Items.Add(file.Text + "   →   " + BatchNames(new[] { file },
                            prefix.Text, suffix.Text)[file]);
                    if (selected.Count > 8) preview.Items.Add("…외 " + (selected.Count - 8) + "개 파일");
                };
                prefix.TextChanged += delegate { refresh(); };
                suffix.TextChanged += delegate { refresh(); };
                refresh();
                FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft };
                Button confirm = new SoftButton { Text = "일괄 이름 변경", Width = 125, Height = 32 };
                Button cancel = new SoftButton { Text = "취소", Width = 80, Height = 32,
                    DialogResult = DialogResult.Cancel };
                confirm.Click += delegate {
                    if (prefix.Text == "" && suffix.Text == "")
                    { preview.Items.Insert(0, "앞이나 뒤에 붙일 내용을 입력하세요."); return; }
                    string error;
                    if (TryRenamePlannedFiles(BatchNames(selected, prefix.Text, suffix.Text), out error))
                        dialog.DialogResult = DialogResult.OK;
                    else preview.Items.Insert(0, error);
                };
                buttons.Controls.Add(confirm); buttons.Controls.Add(cancel);
                layout.Controls.Add(buttons, 0, 4); layout.SetColumnSpan(buttons, 2);
                dialog.AcceptButton = confirm; dialog.CancelButton = cancel;
                SoftUi.Apply(dialog);
                dialog.Shown += delegate { prefix.Focus(); };
                dialog.ShowDialog(this);
            }
        }
    }
}
