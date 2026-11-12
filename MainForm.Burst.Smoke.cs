using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal static class BurstSmoke
    {
        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(target);
        }

        private static void Exit(MainForm form)
        {
            form.GetType().GetMethod("RequestExit", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, null);
        }

        public static int Main()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "DownloadOrganizerBurst-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(testRoot, "downloads");
                string nested = Path.Combine(source, "받은 파일 묶음");
                string root = Path.Combine(testRoot, "sorted");
                string budget = Path.Combine(root, "예산");
                string meetings = Path.Combine(root, "회의록");
                string data = Path.Combine(testRoot, "settings");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(nested);
                Directory.CreateDirectory(budget);
                Directory.CreateDirectory(meetings);
                Directory.CreateDirectory(data);
                File.WriteAllText(Path.Combine(budget, "예산_기존.txt"), "sample");
                File.WriteAllText(Path.Combine(meetings, "회의록_기존.txt"), "sample");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string failure = "";
                int stage = 0;
                using (MainForm form = new MainForm(data))
                {
                    form.Opacity = 0;
                    Field<TextBox>(form, "sourceBox").Text = source;
                    Field<TextBox>(form, "rootBox").Text = root;
                    Field<CheckBox>(form, "automaticBox").Checked = true;
                    DateTime deadline = DateTime.UtcNow.AddSeconds(35);
                    Timer monitor = new Timer { Interval = 100 };
                    monitor.Tick += delegate
                    {
                        try
                        {
                            if (DateTime.UtcNow >= deadline)
                                throw new Exception("100 files did not finish: budget=" +
                                    Directory.GetFiles(budget, "*.txt").Length + ", meetings=" +
                                    Directory.GetFiles(meetings, "*.txt").Length + ", source=" +
                                    Directory.GetFiles(source, "*.txt", SearchOption.AllDirectories).Length);
                            if (stage == 0 && Field<FileSystemWatcher>(form, "downloadWatcher") != null &&
                                !Field<Timer>(form, "timer").Enabled)
                            {
                                if (!Field<FileSystemWatcher>(form, "downloadWatcher").IncludeSubdirectories)
                                    throw new Exception("nested source folders are not watched");
                                for (int i = 0; i < 50; i++)
                                {
                                    foreach (string topic in new string[] { "예산", "회의록" })
                                    {
                                        string final = Path.Combine(i % 2 == 0 ? nested : source,
                                            topic + "_" + i.ToString("D3") + ".txt");
                                        string temporary = final + ".crdownload";
                                        File.WriteAllText(temporary, topic + i);
                                        File.SetLastWriteTimeUtc(temporary, DateTime.UtcNow.AddSeconds(-10));
                                        File.Move(temporary, final);
                                    }
                                }
                                stage = 1;
                            }
                            else if (stage == 1 && Directory.GetFiles(budget, "*.txt").Length == 26 &&
                                Directory.GetFiles(meetings, "*.txt").Length == 26)
                            {
                                stage = 2;
                                monitor.Stop();
                                Exit(form);
                            }
                        }
                        catch (Exception ex)
                        {
                            failure = ex.InnerException == null ? ex.Message : ex.InnerException.Message;
                            monitor.Stop();
                            Application.ExitThread();
                        }
                    };
                    form.Shown += delegate { monitor.Start(); };
                    Application.Run(form);
                }
                if (failure != "") throw new Exception(failure);
                if (stage != 2 || Directory.GetFiles(source).Length != 0)
                    throw new Exception("files remain in downloads");
                if (Directory.GetFiles(nested, "*.txt").Length != 50 ||
                    Directory.GetFiles(budget, "*.txt").Length != 26 ||
                    Directory.GetFiles(meetings, "*.txt").Length != 26)
                    throw new Exception("watch-subfolder files were moved automatically: nested=" +
                        Directory.GetFiles(nested, "*.txt").Length);
                Console.WriteLine("PASS: 100 rapid downloads; top-level files sorted, watch-subfolder files left in place");
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
