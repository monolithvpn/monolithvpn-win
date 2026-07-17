using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace MonolithVpnClient.Services;

public static class WireGuardService
{
    private static readonly string[] CandidatePaths =
    {
        @"C:\Program Files\WireGuard\wireguard.exe",
        @"C:\Program Files (x86)\WireGuard\wireguard.exe",
    };

    private static readonly string TunnelDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MonolithVPN", "tunnels");

    public const string OfficialDownloadPage = "https://www.wireguard.com/install/";
    private const string OfficialInstallerUrl = "https://download.wireguard.com/windows-client/wireguard-installer.exe";

    public static bool IsWireGuardInstalled => CandidatePaths.Any(File.Exists);

    public static async Task<bool> DownloadAndInstallAsync()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "wireguard-installer.exe");

        using (var http = new HttpClient())
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MonolithVPN-Client");
            var bytes = await http.GetByteArrayAsync(OfficialInstallerUrl);
            await File.WriteAllBytesAsync(tempPath, bytes);
        }

        var psi = new ProcessStartInfo(tempPath) { UseShellExecute = true };
        using var proc = Process.Start(psi)!;
        await proc.WaitForExitAsync();

        bool installed = IsWireGuardInstalled;
        if (installed) await CloseAutoLaunchedGuiAsync();
        return installed;
    }

    private static async Task CloseAutoLaunchedGuiAsync()
    {
        await Task.Delay(1500);
        foreach (var proc in Process.GetProcessesByName("wireguard"))
        {
            try
            {
                if (proc.MainWindowHandle != IntPtr.Zero) proc.CloseMainWindow();
            }
            catch
            {
            }
        }
    }

    internal static string WireGuardExe => CandidatePaths.FirstOrDefault(File.Exists)
        ?? throw new InvalidOperationException(
            "WireGuard for Windows isn't installed. Install it from wireguard.com/install, then try again.");

    public static string TunnelNameFor(int serverId) => $"monolithvpn-{serverId}";

    public static async Task ConnectAsync(int serverId, string configText, string? obfuscationHost = null, int? obfuscationPort = null)
    {
        if (AppSettings.Current.ObfuscationEnabled && obfuscationHost is not null && obfuscationPort is int port)
        {
            bool started = await ObfuscationService.StartAsync(serverId, obfuscationHost, port);
            if (!started)
                throw new InvalidOperationException(
                    "Couldn't start the traffic obfuscation layer. Check that Npcap is installed, or turn " +
                    "obfuscation off in Settings and try again.");
            configText = RewriteEndpointToLocal(configText, ObfuscationService.LocalPortFor(serverId));
        }

        Directory.CreateDirectory(TunnelDir);
        var name = TunnelNameFor(serverId);
        var confPath = Path.Combine(TunnelDir, $"{name}.conf");

        var gatewaySnapshot = SplitTunnelService.CaptureCurrentGateway();

        await SplitTunnelService.RemoveControlPlaneBypassAsync();

        await File.WriteAllTextAsync(confPath, configText);

        var result = await RunAsync("/installtunnelservice", confPath);
        if (result.ExitCode != 0)
        {
            await ObfuscationService.StopAsync();
            throw new InvalidOperationException($"Failed to start tunnel: {result.StdErr.Trim()}");
        }

        await SplitTunnelService.ApplyAsync(gatewaySnapshot);
        await StartRedirectServicesIfConfiguredAsync(serverId, gatewaySnapshot);
    }

    public static async Task DisconnectAsync(int serverId)
    {
        var name = TunnelNameFor(serverId);
        var result = await RunAsync("/uninstalltunnelservice", name);
        await ObfuscationService.StopAsync();
        await SplitTunnelService.RemoveAsync();
        await PortRedirectService.StopAsync();
        await AppSplitTunnelService.StopAsync();
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Failed to stop tunnel: {result.StdErr.Trim()}");
    }

    private static async Task StartRedirectServicesIfConfiguredAsync(int serverId, SplitTunnelService.GatewaySnapshot? gateway)
    {
        if (!AppSettings.Current.SplitTunnelEnabled || gateway is not SplitTunnelService.GatewaySnapshot gw) return;

        try
        {
            string tunnelName = TunnelNameFor(serverId);
            var tunnelNic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.Name.Equals(tunnelName, StringComparison.OrdinalIgnoreCase));
            var tunnelV4 = tunnelNic?.GetIPProperties().GetIPv4Properties();
            var tunnelLocalIp = tunnelNic?.GetIPProperties().UnicastAddresses
                .Select(u => u.Address)
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (tunnelV4 is null || tunnelLocalIp is null) return;

            var udpRanges = PortRedirectService.ParseUdpRanges(AppSettings.Current.SplitTunnelPorts);
            if (udpRanges.Count > 0)
                await PortRedirectService.StartAsync(tunnelV4.Index, tunnelLocalIp, gw.InterfaceIndex, gw.LocalIp, udpRanges);

            if (AppSettings.Current.SplitTunnelAppPaths.Count > 0)
                await AppSplitTunnelService.StartAsync(tunnelV4.Index, tunnelLocalIp, gw.InterfaceIndex, gw.LocalIp, AppSettings.Current.SplitTunnelAppPaths);
        }
        catch
        {
        }
    }

    private const int ObfuscatedMtu = 1200;

    private static string RewriteEndpointToLocal(string configText, int localPort)
    {
        var lines = configText.Replace("\r\n", "\n").Split('\n').ToList();
        bool hasMtu = false;
        int interfaceIndex = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("Endpoint", StringComparison.OrdinalIgnoreCase))
                lines[i] = $"Endpoint = 127.0.0.1:{localPort}";
            else if (trimmed.StartsWith("MTU", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"MTU = {ObfuscatedMtu}";
                hasMtu = true;
            }
            else if (trimmed.Equals("[Interface]", StringComparison.OrdinalIgnoreCase))
                interfaceIndex = i;
        }
        if (!hasMtu && interfaceIndex >= 0)
            lines.Insert(interfaceIndex + 1, $"MTU = {ObfuscatedMtu}");
        return string.Join('\n', lines);
    }

    public static async Task<bool> IsTunnelRunningAsync(int serverId)
    {
        if (await IsServiceRunningAsync(serverId)) return true;

        bool? adapterUp = IsTunnelAdapterUp(serverId);
        return adapterUp == true;
    }

    public static async Task<bool> IsDnsRespondingAsync()
    {
        try
        {
            var lookup = Dns.GetHostEntryAsync("cloudflare.com");
            var winner = await Task.WhenAny(lookup, Task.Delay(TimeSpan.FromSeconds(6)));
            return winner == lookup && lookup.IsCompletedSuccessfully;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> IsServiceRunningAsync(int serverId)
    {
        string serviceName = $"WireGuardTunnel${TunnelNameFor(serverId)}";
        try
        {
            var psi = new ProcessStartInfo("sc.exe", $"query \"{serviceName}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi)!;
            string output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static bool? IsTunnelAdapterUp(int serverId)
    {
        try
        {
            string name = TunnelNameFor(serverId);
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            return nic is null ? null : nic.OperationalStatus == OperationalStatus.Up;
        }
        catch
        {
            return null;
        }
    }

    internal static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(params string[] args)
    {
        var psi = new ProcessStartInfo(WireGuardExe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        string stdout = await proc.StandardOutput.ReadToEndAsync();
        string stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode, stdout, stderr);
    }
}
