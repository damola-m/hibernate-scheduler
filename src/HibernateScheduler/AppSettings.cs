/* ===================================
   AppSettings.cs
   -----------------------------------
   - Persisted settings, including the single pending hibernate time.
   - Stored as JSON in %AppData%\HibernateScheduler\settings.json.
   =================================== */

using System;
using System.IO;
using System.Text.Json;

namespace HibernateScheduler;

public sealed class AppSettings
{
    // ==========================================
    // VALUES
    // ==========================================

    // The one pending hibernate; kept on disk so it survives an app restart
    public DateTime? TargetAt { get; set; }
    public string TargetReason { get; set; } = "";

    public int CountdownSeconds { get; set; } = 60;

    public bool RemoteEnabled { get; set; }

    // OneDrive root (or any synced folder); the app works in <root>\HibernateRemote\<machine>
    public string RemoteRoot { get; set; } = OneDriveRemote.DefaultRoot() ?? "";

    // ==========================================
    // PERSISTENCE
    // ==========================================

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HibernateScheduler");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings load failed: {ex.Message}");
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);

        // Write-then-move so a crash mid-write can't corrupt the file
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
