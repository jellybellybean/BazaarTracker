using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BazaarTracker;

/// <summary>
/// Offline checks of the sign-in / retry rules against a fake local server (no Steam, no real network):
/// BazaarTracker.exe --selftest-http
/// </summary>
internal static class HttpSelfTest
{
    private sealed record Reply(int Status, string Body = "", string? RetryAfter = null);

    private sealed record Outcome(PollResult Result, SessionData Session, Dictionary<string, int> Calls, int Tickets)
    {
        public int Count(string path) => Calls.GetValueOrDefault(path);
    }

    private const string Lobbies = "/lobbies";
    private const string Queue = "/queue";
    private const string Refresh = "/api/auth/refreshtokens";
    private const string Login = "/api/auth/login/silent";
    private const string Profile = "/api/PlayerProfiles/me";

    public static async Task<int> RunAsync()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "BazaarTracker-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        Environment.SetEnvironmentVariable("BAZAARTRACKER_DATA_DIR", dataDir);

        var failed = 0;

        void Check(string name, bool ok)
        {
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
            if (!ok)
            {
                failed++;
            }
        }

        try
        {
            // 1. a 401 renews the token with the refresh token and does NOT start a Steam sign-in
            var o = await RunScenarioAsync((p, n) => p == Lobbies && n == 1 ? new Reply(401) : null);
            Check("401 -> refresh token is used, poll succeeds",
                o.Result.LobbyError == null && o.Count(Refresh) == 1 && o.Count(Lobbies) == 2 && o.Session.AccessToken == "a2");
            Check("401 -> no Steam sign-in", o.Tickets == 0 && o.Count(Login) == 0);

            // 2. a 401 that does not go away: one refresh per poll, no Steam sign-in, back off
            o = await RunScenarioAsync((p, _) => p == Lobbies ? new Reply(401) : null);
            Check("persistent 401 -> exactly one refresh + one retry, then backoff",
                o.Result.Failure == PollFailure.Backoff && !o.Result.StopPolling && o.Count(Refresh) == 1 && o.Count(Lobbies) == 2);
            Check("persistent 401 -> still no Steam sign-in", o.Tickets == 0 && o.Count(Login) == 0);

            // 3. a 403 stops: no refresh, no sign-in, tokens untouched
            o = await RunScenarioAsync((p, _) => p == Lobbies ? new Reply(403, "forbidden") : null);
            Check("403 -> polling stops with the error", o.Result.StopPolling && o.Result.LobbyError!.Contains("403"));
            Check("403 -> no refresh, no Steam sign-in, tokens kept",
                o.Count(Refresh) == 0 && o.Tickets == 0 && o.Session.RefreshToken == "r1" && o.Session.AccessToken == "a1");

            // 4. refresh fails with a 5xx: keep the tokens, no Steam sign-in
            o = await RunScenarioAsync((p, _) => p == Refresh ? new Reply(500) : null, expireAccessToken: true);
            Check("refresh 500 -> refresh token kept", o.Session.RefreshToken == "r1");
            Check("refresh 500 -> no Steam sign-in, backoff", o.Tickets == 0 && o.Count(Login) == 0 && o.Result.Failure == PollFailure.Backoff);

            // 5. refresh answered with 429 + Retry-After
            o = await RunScenarioAsync((p, _) => p == Refresh ? new Reply(429, "", "45") : null, expireAccessToken: true);
            Check("refresh 429 -> refresh token kept, no Steam sign-in", o.Session.RefreshToken == "r1" && o.Tickets == 0);
            Check("refresh 429 -> Retry-After 45 s is passed on", o.Result.RetryAfter == TimeSpan.FromSeconds(45));

            // 6. the refresh endpoint is unreachable (connection error / timeout class)
            o = await RunScenarioAsync(null, expireAccessToken: true, unreachableCore: true);
            Check("refresh unreachable -> refresh token kept", o.Session.RefreshToken == "r1");
            Check("refresh unreachable -> no Steam sign-in, backoff", o.Tickets == 0 && o.Result.Failure == PollFailure.Backoff);

            // 7. only an explicit 400/401 on refresh leads to a Steam sign-in
            foreach (var status in new[] { 400, 401 })
            {
                o = await RunScenarioAsync((p, _) => p == Refresh ? new Reply(status) : null, expireAccessToken: true);
                Check($"refresh {status} -> Steam sign-in once, new tokens, poll succeeds",
                    o.Tickets == 1 && o.Count(Login) == 1 && o.Session.RefreshToken == "r3" && o.Result.LobbyError == null);
            }

            // 8. refresh answered with 403: stop, keep tokens
            o = await RunScenarioAsync((p, _) => p == Refresh ? new Reply(403) : null, expireAccessToken: true);
            Check("refresh 403 -> polling stops, tokens kept, no Steam sign-in",
                o.Result.StopPolling && o.Session.RefreshToken == "r1" && o.Tickets == 0);

            // 9. 5xx on the lobby list: no re-auth at all, Retry-After is passed on
            o = await RunScenarioAsync((p, _) => p == Lobbies ? new Reply(503, "", "60") : null);
            Check("lobbies 503 + Retry-After 60 -> backoff with Retry-After",
                o.Result.Failure == PollFailure.Backoff && o.Result.RetryAfter == TimeSpan.FromSeconds(60));
            Check("lobbies 503 -> no refresh, no Steam sign-in", o.Count(Refresh) == 0 && o.Tickets == 0);

            // 10. 429 on the queue only: lobbies still shown, Retry-After honoured, not a failure
            o = await RunScenarioAsync((p, _) => p == Queue ? new Reply(429, "", "30") : null);
            Check("queue 429 -> lobbies returned, Retry-After 30 s passed on, not a failure",
                o.Result.LobbyError == null && o.Result.Failure == PollFailure.None && o.Result.RetryAfter == TimeSpan.FromSeconds(30));

            // 10b. a garbled body is just a failed poll
            o = await RunScenarioAsync((p, _) => p == Lobbies ? new Reply(200, "<html>oops</html>") : null);
            Check("garbled lobby response -> backoff, no re-login, tokens kept",
                o.Result.Failure == PollFailure.Backoff && o.Count(Refresh) == 0 && o.Tickets == 0 && o.Session.RefreshToken == "r1");

            // 11. healthy case: nothing extra happens
            o = await RunScenarioAsync(null);
            Check("healthy poll -> no refresh, no Steam sign-in",
                o.Result.LobbyError == null && o.Count(Refresh) == 0 && o.Tickets == 0 && o.Count(Lobbies) == 1);
        }
        finally
        {
            try
            {
                Directory.Delete(dataDir, recursive: true);
            }
            catch
            {
                // temp folder, not important
            }
        }

        Console.WriteLine(failed == 0 ? "ALL CHECKS PASSED" : $"{failed} CHECK(S) FAILED");
        return failed == 0 ? 0 : 1;
    }

    private static async Task<Outcome> RunScenarioAsync(
        Func<string, int, Reply?>? overrides,
        bool expireAccessToken = false,
        bool unreachableCore = false)
    {
        var accountId = Guid.NewGuid().ToString("N");
        var tokenExpiry = DateTimeOffset.UtcNow.AddHours(1).ToString("o");

        Reply Default(string path) => path switch
        {
            Lobbies => new Reply(200, "[]"),
            Queue => new Reply(200, "{\"state\":\"idle\",\"queuedCount\":0,\"maxPlayers\":8}"),
            Refresh => new Reply(200, $"{{\"accessToken\":\"a2\",\"refreshToken\":\"r2\",\"accessTokenExpiresAt\":\"{tokenExpiry}\"}}"),
            Login => new Reply(200, $"{{\"accessToken\":\"a3\",\"refreshToken\":\"r3\",\"accessTokenExpiresAt\":\"{tokenExpiry}\"}}"),
            Profile => new Reply(200, $"{{\"accountId\":\"{Guid.ParseExact(accountId, "N")}\",\"userName\":\"tester\"}}"),
            _ => new Reply(404)
        };

        using var server = new FakeServer((path, n) => overrides?.Invoke(path, n) ?? Default(path));

        var settings = new AppSettings
        {
            CoreBaseUrl = unreachableCore ? "http://127.0.0.1:1" : server.Url,
            TournamentBaseUrl = server.Url
        };

        var session = new SessionData
        {
            AccessToken = "a1",
            RefreshToken = "r1",
            AccessTokenExpiresAtUtc = expireAccessToken ? DateTimeOffset.UtcNow.AddHours(-1) : DateTimeOffset.UtcNow.AddHours(1),
            AccountId = accountId,
            UserName = "tester",
            SteamId = "1"
        };

        var tickets = 0;
        using var service = new TrackerService(settings, session)
        {
            TicketProviderOverride = _ =>
            {
                Interlocked.Increment(ref tickets);
                return Task.FromResult("fake-ticket");
            }
        };

        var result = await service.PollAsync(CancellationToken.None);
        return new Outcome(result, session, server.Snapshot(), tickets);
    }

    private sealed class FakeServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Func<string, int, Reply> _handler;
        private readonly Dictionary<string, int> _calls = new();
        private readonly object _gate = new();

        public FakeServer(Func<string, int, Reply> handler)
        {
            _handler = handler;

            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            Url = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(Url + "/");
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        public string Url { get; }

        public Dictionary<string, int> Snapshot()
        {
            lock (_gate)
            {
                return new Dictionary<string, int>(_calls);
            }
        }

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                var path = ctx.Request.Url!.AbsolutePath;
                int n;
                lock (_gate)
                {
                    n = _calls.GetValueOrDefault(path) + 1;
                    _calls[path] = n;
                }

                var reply = _handler(path, n);
                ctx.Response.StatusCode = reply.Status;
                if (reply.RetryAfter != null)
                {
                    ctx.Response.Headers["Retry-After"] = reply.RetryAfter;
                }

                var bytes = Encoding.UTF8.GetBytes(reply.Body);
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        }

        public void Dispose() => _listener.Close();
    }
}
