namespace BazaarTracker;

/// <summary>
/// Diagnostic mode: sign in, poll once, print to the console.
/// Run: BazaarTracker.exe --selftest  (or dotnet BazaarTracker.dll --selftest)
/// </summary>
internal static class SelfTest
{
    public static async Task<int> RunAsync(bool switchAccount = false)
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
            // the console may not support UTF-8
        }

        var settings = AppSettings.Load();
        var session = SessionData.Load();
        using var service = new TrackerService(settings, session);
        service.Log += message => Console.WriteLine("[log] " + message);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        Console.WriteLine($"Server: {settings.TournamentBaseUrl}");
        if (!await service.EnsureAuthenticatedAsync(force: true, cts.Token))
        {
            Console.WriteLine("RESULT: sign-in failed.");
            return 2;
        }

        Console.WriteLine($"Signed in: {session.UserName} ({session.AccountId})");

        if (switchAccount)
        {
            Console.WriteLine("Checking account switch (session reset + sign in again)...");
            if (!await service.SwitchAccountAsync(cts.Token))
            {
                Console.WriteLine("RESULT: second sign-in failed.");
                return 4;
            }

            Console.WriteLine($"Signed in again: {session.UserName} ({session.AccountId})");
        }

        var result = await service.PollAsync(cts.Token);
        Console.WriteLine($"Lobbies: {result.Lobbies.Length}; queue: " +
                          (result.Queue is { } q ? $"{q.QueuedCount}/{q.MaxPlayers}" : "no data") +
                          "; error: " + (result.LobbyError ?? "none"));
        foreach (var lobby in result.Lobbies)
        {
            Console.WriteLine(
                $"  '{lobby.DisplayName}' | host {lobby.HostUsername} | {lobby.PlayersText} | code {lobby.Code} | until {lobby.ExpiresAt:HH:mm}");
        }

        return result.LobbyError == null ? 0 : 3;
    }
}
