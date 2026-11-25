namespace BazaarTracker;

/// <summary>
/// Диагностический режим: вход, один опрос, вывод в консоль.
/// Запуск: BazaarTracker.exe --selftest  (или dotnet BazaarTracker.dll --selftest)
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
            // консоль может не поддерживать UTF-8
        }

        var settings = AppSettings.Load();
        var session = SessionData.Load();
        using var service = new TrackerService(settings, session);
        service.Log += message => Console.WriteLine("[log] " + message);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        Console.WriteLine($"Сервер: {settings.TournamentBaseUrl}");
        if (!await service.EnsureAuthenticatedAsync(force: true, cts.Token))
        {
            Console.WriteLine("РЕЗУЛЬТАТ: вход не выполнен.");
            return 2;
        }

        Console.WriteLine($"Вход выполнен: {session.UserName} ({session.AccountId})");

        if (switchAccount)
        {
            Console.WriteLine("Проверяю смену аккаунта (сброс сессии + повторный вход)…");
            if (!await service.SwitchAccountAsync(cts.Token))
            {
                Console.WriteLine("РЕЗУЛЬТАТ: повторный вход не выполнен.");
                return 4;
            }

            Console.WriteLine($"Повторный вход выполнен: {session.UserName} ({session.AccountId})");
        }

        var result = await service.PollAsync(cts.Token);
        Console.WriteLine($"Лобби: {result.Lobbies.Length}; очередь: " +
                          (result.Queue is { } q ? $"{q.QueuedCount}/{q.MaxPlayers}" : "нет данных") +
                          "; ошибка: " + (result.LobbyError ?? "нет"));
        foreach (var lobby in result.Lobbies)
        {
            Console.WriteLine(
                $"  «{lobby.DisplayName}» | хост {lobby.HostUsername} | {lobby.PlayersText} | код {lobby.Code} | до {lobby.ExpiresAt:HH:mm}");
        }

        return result.LobbyError == null ? 0 : 3;
    }
}
