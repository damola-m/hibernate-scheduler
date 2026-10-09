/* ===================================
   MainWindow.xaml.cs
   -----------------------------------
   - Compact flyout: set one hibernate (after a while / at a time), watch it count down, cancel it.
   - Settings view behind the gear: startup, warning length, OneDrive remote trigger.
   =================================== */

using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HibernateScheduler;

public partial class MainWindow : Window
{
    // ==========================================
    // FIELDS
    // ==========================================

    private static readonly int[] QuickMinutes = [30, 60, 120];

    private readonly HibernateController _ctl;
    private bool _ready;
    private bool _syncing;
    private bool _inSettings;
    private bool _anchored;

    internal MainWindow(HibernateController ctl)
    {
        _ctl = ctl;
        InitializeComponent();
        NativeChrome.Apply(this);
        Icon = AppIcon.CreateImage();

        // =============================
        // Part 1 — Quick chips
        // =============================
        foreach (var mins in QuickMinutes)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"),
                GroupName = "quick",
                Content = HibernateController.FormatDuration(TimeSpan.FromMinutes(mins)),
                Tag = mins,
            };
            chip.Checked += Chip_Checked;
            Chips.Children.Add(chip);
        }

        // =============================
        // Part 2 — Wire up
        // =============================
        AtTime.Text = DateTime.Now.AddHours(1).ToString("HH:00");
        _ready = true;
        SyncChips();

        _ctl.StateChanged += Refresh;
        _ctl.Tick += OnTick;
        SizeChanged += (_, _) => { if (_anchored) PinToCorner(); };
        Deactivated += (_, _) => CommitFields();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Hide();
            if (e.Key == Key.Enter && SetPanel.Visibility == Visibility.Visible && SetBtn.IsEnabled) Set_Click(this, new RoutedEventArgs());
        };
        Closing += (_, e) => { e.Cancel = true; Hide(); };
        Refresh();
    }

    // ==========================================
    // WINDOW PLACEMENT
    // ==========================================

    public void ShowNearTray()
    {
        _anchored = true;
        Show();
        PinToCorner();

        // Windows blocks focus-stealing from tray apps; a topmost flick brings it forward anyway
        Topmost = true;
        Activate();
        Topmost = false;
        Refresh();
    }

    private void PinToCorner()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 12;
        Top = area.Bottom - ActualHeight - 12;
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        _anchored = false;
        DragMove();
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    // ==========================================
    // VIEW STATE
    // ==========================================

    private void Refresh()
    {
        // =============================
        // Part 1 — Which panel
        // =============================
        var active = _ctl.TargetAt != null || _ctl.WarningActive;
        SettingsPanel.Visibility = _inSettings ? Visibility.Visible : Visibility.Collapsed;
        SetPanel.Visibility = !_inSettings && !active ? Visibility.Visible : Visibility.Collapsed;
        ActivePanel.Visibility = !_inSettings && active ? Visibility.Visible : Visibility.Collapsed;

        TitleText.Text = _inSettings ? "Settings" : "Hibernate";
        SettingsBtn.Content = _inSettings ? "" : "";
        SettingsBtn.ToolTip = _inSettings ? "Back" : "Settings";
        RemoteDot.Visibility = _ctl.RemoteReady ? Visibility.Visible : Visibility.Collapsed;

        // =============================
        // Part 2 — Settings values
        // =============================
        var s = _ctl.Settings;
        StartupSwitch.IsChecked = StartupRegistration.Enabled;
        if (!WarnSeconds.IsKeyboardFocused) WarnSeconds.Text = s.CountdownSeconds.ToString();
        RemoteSwitch.IsChecked = s.RemoteEnabled;
        RemoteDetails.Visibility = s.RemoteEnabled ? Visibility.Visible : Visibility.Collapsed;

        var folder = _ctl.RemoteFolder ?? "";
        RemotePath.Text = folder.Length > 0 ? folder : "OneDrive folder not found";
        RemotePath.ToolTip = folder;
        RemoteHelp.Text = _ctl.RemoteReady
            ? "From your phone, open this folder in the OneDrive app and create a folder named hibernate, hibernate 30, hibernate at 2300 or cancel. The reply appears in status.txt."
            : "Couldn't create the folder. Pick your OneDrive location with the pencil button.";

        UpdatePreview();
        OnTick();
    }

    private void OnTick()
    {
        if (ActivePanel.Visibility != Visibility.Visible) return;

        if (_ctl.WarningActive)
        {
            Remaining.Text = "now";
            TargetText.Text = "Countdown is on screen";
            ExtendBtn.IsEnabled = false;
            return;
        }
        if (_ctl.TargetAt is not { } at) return;

        Remaining.Text = HibernateController.FormatSpan(at - DateTime.Now);
        TargetText.Text = $"{Capitalise(HibernateController.Format(at))}  ·  {_ctl.Reason}";
        ExtendBtn.IsEnabled = true;
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    // ==========================================
    // SET PANEL
    // ==========================================

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        InPanel.Visibility = ModeIn.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AtPanel.Visibility = ModeAt.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreview();
    }

    private void Chip_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton { Tag: int mins }) return;
        _syncing = true;
        Hours.Text = (mins / 60).ToString();
        Minutes.Text = (mins % 60).ToString();
        _syncing = false;
        UpdatePreview();
    }

    private void Input_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _syncing) return;
        SyncChips();
        UpdatePreview();
    }

    // Highlight the chip that matches the typed duration, if any
    private void SyncChips()
    {
        var total = ReadDuration();
        _syncing = true;
        foreach (RadioButton chip in Chips.Children)
            chip.IsChecked = total is { } t && (int)t.TotalMinutes == (int)chip.Tag;
        _syncing = false;
    }

    private TimeSpan? ReadDuration()
    {
        var hOk = int.TryParse(string.IsNullOrWhiteSpace(Hours.Text) ? "0" : Hours.Text.Trim(), out var h);
        var mOk = int.TryParse(string.IsNullOrWhiteSpace(Minutes.Text) ? "0" : Minutes.Text.Trim(), out var m);
        if (!hOk || !mOk || h < 0 || m < 0 || h > 72) return null;
        var t = TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m);
        return t > TimeSpan.Zero ? t : null;
    }

    private DateTime? ReadAtTime()
    {
        var match = Regex.Match(AtTime.Text.Trim(), @"^(\d{1,2})[:.\s]?(\d{2})?$");
        if (!match.Success) return null;
        var h = int.Parse(match.Groups[1].Value);
        var m = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        if (h > 23 || m > 59) return null;
        return HibernateController.NextOccurrence(h, m);
    }

    private DateTime? ReadTarget()
    {
        if (ModeAt.IsChecked == true) return ReadAtTime();
        return ReadDuration() is { } d ? DateTime.Now + d : null;
    }

    private void UpdatePreview()
    {
        if (!_ready) return;
        var target = ReadTarget();
        SetBtn.IsEnabled = target != null;

        if (target is not { } at)
        {
            Preview.Text = ModeAt.IsChecked == true ? "Enter a time like 22:30" : "Enter a duration";
            return;
        }
        Preview.Text = ModeAt.IsChecked == true
            ? $"{Capitalise(HibernateController.Format(at))}, in {HibernateController.FormatDuration(at - DateTime.Now)}"
            : $"Hibernates {HibernateController.Format(at)}";
    }

    private void Set_Click(object sender, RoutedEventArgs e)
    {
        if (ReadTarget() is not { } at) return;
        var reason = ModeAt.IsChecked == true
            ? "set for a time"
            : $"in {HibernateController.FormatDuration(at - DateTime.Now)}";
        _ctl.SetTarget(at, reason);
    }

    private void Now_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _ = _ctl.HibernateNowAsync("manual");
    }

    // ==========================================
    // ACTIVE PANEL
    // ==========================================

    private void Extend_Click(object sender, RoutedEventArgs e) => _ctl.Extend(TimeSpan.FromMinutes(15));

    private void Cancel_Click(object sender, RoutedEventArgs e) => _ctl.Cancel();

    // ==========================================
    // SETTINGS PANEL
    // ==========================================

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        CommitFields();
        _inSettings = !_inSettings;
        Refresh();
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        try { StartupRegistration.Enabled = StartupSwitch.IsChecked == true; }
        catch (Exception ex) { MessageBox.Show(this, $"Couldn't update the startup setting.\n{ex.Message}", Title); }
        Refresh();
    }

    private void WarnSeconds_LostFocus(object sender, RoutedEventArgs e) => CommitFields();

    private void CommitFields()
    {
        if (!int.TryParse(WarnSeconds.Text.Trim(), out var secs)) { WarnSeconds.Text = _ctl.Settings.CountdownSeconds.ToString(); return; }
        secs = Math.Clamp(secs, 0, 600);
        WarnSeconds.Text = secs.ToString();
        if (secs == _ctl.Settings.CountdownSeconds) return;
        _ctl.Settings.CountdownSeconds = secs;
        _ctl.Settings.Save();
    }

    private void Remote_Click(object sender, RoutedEventArgs e)
    {
        _ctl.Settings.RemoteEnabled = RemoteSwitch.IsChecked == true;
        if (_ctl.Settings.RemoteEnabled && string.IsNullOrWhiteSpace(_ctl.Settings.RemoteRoot))
            _ctl.Settings.RemoteRoot = OneDriveRemote.DefaultRoot() ?? "";
        _ctl.Settings.Save();
        _ctl.ApplyRemoteSettings();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_ctl.RemoteReady) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_ctl.RemoteFolder}\"") { UseShellExecute = true });
    }

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Pick your OneDrive folder (a HibernateRemote folder is created inside it)",
            InitialDirectory = _ctl.Settings.RemoteRoot,
        };
        if (dlg.ShowDialog(this) != true) return;

        _ctl.Settings.RemoteRoot = dlg.FolderName;
        _ctl.Settings.Save();
        _ctl.ApplyRemoteSettings();
    }
}
