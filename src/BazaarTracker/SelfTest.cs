namespace BazaarTracker;

/// <summary>
/// Diagnostic mode: sign in, poll once, print to the console.
/// Run: BazaarTracker.exe --selftest  (or dotnet BazaarTracker.dll --selftest)
/// </summary>
internal static class SelfTest
{
    /// <summary>
    /// Offline checks of the backoff logic (no Steam, no network):
    /// BazaarTracker.exe --selftest-backoff
    /// </summary>
    public static int RunBackoffChecks()
    {
        var failed = 0;

        void Check(string name, bool ok)
        {
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
            if (!ok)
            {
                failed++;
            }
        }

        var interval = TimeSpan.FromSeconds(15);

        // no failures: the normal interval, whatever the random source says
        var calm = new PollBackoff(new Random(1));
        Check("success keeps the normal interval", calm.NextDelay(interval, failed: false) == interval);

        // delays grow, never drop below the interval, never exceed the cap
        var b = new PollBackoff(new Random(42));
        var previousUpperBound = TimeSpan.Zero;
        var allInRange = true;
        var reachedCap = false;
        for (var i = 1; i <= 20; i++)
        {
            var d = b.NextDelay(interval, failed: true);
            var upper = TimeSpan.FromTicks((long)Math.Min(interval.Ticks * Math.Pow(2, Math.Min(i, 10)), PollBackoff.MaxDelay.Ticks));
            allInRange &= d >= interval && d <= upper && d <= PollBackoff.MaxDelay;
            allInRange &= upper >= previousUpperBound;
            previousUpperBound = upper;
            reachedCap |= upper == PollBackoff.MaxDelay;
        }

        Check("failures: delay in [interval, min(2^n * interval, cap)] and the cap is reached", allInRange && reachedCap);
        Check("cap is a few minutes (<= 5 min)", PollBackoff.MaxDelay <= TimeSpan.FromMinutes(5));

        // jitter: not every delay is the same
        var jitter = new PollBackoff(new Random(7));
        var samples = Enumerable.Range(0, 30).Select(_ => { jitter.Reset(); jitter.NextDelay(interval, true); return jitter.NextDelay(interval, true); }).Distinct().Count();
        Check("jitter spreads the delays", samples > 5);

        // success resets
        b.NextDelay(interval, failed: true);
        var afterSuccess = b.NextDelay(interval, failed: false);
        Check("a success resets the backoff", afterSuccess == interval && b.Failures == 0);

        // Retry-After is honoured as a minimum, even when there is no failure
        var r = new PollBackoff(new Random(3));
        var retry = TimeSpan.FromSeconds(120);
        var withRetry = r.NextDelay(interval, failed: false, retryAfter: retry);
        Check("Retry-After 120 s is never undercut", withRetry >= retry && withRetry <= retry * 1.1);

        // Retry-After shorter than the computed backoff does not shorten the backoff
        var r2 = new PollBackoff(new Random(3));
        for (var i = 0; i < 6; i++)
        {
            r2.NextDelay(interval, failed: true);
        }

        var longBackoff = r2.NextDelay(interval, failed: true, retryAfter: TimeSpan.FromSeconds(1));
        Check("short Retry-After does not shorten the backoff", longBackoff >= TimeSpan.FromSeconds(150));

        // absurd Retry-After is limited
        var r3 = new PollBackoff(new Random(3));
        var absurd = r3.NextDelay(interval, failed: true, retryAfter: TimeSpan.FromDays(3));
        Check("Retry-After is limited to 1 h (+10 % jitter)", absurd <= PollBackoff.MaxRetryAfter * 1.1);

        // a slow configured interval is never undercut by the backoff
        var slow = TimeSpan.FromSeconds(300);
        var s = new PollBackoff(new Random(5));
        Check("backoff never goes below a long interval", Enumerable.Range(0, 5).All(_ => s.NextDelay(slow, true) >= slow));

        Console.WriteLine(failed == 0 ? "ALL CHECKS PASSED" : $"{failed} CHECK(S) FAILED");
        return failed == 0 ? 0 : 1;
    }

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
