using System.Text.Json;
using System.Text.Json.Serialization;

namespace BazaarTracker;

/// <summary>
/// Access tokens and profile persisted between runs.
/// The file lives only in the current user's profile (%APPDATA%\BazaarTracker\session.json).
/// </summary>
public sealed class SessionData
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("accessTokenExpiresAtUtc")]
    public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }

    /// <summary>SteamID64 the tokens were issued for. Used to avoid mixing up accounts.</summary>
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
            // ignore a corrupted cache
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
            // not critical
        }
    }

    public void ClearTokens()
    {
        AccessToken = null;
        RefreshToken = null;
        AccessTokenExpiresAtUtc = null;
    }
}
