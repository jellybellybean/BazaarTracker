using System.Text.Json;
using System.Text.Json.Serialization;

namespace BazaarTracker;

public sealed class AppSettings
{
    [JsonPropertyName("pollIntervalSeconds")]
    public int PollIntervalSeconds { get; set; } = 15;

    [JsonPropertyName("notificationsEnabled")]
    public bool NotificationsEnabled { get; set; } = true;

    [JsonPropertyName("launchGameOnClick")]
    public bool LaunchGameOnClick { get; set; } = true;

    [JsonPropertyName("useTrayPopup")]
    public bool UseTrayPopup { get; set; } = true;

    [JsonPropertyName("queueNotificationsEnabled")]
    public bool QueueNotificationsEnabled { get; set; } = true;

    [JsonPropertyName("soundEnabled")]
    public bool SoundEnabled { get; set; } = true;

    [JsonPropertyName("animateTrayIcon")]
    public bool AnimateTrayIcon { get; set; } = true;

    [JsonPropertyName("minimizeToTray")]
    public bool MinimizeToTray { get; set; } = true;

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    [JsonPropertyName("startMinimized")]
    public bool StartMinimized { get; set; }

    [JsonPropertyName("coreBaseUrl")]
    public string CoreBaseUrl { get; set; } = AppInfo.DefaultCoreBaseUrl;

    [JsonPropertyName("tournamentBaseUrl")]
    public string TournamentBaseUrl { get; set; } = AppInfo.DefaultTournamentBaseUrl;

    [JsonIgnore]
    public int ClampedInterval => Math.Clamp(PollIntervalSeconds, 5, 300);

    [JsonIgnore]
    private static string FilePath => Path.Combine(AppInfo.AppDataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (loaded != null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.CoreBaseUrl)) loaded.CoreBaseUrl = AppInfo.DefaultCoreBaseUrl;
                    if (string.IsNullOrWhiteSpace(loaded.TournamentBaseUrl)) loaded.TournamentBaseUrl = AppInfo.DefaultTournamentBaseUrl;
                    loaded.CoreBaseUrl = loaded.CoreBaseUrl.TrimEnd('/');
                    loaded.TournamentBaseUrl = loaded.TournamentBaseUrl.TrimEnd('/');
                    loaded.StartWithWindows = AutoStart.IsEnabled();
                    return loaded;
                }
            }
        }
        catch
        {
            // corrupted settings file: start from defaults
        }

        var fresh = new AppSettings { StartWithWindows = AutoStart.IsEnabled() };
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        try
        {
            PollIntervalSeconds = ClampedInterval;
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // not critical: settings just won't be saved
        }
    }
}

internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BazaarTracker";

    private static string? ExecutablePath => Environment.ProcessPath;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static bool Apply(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null)
            {
                return false;
            }

            if (enabled && !string.IsNullOrWhiteSpace(ExecutablePath))
            {
                key.SetValue(ValueName, "\"" + ExecutablePath + "\" --tray");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
