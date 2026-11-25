using System.Text.Json.Serialization;

namespace BazaarTracker;

/// <summary>Элемент ответа GET /lobbies (TournamentPublicLobbyDto в клиенте).</summary>
public sealed class LobbyDto
{
    [JsonPropertyName("lobbyId")]
    public string? LobbyId { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("hostAccountId")]
    public string? HostAccountId { get; set; }

    [JsonPropertyName("hostUsername")]
    public string? HostUsername { get; set; }

    [JsonPropertyName("joinedCount")]
    public int JoinedCount { get; set; }

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? "(без названия)" : Name!;

    [JsonIgnore]
    public string DisplayCode =>
        string.IsNullOrWhiteSpace(Code) ? "—" : Code!.ToUpperInvariant();

    [JsonIgnore]
    public int FreeSlots => Math.Max(0, MaxPlayers - JoinedCount);

    [JsonIgnore]
    public string PlayersText => $"{JoinedCount}/{MaxPlayers}";
}

/// <summary>Ответ GET /queue.</summary>
public sealed class QueueStatusDto
{
    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("queuedCount")]
    public int QueuedCount { get; set; }

    [JsonPropertyName("maxPlayers")]
    public int MaxPlayers { get; set; }
}

/// <summary>Ответ GET /api/PlayerProfiles/me.</summary>
public sealed class PlayerProfileDto
{
    [JsonPropertyName("accountId")]
    public Guid AccountId { get; set; }

    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    [JsonIgnore]
    public string NormalizedAccountId => AccountId.ToString("N");
}
