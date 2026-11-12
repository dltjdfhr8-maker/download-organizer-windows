using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DownloadOrganizer
{
    public partial class MainForm
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr handle);

        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem trayAutomaticItem;
        private FileSystemWatcher downloadWatcher;
        private string watchedPath = "";
        private readonly System.Windows.Forms.Timer fallbackTimer = new System.Windows.Forms.Timer();
        private int watcherSignalQueued, watcherErrorQueued, pendingRetries;
        private DateTime lastWatcherRecoveryUtc = DateTime.MinValue;
        private bool monitoringSuspended, pendingAfterModal;
        private bool exitRequested, exitAfterWork;
        private bool trayResourcesDisposed;

        private void InitializeTrayAndMonitoring()
        {
            timer.Tick += AutomaticTimerTick;
            fallbackTimer.Interval = 10 * 60 * 1000;
            fallbackTimer.Tick += delegate
            {
                if (!automaticBox.Checked || monitoringSuspended || exitRequested)
                { pendingAfterModal = monitoringSuspended; return; }
                TryEnsureWatcher();
                ScheduleAutomaticScan(200);
            };

            trayMenu = new ContextMenuStrip { ShowItemToolTips = true };
            ToolStripMenuItem open = new ToolStripMenuItem("창 열기") {
                ToolTipText = "다운로드 자동 정리 창을 다시 엽니다."
            };
            open.Click += delegate { RestoreFromTray(); };
            ToolStripMenuItem preview = new ToolStripMenuItem("미리보기 새로고침") {
                ToolTipText = "창을 열고 현재 파일의 추천 위치를 다시 표시합니다."
            };
            preview.Click += delegate { RestoreFromTray(); BeginPreview(); };
            trayAutomaticItem = new ToolStripMenuItem("새 파일 자동 이동") {
                ToolTipText = "새 다운로드 파일의 자동 이동을 켜거나 끕니다."
            };
            trayAutomaticItem.Click += delegate { automaticBox.Checked = !automaticBox.Checked; };
            ToolStripMenuItem exit = new ToolStripMenuItem("프로그램 종료") {
                ToolTipText = "자동 확인을 멈추고 프로그램을 완전히 종료합니다."
            };
            exit.Click += delegate { RequestExit(); };
            trayMenu.Items.Add(open);
            trayMenu.Items.Add(preview);
            trayMenu.Items.Add(trayAutomaticItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(exit);
            trayIcon = new NotifyIcon {
                Icon = Icon,
                Text = "다운로드 자동 정리",
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            trayIcon.DoubleClick += delegate { RestoreFromTray(); };
            Resize += delegate {
                if (WindowState == FormWindowState.Minimized) HideToTray();
            };
            Disposed += delegate { DisposeTrayResources(); };
            SyncTrayState();
        }

        private void SyncTrayState()
        {
            if (trayAutomaticItem == null || trayIcon == null) return;
            trayAutomaticItem.Checked = automaticBox.Checked;
            trayIcon.Text = automaticBox.Checked ?
                "다운로드 자동 정리 - 자동 이동 켜짐" : "다운로드 자동 정리 - 자동 이동 꺼짐";
        }

        private void HideToTray()
        {
            if (exitRequested || IsDisposed) return;
            Hide();
            ShowInTaskbar = false;
            if (trayIcon != null) trayIcon.Visible = true;
        }

        internal void RestoreFromTray()
        {
            if (IsDisposed) return;
            ShowInTaskbar = true;
            if (!Visible) Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
            if (IsHandleCreated) SetForegroundWindow(Handle);
        }

        internal void RequestExit()
        {
            exitRequested = true;
            CancelAutomaticScan();
            StopAutomaticMonitoring();
            if (scanBusy && scanMovesFiles)
            {
                exitAfterWork = true;
                status.Text = "진행 중인 파일 이동이 끝나면 종료합니다.";
                return;
            }
            Close();
        }

        private bool FinishPendingExit()
        {
            if (!exitAfterWork || scanBusy) return false;
            exitAfterWork = false;
            Close();
            return true;
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.WindowsShutDown ||
                e.CloseReason == CloseReason.TaskManagerClosing ||
                e.CloseReason == CloseReason.ApplicationExitCall)
                exitRequested = true;
            if (!exitRequested)
            {
                e.Cancel = true;
                HideToTray();
                Save();
                return;
            }
            if (scanBusy && scanMovesFiles && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                exitAfterWork = true;
                return;
            }
            Save();
            DisposeTrayResources();
        }

        private void DisposeTrayResources()
        {
            if (trayResourcesDisposed) return;
            trayResourcesDisposed = true;
            StopAutomaticMonitoring();
            if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); }
            if (trayMenu != null) trayMenu.Dispose();
            fallbackTimer.Dispose();
            timer.Dispose();
        }

        private void StartAutomaticMonitoring()
        {
            if (!automaticBox.Checked || !IsHandleCreated || exitRequested) return;
            TryEnsureWatcher();
            fallbackTimer.Start();
            ScheduleAutomaticScan(200);
        }

        private void StopAutomaticMonitoring()
        {
            timer.Stop();
            fallbackTimer.Stop();
            DisposeWatcher();
            pendingRetries = 0;
            pendingAfterModal = false;
        }

        private void SuspendAutomaticMonitoring()
        {
            monitoringSuspended = true;
            if (timer.Enabled) { pendingAfterModal = true; timer.Stop(); }
        }

        private void ResumeAutomaticMonitoring()
        {
            monitoringSuspended = false;
            if (pendingAfterModal && automaticBox.Checked)
            {
                pendingAfterModal = false;
                ScheduleAutomaticScan(200);
            }
        }

        private void ScheduleAutomaticScan(int delayMilliseconds)
        {
            if (!automaticBox.Checked || exitRequested) return;
            if (monitoringSuspended) { pendingAfterModal = true; return; }
            timer.Stop();
            timer.Interval = Math.Max(100, delayMilliseconds);
            timer.Start();
        }

        private void AutomaticTimerTick(object sender, EventArgs e)
        {
            timer.Stop();
            if (!automaticBox.Checked || monitoringSuspended || exitRequested) return;
            if (scanBusy)
            {
                timer.Interval = 1000;
                timer.Start();
                return;
            }
            BeginAutomaticScan();
        }

        private void SchedulePendingRetry(int pending)
        {
            if (!automaticBox.Checked || exitRequested) return;
            if (pending <= 0) { pendingRetries = 0; return; }
            pendingRetries = Math.Min(5, pendingRetries + 1);
            int delay = Math.Min(30000, 2000 * (1 << (pendingRetries - 1)));
            ScheduleAutomaticScan(delay);
        }

        private void TryEnsureWatcher()
        {
            try
            {
                string path = Engine.Full(sourceBox.Text.Trim());
                if (downloadWatcher != null && watchedPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                    return;
                DisposeWatcher();
                FileSystemWatcher watcher = new FileSystemWatcher(path) {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                        NotifyFilters.LastWrite | NotifyFilters.Size
                };
                watcher.Created += WatchedFileEvent;
                watcher.Changed += WatchedFileEvent;
                watcher.Renamed += WatchedRenameEvent;
                watcher.Error += WatcherFailed;
                downloadWatcher = watcher;
                watchedPath = path;
                watcher.EnableRaisingEvents = true;
                fallbackTimer.Interval = 10 * 60 * 1000;
            }
            catch (Exception ex)
            {
                DisposeWatcher();
                fallbackTimer.Interval = 60 * 1000;
                status.Text = "파일 감시를 시작하지 못했습니다. 주기 확인으로 계속 시도합니다: " + ex.Message;
            }
        }

        private void DisposeWatcher()
        {
            if (downloadWatcher != null)
            {
                downloadWatcher.EnableRaisingEvents = false;
                downloadWatcher.Dispose();
                downloadWatcher = null;
            }
            watchedPath = "";
        }

        private void WatchedFileEvent(object sender, FileSystemEventArgs e)
        {
            QueueWatcherSignal();
        }

        private void WatchedRenameEvent(object sender, RenamedEventArgs e)
        {
            QueueWatcherSignal();
        }

        private void QueueWatcherSignal()
        {
            if (Interlocked.Exchange(ref watcherSignalQueued, 1) != 0) return;
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    Interlocked.Exchange(ref watcherSignalQueued, 0);
                    if (IsDisposed || !automaticBox.Checked || exitRequested) return;
                    pendingRetries = 0;
                    ScheduleAutomaticScan(2000);
                }));
            }
            catch { Interlocked.Exchange(ref watcherSignalQueued, 0); }
        }

        private void WatcherFailed(object sender, ErrorEventArgs e)
        {
            if (Interlocked.Exchange(ref watcherErrorQueued, 1) != 0) return;
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    Interlocked.Exchange(ref watcherErrorQueued, 0);
                    if (IsDisposed || exitRequested) return;
                    DisposeWatcher();
                    fallbackTimer.Interval = 60 * 1000;
                    if ((DateTime.UtcNow - lastWatcherRecoveryUtc).TotalSeconds >= 30)
                    {
                        lastWatcherRecoveryUtc = DateTime.UtcNow;
                        TryEnsureWatcher();
                    }
                    ScheduleAutomaticScan(200);
                }));
            }
            catch { Interlocked.Exchange(ref watcherErrorQueued, 0); }
        }
    }
}
