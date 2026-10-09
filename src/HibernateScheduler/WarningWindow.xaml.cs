/* ===================================
   WarningWindow.xaml.cs
   -----------------------------------
   - Always-on-top countdown before hibernating: Now / +15 min / Cancel.
   - Never takes focus, so typing can't accidentally press a button.
   =================================== */

using System;
using System.Windows;
using System.Windows.Threading;

namespace HibernateScheduler;

internal enum WarningResult { Cancel, Snooze, Hibernate }

public partial class WarningWindow : Window
{
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _remaining;
    private bool _done;

    internal WarningResult Result { get; private set; } = WarningResult.Cancel;

    public WarningWindow(string reason, int seconds)
    {
        InitializeComponent();
        NativeChrome.Apply(this);
        Icon = AppIcon.CreateImage();
        _remaining = seconds;
        ReasonText.Text = reason;
        Bar.Maximum = seconds;
        Bar.Value = seconds;
        UpdateText();

        _tick.Tick += (_, _) =>
        {
            if (--_remaining <= 0) { Finish(WarningResult.Hibernate); return; }
            Bar.Value = _remaining;
            UpdateText();
        };

        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 12;
            Top = area.Bottom - ActualHeight - 12;
            _tick.Start();
        };
    }

    private void UpdateText() => Headline.Text = $"Hibernating in {_remaining}s";

    private void Now_Click(object sender, RoutedEventArgs e) => Finish(WarningResult.Hibernate);
    private void Snooze_Click(object sender, RoutedEventArgs e) => Finish(WarningResult.Snooze);
    private void Cancel_Click(object sender, RoutedEventArgs e) => Finish(WarningResult.Cancel);

    internal void CancelCountdown() => Finish(WarningResult.Cancel);

    private void Finish(WarningResult result)
    {
        if (_done) return;
        _done = true;
        _tick.Stop();
        Result = result;
        Close();
    }
}
