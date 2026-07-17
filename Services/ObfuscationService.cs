using System.Diagnostics;
using System.IO;

namespace MonolithVpnClient.Services;

public static class ObfuscationService
{
    private static readonly string SharedKey =
        Obfuscation.Reveal("DxVaPjYIRwwEFT5KWwhdQWJbLC58G1kNAAcRABwbD34=");

    private static Process? _process;
    private static int? _activeLocalPort;

    public static bool IsRunning => _process is { HasExited: false };

    public static int LocalPortFor(int serverId) => 28800 + (serverId % 1000);

    private static string ExtractHelperExe()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MonolithVPN");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "udp2raw_mp.exe");

        using var resourceStream = typeof(ObfuscationService).Assembly.GetManifestResourceStream("udp2raw_mp.exe")
            ?? throw new InvalidOperationException("udp2raw_mp.exe resource is missing from this build - reinstall the app.");

        if (!File.Exists(path) || new FileInfo(path).Length != resourceStream.Length)
        {
            using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            resourceStream.CopyTo(fileStream);
        }

        return path;
    }

    public static async Task<bool> StartAsync(int serverId, string remoteHost, int remotePort)
    {
        await StopAsync();

        string exePath = ExtractHelperExe();

        int localPort = LocalPortFor(serverId);
        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("-l");
        psi.ArgumentList.Add($"127.0.0.1:{localPort}");
        psi.ArgumentList.Add("-r");
        psi.ArgumentList.Add($"{remoteHost}:{remotePort}");
        psi.ArgumentList.Add("--raw-mode");
        psi.ArgumentList.Add("faketcp");
        psi.ArgumentList.Add("-k");
        psi.ArgumentList.Add(SharedKey);
        psi.ArgumentList.Add("--cipher-mode");
        psi.ArgumentList.Add("aes128cbc");
        psi.ArgumentList.Add("--auth-mode");
        psi.ArgumentList.Add("md5");
        psi.ArgumentList.Add("--log-level");
        psi.ArgumentList.Add("2");

        _process = Process.Start(psi);
        _activeLocalPort = localPort;

        await Task.Delay(2500);

        if (_process is null || _process.HasExited)
        {
            _activeLocalPort = null;
            return false;
        }
        return true;
    }

    public static Task StopAsync()
    {
        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); }
            catch { }
        }
        _process = null;
        _activeLocalPort = null;
        return Task.CompletedTask;
    }
}
