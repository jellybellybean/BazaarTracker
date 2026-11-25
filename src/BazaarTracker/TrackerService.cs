namespace BazaarTracker;

public sealed record PollResult(
    LobbyDto[] Lobbies,
    QueueStatusDto? Queue,
    string? LobbyError,
    string? QueueError);

/// <summary>
/// Authorization (Steam ticket → JWT → refresh token) and lobby server polling.
/// </summary>
public sealed class TrackerService : IDisposable
{
    private readonly BazaarApi _api = new();
    private readonly SemaphoreSlim _authGate = new(1, 1);
    private bool _steamIdentityChecked;

    public TrackerService(AppSettings settings, SessionData session)
    {
        Settings = settings;
        Session = session;
        _api.Configure(settings.CoreBaseUrl, settings.TournamentBaseUrl);
    }

    public AppSettings Settings { get; private set; }

    public SessionData Session { get; }

    public event Action<string>? Log;

    public void ApplySettings(AppSettings settings)
    {
        Settings = settings;
        _api.Configure(settings.CoreBaseUrl, settings.TournamentBaseUrl);
    }

    public async Task<PollResult> PollAsync(CancellationToken ct)
    {
        if (!await EnsureAuthenticatedAsync(force: false, ct))
        {
            return new PollResult(
                Array.Empty<LobbyDto>(),
                null,
                "No access to the server: " + (SteamAuth.LastError ?? "not authorized"),
                null);
        }

        var lobbies = await _api.GetLobbiesAsync(Session, ct);
        if (lobbies.IsUnauthorized)
        {
            Log?.Invoke("Token expired, signing in again...");
            if (!await EnsureAuthenticatedAsync(force: true, ct))
            {
                return new PollResult(Array.Empty<LobbyDto>(), null, "Session expired and signing in again failed.", null);
            }

            lobbies = await _api.GetLobbiesAsync(Session, ct);
        }

        if (!lobbies.IsOk)
        {
            return new PollResult(
                Array.Empty<LobbyDto>(),
                null,
                "Lobby list: " + (lobbies.Error ?? "unknown error"),
                null);
        }

        var queue = await _api.GetQueueAsync(Session, ct);
        return new PollResult(
            lobbies.Data ?? Array.Empty<LobbyDto>(),
            queue.IsOk ? queue.Data : null,
            null,
            queue.IsOk ? null : "Queue: " + (queue.Error ?? "no data"));
    }

    public async Task<bool> EnsureAuthenticatedAsync(bool force, CancellationToken ct)
    {
        await _authGate.WaitAsync(ct);
        try
        {
            SyncSteamIdentity();

            if (!force)
            {
                if (Session.AccessTokenLooksValid && !string.IsNullOrEmpty(Session.AccountId))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(Session.RefreshToken) && await TryRefreshAsync(ct))
                {
                    return await EnsureProfileAsync(ct);
                }
            }

            return await LoginWithSteamAsync(ct);
        }
        finally
        {
            _authGate.Release();
        }
    }

    /// <summary>
    /// Once per launch, checks the saved session against the current Steam account.
    /// This keeps the app from sticking to someone else's or an old account.
    /// </summary>
    private void SyncSteamIdentity()
    {
        if (_steamIdentityChecked)
        {
            return;
        }

        _steamIdentityChecked = true;
        if (!SteamAuth.TryGetSteamId(out var steamId))
        {
            return;
        }

        var id = steamId.ToString();
        if (!string.IsNullOrEmpty(Session.SteamId) && Session.SteamId != id)
        {
            Log?.Invoke("Steam is on a different account - resetting the saved session.");
            Session.ClearTokens();
            Session.AccountId = null;
            Session.UserName = null;
        }

        Session.SteamId = id;
        Session.Save();
    }

    /// <summary>
    /// Fully signs out of the current session and signs in as the Steam account that is running now.
    /// </summary>
    public async Task<bool> SwitchAccountAsync(CancellationToken ct)
    {
        await _authGate.WaitAsync(ct);
        try
        {
            Session.ClearTokens();
            Session.AccountId = null;
            Session.UserName = null;
            Session.SteamId = null;
            Session.Save();
        }
        finally
        {
            _authGate.Release();
        }

        // reconnect to Steam: the account may have changed in the Steam client
        SteamAuth.Shutdown();
        _steamIdentityChecked = true;
        Log?.Invoke("Session reset, signing in again.");
        return await EnsureAuthenticatedAsync(force: true, ct);
    }

    private async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        var result = await _api.RefreshSessionAsync(Session.RefreshToken!, ct);
        if (!result.IsOk || result.Data?.AccessToken == null)
        {
            Log?.Invoke("Refresh token rejected, a Steam sign-in is needed.");
            Session.ClearTokens();
            return false;
        }

        ApplyTokens(result.Data);
        Log?.Invoke("Session renewed via refresh token.");
        return true;
    }

    private async Task<bool> LoginWithSteamAsync(CancellationToken ct)
    {
        if (!SteamAuth.Init())
        {
            Log?.Invoke("Steam unavailable: " + SteamAuth.LastError);
            return false;
        }

        try
        {
            Log?.Invoke("Requesting a Steam ticket...");
            var ticket = await SteamAuth.GetWebApiTicketAsync(ct);
            var login = await _api.LoginSilentAsync(ticket, ct);
            if (!login.IsOk || login.Data?.AccessToken == null)
            {
                Log?.Invoke("Sign-in failed: " + (login.Error ?? "no token in the response"));
                return false;
            }

            ApplyTokens(login.Data);
            if (SteamAuth.TryGetSteamId(out var steamId))
            {
                Session.SteamId = steamId.ToString();
            }

            // always re-read the profile: nickname and accountId depend on the account
            Session.AccountId = null;
            Session.UserName = null;
            Session.Save();
            Log?.Invoke("Signed in.");

            if (!await EnsureProfileAsync(ct))
            {
                Log?.Invoke("Could not load the profile (aid/uid) - lobby requests may not work.");
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log?.Invoke("Steam sign-in failed: " + ex.Message);
            return false;
        }
    }

    private async Task<bool> EnsureProfileAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(Session.AccountId) && !string.IsNullOrEmpty(Session.UserName))
        {
            return true;
        }

        var profile = await _api.GetProfileAsync(Session.AccessToken!, ct);
        if (!profile.IsOk || profile.Data == null)
        {
            Log?.Invoke("Profile: " + (profile.Error ?? "empty response"));
            return false;
        }

        Session.AccountId = profile.Data.NormalizedAccountId;
        Session.UserName = profile.Data.UserName ?? string.Empty;
        Session.Save();
        return !string.IsNullOrEmpty(Session.AccountId);
    }

    private void ApplyTokens(LoginResponseDto tokens)
    {
        Session.AccessToken = tokens.AccessToken;
        Session.RefreshToken = tokens.RefreshToken ?? Session.RefreshToken;
        Session.AccessTokenExpiresAtUtc = tokens.AccessTokenExpiresAt;
        Session.Save();
    }

    public void Dispose()
    {
        _api.Dispose();
        _authGate.Dispose();
    }
}
