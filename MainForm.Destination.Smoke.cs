using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    // The selected-file destination box: it must list the target root itself, so a file can be
    // moved by hand even before the root has subfolders, and it must pick up suggestions.
    internal static class DestinationSmoke
    {
        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static void Touch(string path)
        {
            File.WriteAllText(path, Path.GetFileName(path));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
        }

        private static string Run(string source, string root, string data, Func<MainForm, int, string> step)
        {
            string failure = "";
            using (MainForm form = new MainForm(data))
            {
                form.Opacity = 0;
                Field<TextBox>(form, "sourceBox").Text = source;
                Field<TextBox>(form, "rootBox").Text = root;
                int stage = 0;
                DateTime deadline = DateTime.UtcNow.AddSeconds(20);
                Timer timer = new Timer { Interval = 200 };
                timer.Tick += delegate
                {
                    try
                    {
                        if (DateTime.UtcNow > deadline) throw new Exception("시간 초과 (단계 " + stage + ")");
                        string next = step(form, stage);
                        if (next == "wait") return;
                        if (next == "done")
                        {
                            timer.Stop();
                            form.GetType().GetMethod("RequestExit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
                            return;
                        }
                        stage++;
                    }
                    catch (Exception ex)
                    {
                        failure = ex.Message; timer.Stop();
                        form.GetType().GetMethod("RequestExit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
                    }
                };
                form.Shown += delegate { timer.Start(); };
                Application.Run(form);
            }
            return failure;
        }

        [STAThread]
        public static int Main()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "DestinationSmoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                MethodInfo appMain = typeof(Program).GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
                if (appMain == null || appMain.GetCustomAttributes(typeof(STAThreadAttribute), false).Length == 0)
                    throw new Exception("앱의 Main에 [STAThread]가 없어 드래그·놓기 창을 열 수 없습니다.");
                string source = Path.Combine(testRoot, "downloads");
                string root = Path.Combine(testRoot, "교육자료");
                string data = Path.Combine(testRoot, "settings");
                Directory.CreateDirectory(source); Directory.CreateDirectory(root); Directory.CreateDirectory(data);
                Touch(Path.Combine(source, "아무거나.txt"));
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                string failure = Run(source, root, data, (form, stage) =>
                {
                    ListView list = Field<ListView>(form, "list");
                    ComboBox box = Field<ComboBox>(form, "folderBox");
                    if (stage == 0)
                    {
                        if (list.VirtualListSize != 1 || !Field<Button>(form, "moveButton").Enabled) return "wait";
                        if (box.Items.Count != 1 || !((FolderChoice)box.Items[0]).Path.Equals(Engine.Full(root),
                            StringComparison.OrdinalIgnoreCase))
                            throw new Exception("하위 폴더가 없을 때 목적지에 상위 폴더가 없습니다: " + box.Items.Count);
                        if (box.FlatStyle != FlatStyle.System) throw new Exception("목적지 칸 테두리가 보이지 않습니다.");
                        list.SelectedIndices.Add(0);
                        box.SelectedIndex = 0;
                        Field<Button>(form, "moveButton").PerformClick();
                        return "next";
                    }
                    if (!File.Exists(Path.Combine(root, "아무거나.txt"))) return "wait";
                    return "done";
                });
                if (failure != "") throw new Exception(failure);
                if (File.Exists(Path.Combine(source, "아무거나.txt")))
                    throw new Exception("빈 상위 폴더로 직접 옮기지 못했습니다.");

                Directory.CreateDirectory(Path.Combine(root, "예산"));
                File.WriteAllText(Path.Combine(root, "예산", "예산_기존.txt"), "sample");
                Touch(Path.Combine(source, "예산_2026.txt"));
                failure = Run(source, root, data, (form, stage) =>
                {
                    ListView list = Field<ListView>(form, "list");
                    ComboBox box = Field<ComboBox>(form, "folderBox");
                    if (list.VirtualListSize != 1 || box.Items.Count != 2 || !Field<Button>(form, "moveButton").Enabled)
                        return "wait";
                    list.SelectedIndices.Add(0);
                    if (box.SelectedIndex != 1 || box.Text != "예산")
                        throw new Exception("추천 폴더가 목적지 칸에 채워지지 않았습니다: '" + box.Text + "'");
                    return "done";
                });
                if (failure != "") throw new Exception(failure);
                Console.WriteLine("PASS: 목적지 칸 테두리, 상위 폴더 직접 선택, 하위 폴더 없을 때 이동, 추천 폴더 자동 선택");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
            finally
            {
                if (testRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
            }
        }
    }
}
