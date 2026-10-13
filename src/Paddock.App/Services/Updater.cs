using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Paddock.Core.Services;

namespace Paddock.App.Services;

/// <summary>
/// Asks GitHub for the latest padx release and, on request, downloads its
/// installer and hands over to it. Only ever runs when the user clicks
/// "Check for updates" — padx makes no network calls on its own.
/// </summary>
internal static class Updater
{
    private const string LatestRelease = "https://api.github.com/repos/serkanyardim/padx/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion => typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>The latest release, or null when GitHub could not be reached.</summary>
    public static async Task<ReleaseInfo?> GetLatestAsync()
    {
        try
        {
            using var response = await Http.GetAsync(LatestRelease);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warn($"Update check: GitHub answered {(int)response.StatusCode}.");
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            var version = UpdateCheck.ParseTag(root.GetProperty("tag_name").GetString());
            if (version is null)
                return null;

            string? setup = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                if (name.StartsWith("padx-setup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    setup = asset.GetProperty("browser_download_url").GetString();
            }

            return new ReleaseInfo(version, root.GetProperty("html_url").GetString() ?? string.Empty, setup);
        }
        catch (Exception ex)
        {
            Log.Warn($"Update check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Downloads the installer to %TEMP% and starts it. The installer replaces
    /// padx (pads, settings and configs are kept) and starts the new version.
    /// Returns false if the download failed; on success the caller should exit.
    /// </summary>
    public static async Task<bool> DownloadAndRunAsync(ReleaseInfo release, IProgress<int>? progress = null)
    {
        if (release.SetupUrl is null)
            return false;

        var file = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(release.SetupUrl).LocalPath));

        try
        {
            using (var response = await Http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;

                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(file);

                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    if (total > 0)
                        progress?.Report((int)(done * 100 / total));
                }
            }

            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            Log.Info($"Update: started the installer for {release.Version}.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Downloading the update failed.", ex);
            return false;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"padx/{CurrentVersion.ToString(3)}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
