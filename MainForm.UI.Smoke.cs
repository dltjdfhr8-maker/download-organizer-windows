using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal static class MainUiSmoke
    {
        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(target);
        }

        public static int Main(string[] args)
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "DownloadOrganizerUi-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(testRoot, "downloads");
                string root = Path.Combine(testRoot, "sorted");
                string data = Path.Combine(testRoot, "settings");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(data);
                for (int i = 0; i < 80; i++)
                {
                    string folder = Path.Combine(root, "주제" + i);
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "주제" + i + "_기존.txt"), "sample");
                }
                for (int i = 0; i < 400; i++)
                    File.WriteAllText(Path.Combine(source, "주제" + (i % 80) + "_새파일" + i + ".txt"), "sample");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (MainForm form = new MainForm(data))
                {
                    form.Opacity = 0;
                    Field<TextBox>(form, "sourceBox").Text = source;
                    Field<TextBox>(form, "rootBox").Text = root;
                    ListView list = Field<ListView>(form, "list");
                    int ticks = 0;
                    DateTime started = DateTime.UtcNow;
                    Timer timer = new Timer { Interval = 50 };
                    timer.Tick += delegate
                    {
                        ticks++;
                        if (list.VirtualListSize == 400 || (DateTime.UtcNow - started).TotalSeconds > 15)
                        {
                            timer.Stop();
                            if (args.Length > 0 && list.VirtualListSize == 400)
                                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                                {
                                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                                    bitmap.Save(args[0]);
                                }
                            form.GetType().GetMethod("RequestExit", BindingFlags.Instance |
                                BindingFlags.NonPublic).Invoke(form, null);
                        }
                    };
                    form.Shown += delegate { timer.Start(); };
                    Application.Run(form);
                    if (list.VirtualListSize != 400 || ticks == 0)
                        throw new Exception("preview did not load responsively: rows=" + list.VirtualListSize + ", ticks=" + ticks);
                }
                Console.WriteLine("PASS: main window remains responsive while 400 files are previewed");
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
