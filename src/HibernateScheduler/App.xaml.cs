/* ===================================
   App.xaml.cs
   -----------------------------------
   - Startup: single instance, controller, tray icon and main window.
   - "--minimized" starts in the tray only (used by the start-with-Windows entry).
   =================================== */

using System;
using System.Linq;
using System.Threading;
using System.Windows;
using WF = System.Windows.Forms;

namespace HibernateScheduler;

public partial class App : Application
{
    private const string MutexName = @"Local\noissaPStudio.HibernateScheduler";
    private const string ShowEventName = @"Local\noissaPStudio.HibernateScheduler.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private HibernateController? _ctl;
    private MainWindow? _main;
    private WF.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // =============================
        // Part 1 — Single instance
        // =============================
        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var show)) { show.Set(); show.Dispose(); }
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(() =>
        {
            try { while (_showEvent.WaitOne()) Dispatcher.BeginInvoke(ShowMain); }
            catch (ObjectDisposedException) { }   // shutting down
        }) { IsBackground = true }.Start();

        // =============================
        // Part 2 — Controller and tray
        // =============================
        _ctl = new HibernateController();
        _main = new MainWindow(_ctl);
        BuildTray();

        if (!e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase)) ShowMain();
    }

    private void BuildTray()
    {
        var ctl = _ctl!;
        var status = new WF.ToolStripMenuItem { Enabled = false };
        var cancel = new WF.ToolStripMenuItem("Cancel hibernate", null, (_, _) => ctl.Cancel());

        var later = new WF.ToolStripMenuItem("Hibernate in");
        foreach (var mins in new[] { 15, 30, 60, 120 })
        {
            var label = HibernateController.FormatDuration(TimeSpan.FromMinutes(mins));
            later.DropDownItems.Add(label, null, (_, _) => ctl.SetTarget(DateTime.Now.AddMinutes(mins), $"in {label}"));
        }

        var menu = new WF.ContextMenuStrip();
        menu.Items.Add(status);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Open", null, (_, _) => ShowMain());
        menu.Items.Add(later);
        menu.Items.Add(cancel);
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        menu.Opening += (_, _) =>
        {
            status.Text = ctl.StatusText();
            cancel.Enabled = ctl.TargetAt != null || ctl.WarningActive;
        };

        _trayIcon = AppIcon.Create(true);
        _tray = new WF.NotifyIcon { Icon = _trayIcon, ContextMenuStrip = menu, Visible = true, Text = "Hibernate Scheduler" };
        _tray.MouseClick += (_, e) => { if (e.Button == WF.MouseButtons.Left) ToggleMain(); };
        ctl.Notice += msg => _tray.ShowBalloonTip(4000, "Hibernate Scheduler", msg, WF.ToolTipIcon.None);
        ctl.StateChanged += () => _tray.Text = $"Hibernate Scheduler\n{ctl.StatusText()}";
        _tray.Text = $"Hibernate Scheduler\n{ctl.StatusText()}";
    }

    private void ShowMain() => _main?.ShowNearTray();

    private void ToggleMain()
    {
        if (_main == null) return;
        if (_main.IsVisible && _main.IsActive) _main.Hide();
        else ShowMain();
    }

    private void ExitApp()
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _trayIcon?.Dispose();
        _ctl?.Dispose();
        _showEvent?.Dispose();
        _mutex?.Dispose();
        Shutdown();
    }
}
