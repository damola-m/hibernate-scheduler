/* ===================================
   HibernateController.cs
   -----------------------------------
   - Owns the single pending hibernate, the 1s clock and the warning countdown.
   - Runs remote commands picked up from the OneDrive folder and writes status back.
   =================================== */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace HibernateScheduler;

internal sealed partial class HibernateController : IDisposable
{
    // ==========================================
    // STATE
    // ==========================================

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _lastTick = DateTime.Now;
    private int _ticks;
    private WarningWindow? _warning;
    private OneDriveRemote? _remote;

    public AppSettings Settings { get; }
    public DateTime? TargetAt => Settings.TargetAt;
    public string Reason => Settings.TargetReason;
    public bool WarningActive => _warning != null;
    public bool RemoteReady => _remote?.Ready == true;
    public string? RemoteFolder => _remote?.Folder;

    public event Action? StateChanged;
    public event Action? Tick;
    public event Action<string>? Notice;

    public HibernateController()
    {
        Settings = AppSettings.Load();

        // Drop a target that passed while the app wasn't running
        if (Settings.TargetAt <= DateTime.Now) ClearTarget(save: true);

        _clock.Tick += (_, _) => OnTick();
        _clock.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        ApplyRemoteSettings();
    }

    // ==========================================
    // CLOCK
    // ==========================================

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        _lastTick = DateTime.Now;
        _remote?.Rearm();
    }

    private void OnTick()
    {
        var now = DateTime.Now;

        // =============================
        // Part 1 — Ignore time lost to sleep/hibernate
        // =============================
        if (now - _lastTick > TimeSpan.FromMinutes(2))
        {
            _lastTick = now;
            _remote?.Rearm();
            if (TargetAt <= now)
            {
                ClearTarget(save: true);
                Announce("The hibernate time passed while the laptop was asleep, so it was cleared.");
            }
            return;
        }
        _lastTick = now;

        // =============================
        // Part 2 — Target reached
        // =============================
        if (TargetAt is { } due && due <= now)
        {
            var reason = Reason;
            ClearTarget(save: true);
            StartWarning(reason);
        }

        // =============================
        // Part 3 — Remote folder every 3s
        // =============================
        if (_remote != null && ++_ticks % 3 == 0)
        {
            foreach (var cmd in _remote.Poll()) HandleRemoteCommand(cmd);
        }

        Tick?.Invoke();
    }

    // ==========================================
    // ACTIONS
    // ==========================================

    public void SetTarget(DateTime at, string reason)
    {
        Settings.TargetAt = new DateTime(at.Year, at.Month, at.Day, at.Hour, at.Minute, at.Second);
        Settings.TargetReason = reason;
        Settings.Save();
        StateChanged?.Invoke();
        Announce($"Hibernating {Format(at)} ({reason}).");
    }

    public void Extend(TimeSpan by)
    {
        if (TargetAt is { } at) SetTarget(at + by, Reason);
    }

    public void Cancel()
    {
        var had = TargetAt != null;
        ClearTarget(save: true);
        StateChanged?.Invoke();

        // The warning's close handler announces for itself
        if (_warning != null) _warning.CancelCountdown();
        else if (had) Announce("Hibernate cancelled.");
    }

    private void ClearTarget(bool save)
    {
        Settings.TargetAt = null;
        Settings.TargetReason = "";
        if (save) Settings.Save();
    }

    public void StartWarning(string reason)
    {
        if (_warning != null) return;
        if (Settings.CountdownSeconds <= 0) { _ = HibernateNowAsync(reason); return; }

        _warning = new WarningWindow(reason, Settings.CountdownSeconds);
        _warning.Closed += (_, _) =>
        {
            var result = _warning!.Result;
            _warning = null;
            StateChanged?.Invoke();

            switch (result)
            {
                case WarningResult.Hibernate: _ = HibernateNowAsync(reason); break;
                case WarningResult.Snooze: SetTarget(DateTime.Now.AddMinutes(15), reason); break;
                default: Announce("Hibernate cancelled."); break;
            }
        };
        _warning.Show();
        StateChanged?.Invoke();
        Announce($"Hibernating in {Settings.CountdownSeconds}s ({reason}).");
    }

    public async Task HibernateNowAsync(string reason)
    {
        if (!PowerControl.HibernateAvailable)
        {
            Announce("Hibernate is turned off on this machine. Run 'powercfg /hibernate on' as admin.");
            return;
        }

        if (_remote != null)
        {
            _remote.WriteStatus($"Hibernated {DateTime.Now:ddd d MMM HH:mm} ({reason}).");
            await Task.Delay(1500);   // give OneDrive a moment to pick the status up
        }
        PowerControl.Hibernate();
    }

    // ==========================================
    // FORMATTING
    // ==========================================

    public static DateTime NextOccurrence(int hour, int minute)
    {
        var at = DateTime.Today.AddHours(hour).AddMinutes(minute);
        return at > DateTime.Now ? at : at.AddDays(1);
    }

    public static string Format(DateTime at)
    {
        var today = DateTime.Today;
        if (at.Date == today) return $"today at {at:HH:mm}";
        if (at.Date == today.AddDays(1)) return $"tomorrow at {at:HH:mm}";
        return $"{at:ddd d MMM} at {at:HH:mm}";
    }

    public static string FormatSpan(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    public static string FormatDuration(TimeSpan t)
    {
        var mins = (int)Math.Round(t.TotalMinutes);
        if (mins < 60) return $"{mins} min";
        return mins % 60 == 0 ? $"{mins / 60} hr" : $"{mins / 60} hr {mins % 60} min";
    }

    public string StatusText()
    {
        if (_warning != null) return "Countdown running";
        if (TargetAt is { } at) return $"Hibernating {Format(at)}";
        return "Nothing set";
    }

    // ==========================================
    // REMOTE
    // ==========================================

    public void ApplyRemoteSettings()
    {
        _remote = null;
        if (Settings.RemoteEnabled && !string.IsNullOrWhiteSpace(Settings.RemoteRoot))
        {
            var remote = new OneDriveRemote(OneDriveRemote.NormaliseRoot(Settings.RemoteRoot));
            if (remote.EnsureFolder())
            {
                _remote = remote;
                _remote.Poll();   // clear out anything left over; it's stale by definition
                WriteRemoteStatus("Remote trigger is on.");
            }
        }
        StateChanged?.Invoke();
    }

    [GeneratedRegex(@"\bat\s*(\d{1,2})[\s:.h]?(\d{2})?\b")]
    private static partial Regex AtPattern();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(h|hr|hrs|hours?|m|mins?|minutes?)?\b")]
    private static partial Regex DurationPattern();

    private void HandleRemoteCommand(string raw)
    {
        var text = raw.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
        bool Has(params string[] words) => words.Any(w => Regex.IsMatch(text, $@"\b{w}\b"));

        // =============================
        // Part 1 — Simple verbs
        // =============================
        if (Has("cancel", "stop", "abort")) { Cancel(); WriteRemoteStatus("Cancelled."); return; }
        if (Has("status")) { WriteRemoteStatus("Status requested."); return; }
        if (!Has("hibernate", "sleep")) { WriteRemoteStatus($"Didn't understand \"{raw}\". See 'How to use.txt'."); return; }

        // =============================
        // Part 2 — "hibernate at 2300"
        // =============================
        var at = AtPattern().Match(text);
        if (at.Success)
        {
            var h = int.Parse(at.Groups[1].Value);
            var m = at.Groups[2].Success ? int.Parse(at.Groups[2].Value) : 0;

            // "at 2300" arrives as a single 4-digit group
            if (h > 23 && at.Groups[1].Value.Length >= 3) { m = h % 100; h /= 100; }
            if (h > 23 || m > 59) { WriteRemoteStatus($"\"{raw}\" isn't a valid time."); return; }

            SetTarget(NextOccurrence(h, m), "remote");
            WriteRemoteStatus("Set from remote.");
            return;
        }

        // =============================
        // Part 3 — "hibernate 30" / "hibernate 2h"
        // =============================
        var total = TimeSpan.Zero;
        foreach (Match d in DurationPattern().Matches(text))
        {
            var n = double.Parse(d.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            total += d.Groups[2].Value.StartsWith('h') ? TimeSpan.FromHours(n) : TimeSpan.FromMinutes(n);
        }

        if (total > TimeSpan.Zero)
        {
            SetTarget(DateTime.Now + total, "remote");
            WriteRemoteStatus("Set from remote.");
        }
        else
        {
            StartWarning("remote");
            WriteRemoteStatus("Countdown started from remote.");
        }
    }

    private void WriteRemoteStatus(string lastAction)
    {
        if (_remote == null) return;

        var power = WF.SystemInformation.PowerStatus;
        var battery = power.BatteryLifePercent <= 1
            ? $"Battery {power.BatteryLifePercent:P0}{(power.PowerLineStatus == WF.PowerLineStatus.Online ? ", plugged in" : "")}"
            : "No battery info";

        _remote.WriteStatus($"{Environment.MachineName} is awake.\r\n{StatusText()}.\r\n{battery}.\r\nLast: {lastAction}");
    }

    // Tray balloon + keep the remote status file current
    private void Announce(string message)
    {
        Notice?.Invoke(message);
        WriteRemoteStatus(message);
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _clock.Stop();
        _warning?.Close();
    }
}
