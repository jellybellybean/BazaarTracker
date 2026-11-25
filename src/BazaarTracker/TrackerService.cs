namespace BazaarTracker;

public sealed record PollResult(
    LobbyDto[] Lobbies,
    QueueStatusDto? Queue,
    string? LobbyError,
    string? QueueError);

/// <summary>
/// Авторизация (Steam-тикет → JWT → refresh-токен) и опрос сервера лобби.
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
                "Нет доступа к серверу: " + (SteamAuth.LastError ?? "авторизация не выполнена"),
                null);
        }

        var lobbies = await _api.GetLobbiesAsync(Session, ct);
        if (lobbies.IsUnauthorized)
        {
            Log?.Invoke("Токен истёк, повторяю вход…");
            if (!await EnsureAuthenticatedAsync(force: true, ct))
            {
                return new PollResult(Array.Empty<LobbyDto>(), null, "Сессия истекла, войти заново не удалось.", null);
            }

            lobbies = await _api.GetLobbiesAsync(Session, ct);
        }

        if (!lobbies.IsOk)
        {
            return new PollResult(
                Array.Empty<LobbyDto>(),
                null,
                "Список лобби: " + (lobbies.Error ?? "неизвестная ошибка"),
                null);
        }

        var queue = await _api.GetQueueAsync(Session, ct);
        return new PollResult(
            lobbies.Data ?? Array.Empty<LobbyDto>(),
            queue.IsOk ? queue.Data : null,
            null,
            queue.IsOk ? null : "Очередь: " + (queue.Error ?? "нет данных"));
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
    /// Один раз за запуск сверяет сохранённую сессию с текущим Steam-аккаунтом.
    /// Так приложение не «залипает» на чужом или старом аккаунте.
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
            Log?.Invoke("В Steam другой аккаунт — сбрасываю сохранённую сессию.");
            Session.ClearTokens();
            Session.AccountId = null;
            Session.UserName = null;
        }

        Session.SteamId = id;
        Session.Save();
    }

    /// <summary>
    /// Полный выход из текущей сессии и вход под тем Steam-аккаунтом, который запущен сейчас.
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

        // переподключаемся к Steam: аккаунт мог смениться в клиенте Steam
        SteamAuth.Shutdown();
        _steamIdentityChecked = true;
        Log?.Invoke("Сессия сброшена, вхожу заново.");
        return await EnsureAuthenticatedAsync(force: true, ct);
    }

    private async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        var result = await _api.RefreshSessionAsync(Session.RefreshToken!, ct);
        if (!result.IsOk || result.Data?.AccessToken == null)
        {
            Log?.Invoke("Refresh-токен не принят, нужен вход через Steam.");
            Session.ClearTokens();
            return false;
        }

        ApplyTokens(result.Data);
        Log?.Invoke("Сессия обновлена по refresh-токену.");
        return true;
    }

    private async Task<bool> LoginWithSteamAsync(CancellationToken ct)
    {
        if (!SteamAuth.Init())
        {
            Log?.Invoke("Steam недоступен: " + SteamAuth.LastError);
            return false;
        }

        try
        {
            Log?.Invoke("Запрашиваю Steam-тикет…");
            var ticket = await SteamAuth.GetWebApiTicketAsync(ct);
            var login = await _api.LoginSilentAsync(ticket, ct);
            if (!login.IsOk || login.Data?.AccessToken == null)
            {
                Log?.Invoke("Вход не удался: " + (login.Error ?? "нет токена в ответе"));
                return false;
            }

            ApplyTokens(login.Data);
            if (SteamAuth.TryGetSteamId(out var steamId))
            {
                Session.SteamId = steamId.ToString();
            }

            // профиль перечитываем всегда: ник и accountId зависят от аккаунта
            Session.AccountId = null;
            Session.UserName = null;
            Session.Save();
            Log?.Invoke("Вход выполнен.");

            if (!await EnsureProfileAsync(ct))
            {
                Log?.Invoke("Не удалось загрузить профиль (aid/uid) — запросы к лобби могут не работать.");
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
            Log?.Invoke("Вход через Steam не удался: " + ex.Message);
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
            Log?.Invoke("Профиль: " + (profile.Error ?? "пустой ответ"));
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
