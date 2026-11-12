using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal static class TopicSuggestSmoke
    {
        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static TreeNode FindDeep(TreeNodeCollection nodes, string text)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Text == text) return node;
                TreeNode found = FindDeep(node.Nodes, text);
                if (found != null) return found;
            }
            return null;
        }

        private static StructureEditor Open(string source, string root, string data)
        {
            StructureEditor editor = new StructureEditor(source, root, data);
            editor.Opacity = 0;
            editor.Show();
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (!editor.Loaded && DateTime.UtcNow < deadline)
            { Application.DoEvents(); Thread.Sleep(20); }
            Check(editor.Loaded, "구조 편집기를 열지 못했습니다.");
            return editor;
        }

        private static void Touch(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Path.GetFileName(path));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10));
        }

        [STAThread]
        public static int Main()
        {
            string temp = Path.Combine(Path.GetTempPath(), "TopicSuggestSmoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(temp, "downloads");
                string root = Path.Combine(temp, "sorted");
                string data = Path.Combine(temp, "settings");
                Directory.CreateDirectory(root); Directory.CreateDirectory(data);
                string[] names = {
                    "기관업무혁신시스템 구축.html", "기관업무혁신계획_2026.hwp", "기관업무혁신 추진현황.xlsx",
                    "기관업무혁신 우수사례.pdf", "대학원 학칙_2026.10.02(개정).pdf", "대학원 학칙_2025.hwp",
                    "대학원 학위청구 안내.pdf", "대학원 장학 규정.hwp", "web_inspector_v4_www.mois.go.kr_20261111_1.json",
                    "web_inspector_v4_www.jbnu.ac.kr_20261112.json", "web_inspector_v4_www.mois.go.kr_20261111_2.json",
                    "IMG_0001.jpg", "회의록.txt"
                };
                foreach (string name in names) Touch(Path.Combine(source, name));
                string exam2019 = Path.Combine(source, "2019년도 5급(기술) 제2차시험 문제지");
                string exam2020 = Path.Combine(source, "2020년도 5급(기술) 제2차시험 문제지");
                foreach (string subject in new[] { "행정법", "경제학", "통계학", "물리학" })
                    Touch(Path.Combine(exam2019, subject + ".pdf"));
                foreach (string subject in new[] { "행정법", "경제학", "화학" })
                    Touch(Path.Combine(exam2020, subject + ".pdf"));
                Touch(Path.Combine(exam2019, "정답", "정답표.pdf"));

                List<string> files = Engine.SourceFiles(source, true).Select(f => f.FullName).ToList();
                List<TopicCandidate> suggested = TopicSuggester.Suggest(source, files, new string[0]);
                string lines = String.Join(" | ", suggested.Select(s => s.Line).ToArray());
                Check(suggested.Any(s => s.Line.Contains("기관업무혁신")), "합성어 공통 앞부분 주제를 찾지 못했습니다: " + lines);
                Check(suggested.Any(s => s.Line.Contains("대학원")), "대학원 주제를 찾지 못했습니다: " + lines);
                Check(suggested.Any(s => s.Line.Contains("5급") || s.Line.Contains("문제지") ||
                    s.Line.Contains("제2차시험")), "하위 폴더 이름의 공통 주제를 찾지 못했습니다: " + lines);
                Check(!suggested.Any(s => s.Line.Split(' ', '>').Any(w => w == "www" || w == "kr" || w == "2026")),
                    "의미 없는 낱말이 주제로 나왔습니다: " + lines);
                List<TopicCandidate> withExisting = TopicSuggester.Suggest(source, files, new[] { "대학원" });
                Check(withExisting.Any(s => s.Line == "대학원" && s.MatchesExistingFolder) ||
                    !withExisting.Any(s => s.Line == "대학원"), "기존 정리 폴더와 같은 주제를 표시하지 않았습니다.");

                Check(TopicSuggester.Pick(new[] { "업무", "업무\\예산" }, new[] { 0.8, 0.9 }) == 1,
                    "하위 주제를 우선하지 않았습니다.");
                Check(TopicSuggester.Pick(new[] { "업무", "업무\\예산" }, new[] { 0.95, 0.7 }) == 1,
                    "상위·하위가 함께 맞을 때 하위 주제를 고르지 않았습니다.");
                Check(TopicSuggester.Pick(new[] { "가", "나" }, new[] { 0.8, 0.75 }) == -1,
                    "애매한 두 주제 중 하나를 골랐습니다.");
                Check(TopicSuggester.TopicScore("기관업무혁신계획_2026", "기관업무혁신") >= 0.62,
                    "합성어에 포함된 주제를 맞추지 못했습니다.");

                List<string> merged = StructureEditor.MergeTopicLines(new[] { "대학원", "", "업무 > 예산" },
                    new[] { "대학원", "업무/예산", "기관업무혁신" }, false);
                Check(merged.SequenceEqual(new[] { "대학원", "업무 > 예산", "기관업무혁신" }),
                    "주제 목록 합치기 결과가 다릅니다: " + String.Join(" | ", merged.ToArray()));

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                File.WriteAllText(Path.Combine(data, "topics.txt"), "기관업무혁신\n대학원", Encoding.UTF8);
                using (StructureEditor editor = Open(source, root, data))
                {
                    TreeView proposed = Field<TreeView>(editor, "proposed");
                    TreeNode innovation = proposed.Nodes[0].Nodes.Cast<TreeNode>().First(n => n.Text == "기관업무혁신");
                    Check(innovation.Nodes.Count == 4, "기관업무혁신 주제에 4개 파일이 배치되지 않았습니다: " + innovation.Nodes.Count);
                    Check(proposed.Nodes[1].Text.StartsWith("정리하지 않고 둘 파일"), "보류 영역 이름이 바뀌지 않았습니다.");
                    TreeView current = Field<TreeView>(editor, "current");
                    Check(current.Nodes[1].Text.EndsWith("· 감시 중"), "왼쪽 감시 폴더 이름이 바뀌지 않았습니다.");
                    editor.Close();
                }

                File.WriteAllText(Path.Combine(data, "topics.txt"), "", Encoding.UTF8);
                using (StructureEditor editor = Open(source, root, data))
                {
                    TreeView proposed = Field<TreeView>(editor, "proposed");
                    TreeNode hold = proposed.Nodes[1];
                    TreeNode examFolder = hold.Nodes.Cast<TreeNode>().First(n => n.Text == Path.GetFileName(exam2019));
                    Check(!editor.MovePlannedNode(examFolder, hold), "하위 폴더를 보류 영역 안에서 옮겼습니다.");
                    Check(editor.MovePlannedNode(examFolder, proposed.Nodes[0]), "하위 폴더를 정리 폴더로 끌어 놓지 못했습니다.");
                    TreeNode placed = proposed.Nodes[0].Nodes.Cast<TreeNode>().First(n => n.Text == Path.GetFileName(exam2019));
                    Check(placed.Nodes.Count == 5 && FindDeep(placed.Nodes, "정답표.pdf") != null,
                        "하위 폴더의 파일과 안쪽 폴더가 함께 옮겨지지 않았습니다.");
                    TreeNode empty = hold.Nodes.Cast<TreeNode>().First(n => n.Text == Path.GetFileName(exam2019));
                    Check(!editor.MovePlannedNode(empty, proposed.Nodes[0]), "빈 하위 폴더를 다시 옮겼습니다.");
                    PlanData plan = (PlanData)typeof(StructureEditor)
                        .GetMethod("ReadPlan", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(editor, null);
                    StructurePlanner.Apply(root, source, Field<StructureSnapshot>(editor, "snapshot"),
                        plan.Files, plan.Directories);
                    string moved = Path.Combine(root, Path.GetFileName(exam2019));
                    Check(File.Exists(Path.Combine(moved, "행정법.pdf")) &&
                        File.Exists(Path.Combine(moved, "정답", "정답표.pdf")), "하위 폴더 이동이 적용되지 않았습니다.");
                    Check(!File.Exists(Path.Combine(exam2019, "행정법.pdf")) &&
                        File.Exists(Path.Combine(exam2020, "행정법.pdf")), "다른 하위 폴더 파일까지 옮겼거나 원본이 남았습니다.");
                    editor.Close();
                }
                Console.WriteLine("PASS: 주제 추천, 하위 주제 우선 배치, 목록 합치기, 하위 폴더 통째로 이동, 표시 이름");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
            finally
            {
                if (temp.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(temp)) Directory.Delete(temp, true);
            }
        }
    }
}
