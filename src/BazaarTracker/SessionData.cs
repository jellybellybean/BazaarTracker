using System.Text.Json;
using System.Text.Json.Serialization;

namespace BazaarTracker;

/// <summary>
/// Токены доступа и профиль, сохранённые между запусками.
/// Файл лежит только в профиле текущего пользователя (%APPDATA%\BazaarTracker\session.json).
/// </summary>
public sealed class SessionData
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("accessTokenExpiresAtUtc")]
    public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }

    /// <summary>SteamID64, под которым получены токены. Нужен, чтобы не перепутать аккаунты.</summary>
    [JsonPropertyName("steamId")]
    public string? SteamId { get; set; }

    [JsonPropertyName("accountId")]
    public string? AccountId { get; set; }

    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    [JsonIgnore]
    private static string FilePath => Path.Combine(AppInfo.AppDataDir, "session.json");

    [JsonIgnore]
    public bool AccessTokenLooksValid =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        AccessTokenExpiresAtUtc is { } expires &&
        expires > DateTimeOffset.UtcNow.AddMinutes(2);

    public static SessionData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<SessionData>(File.ReadAllText(FilePath)) ?? new SessionData();
            }
        }
        catch
        {
            // игнорируем повреждённый кэш
        }

        return new SessionData();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // не критично
        }
    }

    public void ClearTokens()
    {
        AccessToken = null;
        RefreshToken = null;
        AccessTokenExpiresAtUtc = null;
    }
}
