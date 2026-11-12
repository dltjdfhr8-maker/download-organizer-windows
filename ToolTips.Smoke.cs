using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    internal static class ToolTipsSmoke
    {
        private static ToolTip Tips(Form form)
        {
            return (ToolTip)form.GetType().GetField("helpTips",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control nested in Descendants(child)) yield return nested;
            }
        }

        private static void Check(Form form)
        {
            ToolTip tips = Tips(form);
            int checkedCount = 0;
            foreach (Control control in Descendants(form))
            {
                if (!(control is ButtonBase) && !(control is TextBox) &&
                    !(control is ComboBox) && !(control is ListView) &&
                    !(control is TreeView)) continue;
                if (String.IsNullOrWhiteSpace(tips.GetToolTip(control)))
                    throw new Exception(form.Text + ": missing help on " +
                        control.GetType().Name + " " + control.Text);
                checkedCount++;
            }
            if (checkedCount < 5 || tips.InitialDelay > 600 || tips.IsBalloon)
                throw new Exception(form.Text + ": hover help is not configured");
        }

        public static int Main()
        {
            string testRoot = Path.Combine(Path.GetTempPath(), "DownloadOrganizerTips-" + Guid.NewGuid().ToString("N"));
            try
            {
                string source = Path.Combine(testRoot, "downloads");
                string root = Path.Combine(testRoot, "sorted");
                string data = Path.Combine(testRoot, "settings");
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(data);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (MainForm main = new MainForm(data)) Check(main);
                using (StructureEditor structure = new StructureEditor(source, root, data)) Check(structure);
                using (HistoryForm history = new HistoryForm(new ChangeJournal(), delegate { })) Check(history);
                Console.WriteLine("PASS: hover help covers controls in all three windows");
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
