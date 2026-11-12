using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal static class SingleFolderWatcherSmoke
    {
        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(target);
        }

        [STAThread]
        public static int Main()
        {
            string temp = Path.Combine(Path.GetTempPath(), "SingleFolderWatcher-" + Guid.NewGuid().ToString("N"));
            try
            {
                string root = Path.Combine(temp, "one-folder");
                string budget = Path.Combine(root, "예산");
                string wrong = Path.Combine(root, "기타");
                string data = Path.Combine(temp, "settings");
                Directory.CreateDirectory(budget);
                Directory.CreateDirectory(wrong);
                Directory.CreateDirectory(data);
                File.WriteAllText(Path.Combine(budget, "예산_기존.txt"), "sample");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string failure = "";
                int stage = 0;
                using (MainForm form = new MainForm(data))
                {
                    form.Opacity = 0;
                    Field<TextBox>(form, "sourceBox").Text = root;
                    Field<TextBox>(form, "rootBox").Text = root;
                    Field<CheckBox>(form, "automaticBox").Checked = true;
                    string incoming = Path.Combine(wrong, "예산_새파일.txt");
                    string moved = Path.Combine(budget, "예산_새파일.txt");
                    DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                    Timer monitor = new Timer { Interval = 100 };
                    monitor.Tick += delegate
                    {
                        try
                        {
                            if (DateTime.UtcNow >= deadline)
                                throw new Exception("하위 폴더에서 새 파일을 옮기지 못함");
                            FileSystemWatcher watcher = Field<FileSystemWatcher>(form, "downloadWatcher");
                            if (stage == 0 && watcher != null && !Field<Timer>(form, "timer").Enabled)
                            {
                                if (!watcher.IncludeSubdirectories)
                                    throw new Exception("하위 폴더 감시가 꺼져 있음");
                                File.WriteAllText(incoming, "new");
                                File.SetLastWriteTimeUtc(incoming, DateTime.UtcNow.AddSeconds(-10));
                                stage = 1;
                            }
                            else if (stage == 1 && File.Exists(moved))
                            {
                                stage = 2;
                                monitor.Stop();
                                form.RequestExit();
                            }
                        }
                        catch (Exception ex)
                        {
                            failure = ex.Message;
                            monitor.Stop();
                            Application.ExitThread();
                        }
                    };
                    form.Shown += delegate { monitor.Start(); };
                    Application.Run(form);
                    if (failure != "" || stage != 2 || !File.Exists(moved) || File.Exists(incoming))
                        throw new Exception(failure == "" ? "하위 폴더 자동 정리 실패" : failure);
                }
                Console.WriteLine("PASS: 하위 폴더 새 파일 감지와 자동 분류");
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
