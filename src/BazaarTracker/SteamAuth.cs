using Steamworks;

namespace BazaarTracker;

/// <summary>
/// Obtains a Steam ticket for the web API (the same way the game itself does:
/// ISteamUser::GetAuthTicketForWebApi with AppID 1617400). The ticket is only needed for
/// the initial sign-in; after that the app lives on the refresh token.
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
                    LastError = "SteamAPI.Init() could not connect to Steam. Make sure Steam is running, " +
                                "and that the current Steam account owns The Bazaar (AppID 1617400).";
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
                LastError = "Steam initialization error: " + ex.Message;
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
                // callbacks must not crash the app
            }

            Thread.Sleep(100);
        }
    }

    /// <summary>
    /// SteamID64 of the current user. Initializes Steamworks if that hasn't been done yet.
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

    /// <summary>Returns the ticket as lowercase hex, as /api/auth/login/silent expects it.</summary>
    public static async Task<string> GetWebApiTicketAsync(CancellationToken cancellationToken)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException(LastError ?? "Steam is not initialized.");
        }

        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = Callback<GetTicketForWebApiResponse_t>.Create(response =>
        {
            if (response.m_eResult != EResult.k_EResultOK)
            {
                tcs.TrySetException(new InvalidOperationException(
                    $"Steam returned an error while getting the ticket: {response.m_eResult}"));
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
            throw new InvalidOperationException("Steam did not issue a ticket (HAuthTicket.Invalid).");
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
                // the ticket may already have been used/cancelled
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
                // ignore
            }

            IsInitialized = false;
        }
    }
}
