using Steamworks;

namespace BazaarTracker;

/// <summary>
/// Получение Steam-тикета для веб-API (тот же способ, что использует сама игра:
/// ISteamUser::GetAuthTicketForWebApi с AppID 1617400). Тикет нужен только для
/// первичного входа; дальше приложение живёт на refresh-токене.
/// </summary>
public static class SteamAuth
{
    private static readonly object Gate = new();
    private static Thread? _callbackThread;
    private static volatile bool _running;

    public static bool IsInitialized { get; private set; }

    public static string? LastError { get; private set; }

    public static bool Init()
    {
        lock (Gate)
        {
            if (IsInitialized)
            {
                return true;
            }

            try
            {
                Environment.SetEnvironmentVariable("SteamAppId", AppInfo.SteamAppIdText);
                Environment.SetEnvironmentVariable("SteamGameId", AppInfo.SteamAppIdText);

                if (!SteamAPI.Init())
                {
                    LastError = "SteamAPI.Init() не смог подключиться к Steam. Проверьте, что Steam запущен, " +
                                "и что на текущем Steam-аккаунте куплена The Bazaar (AppID 1617400).";
                    return false;
                }

                IsInitialized = true;
                _running = true;
                _callbackThread = new Thread(CallbackLoop)
                {
                    IsBackground = true,
                    Name = "SteamCallbacks"
                };
                _callbackThread.Start();
                return true;
            }
            catch (Exception ex)
            {
                LastError = "Ошибка инициализации Steam: " + ex.Message;
                return false;
            }
        }
    }

    private static void CallbackLoop()
    {
        while (_running)
        {
            try
            {
                SteamAPI.RunCallbacks();
            }
            catch
            {
                // колбэки не должны ронять приложение
            }

            Thread.Sleep(100);
        }
    }

    /// <summary>
    /// SteamID64 текущего пользователя. Инициализирует Steamworks, если это ещё не сделано.
    /// </summary>
    public static bool TryGetSteamId(out ulong steamId)
    {
        steamId = 0;
        if (!Init())
        {
            return false;
        }

        try
        {
            steamId = SteamUser.GetSteamID().m_SteamID;
            return steamId != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Возвращает тикет в hex-виде (нижний регистр), как его ждёт /api/auth/login/silent.</summary>
    public static async Task<string> GetWebApiTicketAsync(CancellationToken cancellationToken)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException(LastError ?? "Steam не инициализирован.");
        }

        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = Callback<GetTicketForWebApiResponse_t>.Create(response =>
        {
            if (response.m_eResult != EResult.k_EResultOK)
            {
                tcs.TrySetException(new InvalidOperationException(
                    $"Steam вернул ошибку при получении тикета: {response.m_eResult}"));
                return;
            }

            var ticket = new byte[response.m_cubTicket];
            if (response.m_cubTicket > 0)
            {
                Array.Copy(response.m_rgubTicket, ticket, response.m_cubTicket);
            }

            tcs.TrySetResult(ticket);
        });

        var handle = SteamUser.GetAuthTicketForWebApi(null);
        if (handle == HAuthTicket.Invalid)
        {
            throw new InvalidOperationException("Steam не выдал тикет (HAuthTicket.Invalid).");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var registration2 = linked.Token.Register(() => tcs.TrySetCanceled(linked.Token));

        byte[] bytes;
        try
        {
            bytes = await tcs.Task;
        }
        finally
        {
            try
            {
                SteamUser.CancelAuthTicket(handle);
            }
            catch
            {
                // тикет уже мог быть использован/отменён
            }
        }

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            if (!IsInitialized)
            {
                return;
            }

            _running = false;
            try
            {
                SteamAPI.Shutdown();
            }
            catch
            {
                // игнорируем
            }

            IsInitialized = false;
        }
    }
}
