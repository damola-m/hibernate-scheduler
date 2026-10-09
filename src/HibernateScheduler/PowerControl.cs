/* ===================================
   PowerControl.cs
   -----------------------------------
   - Hibernate the machine, start-with-Windows registry toggle.
   =================================== */

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace HibernateScheduler;

internal static class PowerControl
{
    [DllImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsPwrHibernateAllowed();

    public static bool HibernateAvailable
    {
        get
        {
            try { return IsPwrHibernateAllowed(); }
            catch { return false; }
        }
    }

    public static void Hibernate()
    {
        // SetSuspendState can fail without the shutdown privilege; shutdown.exe enables it itself
        if (WF.Application.SetSuspendState(WF.PowerState.Hibernate, false, false)) return;

        Process.Start(new ProcessStartInfo("shutdown.exe", "/h")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
    }
}

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HibernateScheduler";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --minimized");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
