namespace AutoToolCatalog.Services;

public static class PlaywrightBootstrap
{
    private static int _initialized;

    public static void EnsureBrowsersInstalled(ILogger logger)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            return;

        try
        {
            logger.LogInformation("Checking Playwright Chromium installation...");
            var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (exitCode != 0)
                logger.LogWarning("Playwright browser install exited with code {ExitCode}", exitCode);
            else
                logger.LogInformation("Playwright Chromium is ready.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to install Playwright browsers. Run: powershell bin/Debug/net10.0/playwright.ps1 install chromium");
        }
    }
}
