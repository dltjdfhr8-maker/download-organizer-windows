using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal static class FlickerSmoke
    {
        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(target);
        }

        public static int Main()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "DownloadOrganizerFlicker-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(testRoot, "downloads");
                string root = Path.Combine(testRoot, "sorted");
                string data = Path.Combine(testRoot, "settings");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(Path.Combine(root, "예산"));
                Directory.CreateDirectory(Path.Combine(root, "회의"));
                Directory.CreateDirectory(data);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (MainForm form = new MainForm(data))
                {
                    form.Opacity = 0;
                    Field<TextBox>(form, "sourceBox").Text = source;
                    Field<TextBox>(form, "rootBox").Text = root;
                    Button sourceButton = Field<Button>(form, "sourceButton");
                    ComboBox folderBox = Field<ComboBox>(form, "folderBox");
                    Label status = Field<Label>(form, "status");
                    int buttonChanges = 0, folderChanges = 0, statusChanges = 0;
                    sourceButton.EnabledChanged += delegate { buttonChanges++; };
                    folderBox.SelectedIndexChanged += delegate { folderChanges++; };
                    status.TextChanged += delegate { statusChanges++; };
                    Field<CheckBox>(form, "automaticBox").Checked = true;
                    bool monitoring = false;
                    DateTime monitorStart = DateTime.MinValue;
                    DateTime deadline = DateTime.UtcNow.AddSeconds(12);
                    Timer monitor = new Timer { Interval = 100 };
                    monitor.Tick += delegate
                    {
                        if (!monitoring && folderBox.Items.Count == 3)
                        {
                            folderBox.SelectedIndex = 0;
                            monitoring = true;
                            monitorStart = DateTime.UtcNow;
                            folderChanges = 0;
                            statusChanges = 0;
                        }
                        if ((monitoring && (DateTime.UtcNow - monitorStart).TotalSeconds >= 5) ||
                            DateTime.UtcNow >= deadline)
                        {
                            monitor.Stop();
                            form.GetType().GetMethod("RequestExit", BindingFlags.Instance |
                                BindingFlags.NonPublic).Invoke(form, null);
                        }
                    };
                    form.Shown += delegate { monitor.Start(); };
                    Application.Run(form);
                    if (!monitoring || buttonChanges != 0 || folderChanges != 0 || statusChanges != 0 ||
                        folderBox.SelectedIndex != 0)
                        throw new Exception("idle automatic checks redrew controls: button=" + buttonChanges +
                            ", folder=" + folderChanges + ", status=" + statusChanges);
                }
                Console.WriteLine("PASS: repeated automatic checks keep controls and selection stable");
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
