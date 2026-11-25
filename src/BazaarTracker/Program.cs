using System.Threading;

namespace BazaarTracker;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--selftest-backoff", StringComparer.OrdinalIgnoreCase))
        {
            return SelfTest.RunBackoffChecks();
        }

        if (args.Contains("--selftest-http", StringComparer.OrdinalIgnoreCase))
        {
            return HttpSelfTest.RunAsync().GetAwaiter().GetResult();
        }

        if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--selftest-switch", StringComparer.OrdinalIgnoreCase))
        {
            var switchAccount = args.Contains("--selftest-switch", StringComparer.OrdinalIgnoreCase);
            return SelfTest.RunAsync(switchAccount).GetAwaiter().GetResult();
        }

        using var mutex = new Mutex(true, @"Local\BazaarTracker.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "BazaarTracker is already running - you can open its window from the tray icon.",
                "BazaarTracker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        var startHidden = args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        Application.Run(new TrackerForm(startHidden));
        return 0;
    }
}
