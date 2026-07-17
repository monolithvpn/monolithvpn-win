using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace MonolithVpnClient.Services;

public record UpdateCheckResult(bool UpdateAvailable, string CurrentVersion, string? LatestVersion, string? Notes, string? DownloadUrl);

public record AutoUpdateResult(bool Success, string? Error);

public static class UpdateService
{
    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static async Task<UpdateCheckResult> CheckAsync(ApiClient api)
    {
        var current = CurrentVersion;
        try
        {
            var result = await api.GetAppVersionAsync();
            if (string.IsNullOrWhiteSpace(result.Version))
                return new UpdateCheckResult(false, current, null, null, null);

            bool isNewer = Version.TryParse(result.Version, out var latestVer)
                && Version.TryParse(current, out var currentVer)
                && latestVer > currentVer;

            return new UpdateCheckResult(isNewer, current, result.Version, result.ReleaseNotes, result.DownloadUrl);
        }
        catch
        {
            return new UpdateCheckResult(false, current, null, null, null);
        }
    }

    public static void OpenDownload(string url)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    public static async Task<AutoUpdateResult> PerformAutoUpdateAsync(ApiClient api, string downloadUrl)
    {
        try
        {
            var installerBytes = await api.DownloadAppUpdateAsync(downloadUrl);
            string tempPath = Path.Combine(Path.GetTempPath(), $"MonolithVPN-Setup-{Guid.NewGuid():N}.exe");
            await File.WriteAllBytesAsync(tempPath, installerBytes);

            using var _ = Process.Start(new ProcessStartInfo(tempPath)
            {
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true,
            });
            return new AutoUpdateResult(true, null);
        }
        catch (Exception ex)
        {
            return new AutoUpdateResult(false, ex.Message);
        }
    }
}
