using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal static class HistorySmoke
    {
        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(target);
        }

        public static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                ChangeJournal journal = new ChangeJournal();
                journal.AddFile(Path.Combine(Path.GetTempPath(), "original.txt"),
                    Path.Combine(Path.GetTempPath(), "new.txt"), 4, 1, "now", "test", "");
                journal.Records.Add(new ChangeRecord {
                    Id = Guid.NewGuid().ToString("N"), Kind = "폴더 생성",
                    To = Path.Combine(Path.GetTempPath(), "example"), When = "now"
                });
                using (HistoryForm form = new HistoryForm(journal, delegate { }))
                {
                    form.Opacity = 0;
                    form.Show();
                    Application.DoEvents();
                    ListView list = Field<ListView>(form, "list");
                    ComboBox filter = Field<ComboBox>(form, "filter");
                    TextBox search = Field<TextBox>(form, "search");
                    if (list.VirtualListSize != 2) throw new Exception("initial history count");
                    if (args.Length > 0)
                        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                            bitmap.Save(args[0]);
                        }
                    filter.SelectedIndex = 2;
                    Application.DoEvents();
                    if (list.VirtualListSize != 1) throw new Exception("folder filter");
                    search.Text = "no match";
                    Application.DoEvents();
                    if (list.VirtualListSize != 0) throw new Exception("search filter");
                    search.Text = "example";
                    Application.DoEvents();
                    if (list.VirtualListSize != 1) throw new Exception("history search");
                    form.Close();
                }
                Console.WriteLine("PASS: history window, virtual rows, file/folder filter, search");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        }
    }
}
