namespace BazaarTracker;

/// <summary>How the caller should treat a failed poll.</summary>
public enum PollFailure
{
    /// <summary>No failure.</summary>
    None,

    /// <summary>Network or server trouble: retry later with a growing delay.</summary>
    Backoff,

    /// <summary>Problem on this computer (e.g. Steam is not running): retry at the normal interval.</summary>
    Local
}

public sealed record PollResult(
    LobbyDto[] Lobbies,
    QueueStatusDto? Queue,
    string? LobbyError,
    string? QueueError,
    PollFailure Failure = PollFailure.None,
    TimeSpan? RetryAfter = null,
    bool StopPolling = false);

/// <summary>
/// Authorization (Steam ticket → JWT → refresh token) and lobby server polling.
/// </summary>
/// <remarks>
/// A full Steam sign-in is only done when there is no refresh token or the server explicitly
/// rejected it (HTTP 400/401). Timeouts, 429 and 5xx never discard the tokens. HTTP 403 stops
/// everything and is reported to the user.
/// </remarks>
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

    /// <summary>Test seam: replaces Steam as the source of the sign-in ticket (used by the offline self-tests).</summary>
    internal Func<CancellationToken, Task<string>>? TicketProviderOverride { get; set; }

    public void ApplySettings(AppSettings settings)
    {
        Settings = settings;
        _api.Configure(settings.CoreBaseUrl, settings.TournamentBaseUrl);
    }

    public async Task<PollResult> PollAsync(CancellationToken ct)
    {
        var auth = await AuthenticateAsync(ignoreValidAccessToken: false, ct);
        if (!auth.Ok)
        {
            return FromAuthFailure(auth);
        }

        var lobbies = await _api.GetLobbiesAsync(Session, ct);
        if (lobbies.IsUnauthorized)
        {
            // 401 only: renew the token (refresh token first, Steam only if that is rejected) and retry once
            Log?.Invoke("Access token rejected (HTTP 401), renewing it...");
            auth = await AuthenticateAsync(ignoreValidAccessToken: true, ct);
            if (!auth.Ok)
            {
                return FromAuthFailure(auth);
            }

            lobbies = await _api.GetLobbiesAsync(Session, ct);
        }

        if (lobbies.IsForbidden)
        {
            return Failed("Lobby list: " + lobbies.Error, PollFailure.None, null, stop: true);
        }

        if (!lobbies.IsOk)
        {
            return Failed("Lobby list: " + (lobbies.Error ?? "unknown error"), PollFailure.Backoff, lobbies.RetryAfter);
        }

        var queue = await _api.GetQueueAsync(Session, ct);
        return new PollResult(
            lobbies.Data ?? Array.Empty<LobbyDto>(),
            queue.IsOk ? queue.Data : null,
            null,
            queue.IsOk ? null : "Queue: " + (queue.Error ?? "no data"),
            RetryAfter: queue.IsOk ? null : queue.RetryAfter,
            StopPolling: queue.IsForbidden);
    }

    /// <summary>
    /// Makes sure a usable session exists. With <paramref name="force"/> the current access token is
    /// not trusted, but the refresh token is still tried before a Steam sign-in.
    /// </summary>
    public async Task<bool> EnsureAuthenticatedAsync(bool force, CancellationToken ct)
        => (await AuthenticateAsync(force, ct)).Ok;

    private static PollResult Failed(string error, PollFailure failure, TimeSpan? retryAfter, bool stop = false)
        => new(Array.Empty<LobbyDto>(), null, error, null, failure, retryAfter, stop);

    private static PollResult FromAuthFailure(AuthResult auth)
        => Failed("No access to the server: " + (auth.Error ?? "not authorized"), auth.Failure, auth.RetryAfter, auth.Stop);

    private async Task<AuthResult> AuthenticateAsync(bool ignoreValidAccessToken, CancellationToken ct)
    {
        await _authGate.WaitAsync(ct);
        try
        {
            SyncSteamIdentity();

            if (!ignoreValidAccessToken && Session.AccessTokenLooksValid && !string.IsNullOrEmpty(Session.AccountId))
            {
                return AuthResult.Success;
            }

            if (!string.IsNullOrEmpty(Session.RefreshToken))
            {
                var refresh = await TryRefreshAsync(ct);
                switch (refresh.Outcome)
                {
                    case RefreshOutcome.Renewed:
                        return await EnsureProfileAsync(ct);
                    case RefreshOutcome.Rejected:
                        break; // the server really refused the refresh token: fall back to Steam
                    default:
                        // timeout / 429 / 5xx / 403: keep the tokens, do NOT start a Steam sign-in
                        return refresh.Failure!;
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
        if (_steamIdentityChecked || TicketProviderOverride != null)
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
        return (await AuthenticateAsync(ignoreValidAccessToken: true, ct)).Ok;
    }

    private async Task<RefreshResult> TryRefreshAsync(CancellationToken ct)
    {
        var result = await _api.RefreshSessionAsync(Session.RefreshToken!, ct);
        if (result.IsOk && result.Data?.AccessToken != null)
        {
            ApplyTokens(result.Data);
            Log?.Invoke("Session renewed via refresh token.");
            return new RefreshResult(RefreshOutcome.Renewed, null);
        }

        // The refresh token is thrown away ONLY when the server explicitly rejects it (400/401).
        if (result.StatusCode is 400 or 401)
        {
            Log?.Invoke("Refresh token rejected (HTTP " + result.StatusCode + "), a Steam sign-in is needed.");
            Session.ClearTokens();
            Session.Save();
            return new RefreshResult(RefreshOutcome.Rejected, null);
        }

        Log?.Invoke("Could not renew the session (" + (result.Error ?? "unexpected response") +
                    "), keeping the saved tokens.");
        return new RefreshResult(RefreshOutcome.Failed, FromHttp(result, "Session refresh"));
    }

    private async Task<AuthResult> LoginWithSteamAsync(CancellationToken ct)
    {
        var ticketOverride = TicketProviderOverride;
        if (ticketOverride == null && !SteamAuth.Init())
        {
            Log?.Invoke("Steam unavailable: " + SteamAuth.LastError);
            return new AuthResult(false, PollFailure.Local, Error: SteamAuth.LastError);
        }

        try
        {
            Log?.Invoke("Requesting a Steam ticket...");
            var ticket = ticketOverride != null
                ? await ticketOverride(ct)
                : await SteamAuth.GetWebApiTicketAsync(ct);
            var login = await _api.LoginSilentAsync(ticket, ct);
            if (!login.IsOk || login.Data?.AccessToken == null)
            {
                Log?.Invoke("Sign-in failed: " + (login.Error ?? "no token in the response"));
                return FromHttp(login, "Sign-in");
            }

            ApplyTokens(login.Data);
            if (ticketOverride == null && SteamAuth.TryGetSteamId(out var steamId))
            {
                Session.SteamId = steamId.ToString();
            }

            // always re-read the profile: nickname and accountId depend on the account
            Session.AccountId = null;
            Session.UserName = null;
            Session.Save();
            Log?.Invoke("Signed in.");

            var profile = await EnsureProfileAsync(ct);
            if (!profile.Ok)
            {
                Log?.Invoke("Could not load the profile (aid/uid) - lobby requests may not work.");
            }

            return profile;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log?.Invoke("Steam sign-in failed: " + ex.Message);
            return new AuthResult(false, PollFailure.Local, Error: ex.Message);
        }
    }

    private async Task<AuthResult> EnsureProfileAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(Session.AccountId) && !string.IsNullOrEmpty(Session.UserName))
        {
            return AuthResult.Success;
        }

        var profile = await _api.GetProfileAsync(Session.AccessToken!, ct);
        if (!profile.IsOk || profile.Data == null)
        {
            Log?.Invoke("Profile: " + (profile.Error ?? "empty response"));
            return FromHttp(profile, "Profile");
        }

        Session.AccountId = profile.Data.NormalizedAccountId;
        Session.UserName = profile.Data.UserName ?? string.Empty;
        Session.Save();
        return string.IsNullOrEmpty(Session.AccountId)
            ? new AuthResult(false, PollFailure.Backoff, Error: "Profile: the server sent no account id")
            : AuthResult.Success;
    }

    /// <summary>Turns a failed HTTP result into an auth failure: 403 stops, everything else backs off.</summary>
    private static AuthResult FromHttp<T>(HttpResult<T> result, string what)
        => new(
            false,
            PollFailure.Backoff,
            Stop: result.IsForbidden,
            RetryAfter: result.RetryAfter,
            Error: what + ": " + (result.Error ?? "empty response"));

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

    private sealed record AuthResult(
        bool Ok,
        PollFailure Failure = PollFailure.None,
        bool Stop = false,
        TimeSpan? RetryAfter = null,
        string? Error = null)
    {
        public static readonly AuthResult Success = new(true);
    }

    private enum RefreshOutcome
    {
        Renewed,
        Rejected,
        Failed
    }

    private sealed record RefreshResult(RefreshOutcome Outcome, AuthResult? Failure);
}
