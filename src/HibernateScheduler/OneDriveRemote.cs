/* ===================================
   OneDriveRemote.cs
   -----------------------------------
   - Remote trigger through a OneDrive folder: create a folder (or file) named after a command,
     the app runs it, deletes it and writes status.txt alongside.
   - No third-party service and no listening ports; access is whatever protects your OneDrive.
   =================================== */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HibernateScheduler;

internal sealed class OneDriveRemote
{
    // ==========================================
    // CONSTANTS
    // ==========================================

    private const string StatusFile = "status.txt";
    private const string HelpFile = "How to use.txt";

    // Commands older than this are stale, e.g. made while the laptop was off
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

    public string Folder { get; }
    private DateTime _armedSince = DateTime.Now;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public OneDriveRemote(string root)
    {
        Folder = Path.Combine(root, "HibernateRemote", Environment.MachineName);
    }

    // Personal OneDrive first, then a work/school one
    public static string? DefaultRoot()
    {
        foreach (var name in new[] { "OneDriveConsumer", "OneDrive", "OneDriveCommercial" })
        {
            var path = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) return path;
        }
        return null;
    }

    // Accept the OneDrive root, its HibernateRemote folder or the machine folder itself
    public static string NormaliseRoot(string picked)
    {
        var path = picked.TrimEnd('\\', '/');
        if (Path.GetFileName(path).Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(Path.GetDirectoryName(path) ?? "").Equals("HibernateRemote", StringComparison.OrdinalIgnoreCase))
            path = Path.GetDirectoryName(path)!;
        if (Path.GetFileName(path).Equals("HibernateRemote", StringComparison.OrdinalIgnoreCase))
            path = Path.GetDirectoryName(path) ?? path;
        return path;
    }

    public bool Ready => Directory.Exists(Folder);

    // ==========================================
    // SETUP
    // ==========================================

    public bool EnsureFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            // Rewritten whenever the wording changes so the phone copy never goes stale
            var help = Path.Combine(Folder, HelpFile);
            var text = HelpText();
            if (!File.Exists(help) || File.ReadAllText(help) != text) File.WriteAllText(help, text);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Remote folder setup failed: {ex.Message}");
            return false;
        }
    }

    // Written for reading on a phone: no column alignment, short lines
    private static string HelpText() => string.Join("\r\n",
        $"HIBERNATE {Environment.MachineName} FROM YOUR PHONE",
        "",
        "STEPS",
        "1. Open the OneDrive app (personal account).",
        $"2. Go to HibernateRemote > {Environment.MachineName} (this folder).",
        "3. Tap + and choose Create folder.",
        "4. Name the folder with one of the commands below, then tap Create.",
        "5. Wait up to a minute. The folder disappears once the laptop has read it.",
        "6. Open status.txt to see the result. Pull down to refresh if it looks old.",
        "",
        "COMMANDS (type as the folder name)",
        "- hibernate : 60-second warning on screen, then hibernates",
        "- hibernate 30 : hibernates in 30 minutes",
        "- hibernate 2h : hibernates in 2 hours",
        "- hibernate at 2300 : hibernates at 23:00 (no colon, folder names can't have one)",
        "- cancel : cancels a pending hibernate or a running countdown",
        "- status : just refreshes status.txt",
        "",
        "GOOD TO KNOW",
        "- The laptop must be on, awake and running Hibernate Scheduler (moon icon in the tray).",
        "- It can't wake a laptop that is already hibernating.",
        "- Commands older than 10 minutes are ignored, so an old one can't fire later.",
        "- Capitals don't matter: Hibernate 30 works too.",
        "- Don't share this folder. Anyone who can create folders here can hibernate the laptop.",
        "");

    // After wake, anything already in the folder was made while asleep
    public void Rearm() => _armedSince = DateTime.Now;

    // ==========================================
    // POLL
    // ==========================================

    public List<string> Poll()
    {
        var commands = new List<string>();
        if (!Ready) return commands;

        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(Folder).ToList(); }
        catch { return commands; }

        foreach (var path in entries)
        {
            // =============================
            // Part 1 — Skip our own files
            // =============================
            var isDir = Directory.Exists(path);
            var fileName = Path.GetFileName(path);
            if (!isDir && (fileName.Equals(StatusFile, StringComparison.OrdinalIgnoreCase)
                        || fileName.Equals(HelpFile, StringComparison.OrdinalIgnoreCase)
                        || fileName.StartsWith('.') || fileName.StartsWith('~')))
                continue;

            var name = isDir ? fileName : Path.GetFileNameWithoutExtension(path);

            // =============================
            // Part 2 — Staleness check
            // =============================
            DateTime stamp;
            try
            {
                stamp = isDir
                    ? Max(Directory.GetCreationTime(path), Directory.GetLastWriteTime(path))
                    : Max(File.GetCreationTime(path), File.GetLastWriteTime(path));
            }
            catch { continue; }

            var key = $"{name}|{stamp.Ticks}";
            if (!_seen.Add(key)) { TryDelete(path, isDir); continue; }

            var fresh = stamp >= _armedSince.AddMinutes(-1) && DateTime.Now - stamp <= MaxAge;

            // =============================
            // Part 3 — Consume
            // =============================
            TryDelete(path, isDir);
            if (fresh) commands.Add(name);
        }
        return commands;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static void TryDelete(string path, bool isDir)
    {
        try
        {
            if (isDir) Directory.Delete(path, recursive: true);
            else File.Delete(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Couldn't remove command {path}: {ex.Message}");
        }
    }

    public void WriteStatus(string text)
    {
        try
        {
            if (!Ready) return;
            File.WriteAllText(Path.Combine(Folder, StatusFile), $"{text}\r\n\r\nUpdated {DateTime.Now:ddd d MMM HH:mm:ss}\r\n");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Status write failed: {ex.Message}");
        }
    }
}
