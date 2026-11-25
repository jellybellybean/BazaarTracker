namespace BazaarTracker;

internal static class AppInfo
{
    public const uint SteamAppId = 1617400;
    public const string SteamAppIdText = "1617400";
    public const string Platform = "Steam";
    public const string ClientFlavor = "Paid";

    // Defaults: The Bazaar PTR server (taken from the client, Config.cs).
    public const string DefaultCoreBaseUrl = "https://temponet-core-ptr-app-srvce.azurewebsites.net";
    public const string DefaultTournamentBaseUrl = "https://tournament-ptr-app.orangestone-41b95ee4.westus2.azurecontainerapps.io";

    public static string AppDataDir
    {
        get
        {
            // BAZAARTRACKER_DATA_DIR is only used by the offline self-tests so they never touch the real session
            var dir = Environment.GetEnvironmentVariable("BAZAARTRACKER_DATA_DIR") is { Length: > 0 } custom
                ? custom
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BazaarTracker");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
