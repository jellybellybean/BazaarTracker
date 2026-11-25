using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BazaarTracker;

public sealed class LoginResponseDto
{
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("accessTokenExpiresAt")]
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
}

public sealed record HttpResult<T>(int StatusCode, T? Data, string? Error, TimeSpan? RetryAfter = null)
{
    public bool IsOk => StatusCode is >= 200 and < 300 && Error == null;

    /// <summary>HTTP 401: the access token is missing, expired or rejected.</summary>
    public bool IsUnauthorized => StatusCode == 401;

    /// <summary>HTTP 403: the server refuses this client. Retrying or re-signing in will not help.</summary>
    public bool IsForbidden => StatusCode == 403;

    /// <summary>Network error, timeout, 408, 429 or 5xx: a problem on the way or on the server side.</summary>
    public bool IsTransient => StatusCode is 0 or 408 or 429 or >= 500;
}

/// <summary>
/// Wrapper over the game's two HTTP APIs: temponet (login, profile) and tournament (lobbies, queue).
/// </summary>
public sealed class BazaarApi : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private string _coreBaseUrl = AppInfo.DefaultCoreBaseUrl;
    private string _tournamentBaseUrl = AppInfo.DefaultTournamentBaseUrl;

    public BazaarApi()
    {
        _http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _http.DefaultRequestHeaders.Add("X-Platform", AppInfo.Platform);
        _http.DefaultRequestHeaders.Add("X-ClientFlavor", AppInfo.ClientFlavor);
    }

    public void Configure(string coreBaseUrl, string tournamentBaseUrl)
    {
        _coreBaseUrl = coreBaseUrl.TrimEnd('/');
        _tournamentBaseUrl = tournamentBaseUrl.TrimEnd('/');
    }

    public Task<HttpResult<LoginResponseDto>> LoginSilentAsync(string ticketHex, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            SteamTicket = new { TicketAsString = ticketHex }
        });

        return SendJsonAsync<LoginResponseDto>(
            HttpMethod.Post, _coreBaseUrl + "/api/auth/login/silent", null, payload, ct);
    }

    public Task<HttpResult<LoginResponseDto>> RefreshSessionAsync(string refreshToken, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { RefreshToken = refreshToken });
        return SendJsonAsync<LoginResponseDto>(
            HttpMethod.Post, _coreBaseUrl + "/api/auth/refreshtokens", null, payload, ct);
    }

    public Task<HttpResult<PlayerProfileDto>> GetProfileAsync(string accessToken, CancellationToken ct)
    {
        return SendJsonAsync<PlayerProfileDto>(
            HttpMethod.Get, _coreBaseUrl + "/api/PlayerProfiles/me", accessToken, null, ct);
    }

    public Task<HttpResult<LobbyDto[]>> GetLobbiesAsync(SessionData session, CancellationToken ct)
    {
        return SendTournamentAsync<LobbyDto[]>(session, "/lobbies?limit=50", ct);
    }

    public Task<HttpResult<QueueStatusDto>> GetQueueAsync(SessionData session, CancellationToken ct)
    {
        return SendTournamentAsync<QueueStatusDto>(session, "/queue", ct);
    }

    private Task<HttpResult<T>> SendTournamentAsync<T>(SessionData session, string path, CancellationToken ct)
    {
        var url = _tournamentBaseUrl + path;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.TryAddWithoutValidation("aid", session.AccountId);
        request.Headers.TryAddWithoutValidation("uid", session.UserName);
        return SendAsync<T>(request, ct);
    }

    private async Task<HttpResult<T>> SendJsonAsync<T>(
        HttpMethod method,
        string url,
        string? accessToken,
        string? jsonBody,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (jsonBody != null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return await SendAsync<T>(request, ct);
    }

    private async Task<HttpResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                return new HttpResult<T>(status, default, FormatError(response.StatusCode, body), ParseRetryAfter(response));
            }

            if (string.IsNullOrWhiteSpace(body) || body == "null")
            {
                return new HttpResult<T>(status, default, null);
            }

            var data = JsonSerializer.Deserialize<T>(body, JsonOptions);
            return new HttpResult<T>(status, data, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new HttpResult<T>(0, default, "Request cancelled.");
        }
        catch (OperationCanceledException)
        {
            return new HttpResult<T>(0, default, "Server did not respond within 20 seconds.");
        }
        catch (JsonException ex)
        {
            // a garbled or unexpected body is a server-side problem: report it, never crash or drop the tokens
            return new HttpResult<T>(502, default, "Unexpected response from the server: " + ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new HttpResult<T>(0, default, "Network: " + ex.Message);
        }
    }

    /// <summary>Reads the Retry-After header (delta-seconds or an HTTP date).</summary>
    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header == null)
        {
            return null;
        }

        var delay = header.Delta ?? (header.Date.HasValue ? header.Date.Value - DateTimeOffset.UtcNow : (TimeSpan?)null);
        return delay is { } d && d > TimeSpan.Zero ? d : null;
    }

    private static string FormatError(HttpStatusCode status, string body)
    {
        var shortBody = body.Length > 300 ? body[..300] + "…" : body;
        return $"HTTP {(int)status} ({status}){(string.IsNullOrWhiteSpace(shortBody) ? string.Empty : ": " + shortBody)}";
    }

    public void Dispose() => _http.Dispose();
}
