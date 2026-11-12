using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal static class TraySmoke
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
            string testRoot = Path.Combine(Path.GetTempPath(), "DownloadOrganizerTray-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(testRoot, "downloads");
                string root = Path.Combine(testRoot, "sorted");
                string destinationFolder = Path.Combine(root, "예산");
                string data = Path.Combine(testRoot, "settings");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(destinationFolder);
                Directory.CreateDirectory(data);
                File.WriteAllText(Path.Combine(destinationFolder, "예산_기존.txt"), "sample");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                string failure = "";
                using (MainForm form = new MainForm(data))
                {
                    form.Opacity = 0;
                    Field<TextBox>(form, "sourceBox").Text = source;
                    Field<TextBox>(form, "rootBox").Text = root;
                    Field<CheckBox>(form, "automaticBox").Checked = true;
                    string incoming = Path.Combine(source, "예산_새파일.txt");
                    string moved = Path.Combine(destinationFolder, "예산_새파일.txt");
                    string afterClose = Path.Combine(source, "예산_닫은후.txt");
                    string movedAfterClose = Path.Combine(destinationFolder, "예산_닫은후.txt");
                    DateTime deadline = DateTime.UtcNow.AddSeconds(25);
                    int stage = 0;
                    Timer monitor = new Timer { Interval = 100 };
                    monitor.Tick += delegate
                    {
                        try
                        {
                            if (DateTime.UtcNow >= deadline)
                                throw new Exception("watcher did not complete the tray flow");
                            if (stage == 0 && Field<FileSystemWatcher>(form, "downloadWatcher") != null &&
                                !Field<Timer>(form, "timer").Enabled)
                            {
                                if (!Field<Timer>(form, "fallbackTimer").Enabled)
                                    throw new Exception("fallback check is not active");
                                form.WindowState = FormWindowState.Minimized;
                                if (form.Visible || form.ShowInTaskbar ||
                                    !Field<NotifyIcon>(form, "trayIcon").Visible)
                                    throw new Exception("minimize did not hide to the tray");
                                File.WriteAllText(incoming, "new");
                                File.SetLastWriteTimeUtc(incoming, DateTime.UtcNow.AddSeconds(-10));
                                stage = 1;
                            }
                            else if (stage == 1 && File.Exists(moved))
                            {
                                form.RestoreFromTray();
                                if (!form.Visible || !form.ShowInTaskbar ||
                                    form.WindowState != FormWindowState.Normal)
                                    throw new Exception("tray restore failed");
                                form.Close();
                                if (form.IsDisposed || form.Visible)
                                    throw new Exception("close did not keep the app in the tray");
                                File.WriteAllText(afterClose, "still running");
                                File.SetLastWriteTimeUtc(afterClose, DateTime.UtcNow.AddSeconds(-10));
                                stage = 2;
                            }
                            else if (stage == 2 && File.Exists(movedAfterClose))
                            {
                                stage = 3;
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
                    if (failure != "") throw new Exception(failure);
                    if (stage != 3 || !File.Exists(moved) || !File.Exists(movedAfterClose))
                        throw new Exception("tray flow stopped early");
                }
                Console.WriteLine("PASS: idle watcher, tray hide/restore, file move, explicit exit");
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
