using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace MonolithVpnClient.Services;

public static class MultiHopService
{
    public static bool IsActive { get; private set; }
    public static int? EntryServerId { get; private set; }
    public static int? ExitServerId { get; private set; }

    private static readonly string TunnelDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MonolithVPN", "tunnels");

    private static readonly Regex EndpointRegex = new(@"^\s*Endpoint\s*=\s*([^\s:]+):\d+\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private const int DeprioritizedMetric = 9000;

    public static async Task ConnectAsync(int entryServerId, string entryConfigText, int exitServerId, string exitConfigText)
    {
        if (entryServerId == exitServerId)
            throw new InvalidOperationException("Entry and exit servers must be different.");

        Directory.CreateDirectory(TunnelDir);
        string entryName = WireGuardService.TunnelNameFor(entryServerId);
        string exitName = WireGuardService.TunnelNameFor(exitServerId);
        string entryConfPath = Path.Combine(TunnelDir, $"{entryName}.conf");
        string exitConfPath = Path.Combine(TunnelDir, $"{exitName}.conf");

        await SplitTunnelService.RemoveControlPlaneBypassAsync();

        await File.WriteAllTextAsync(entryConfPath, entryConfigText);
        var entryResult = await WireGuardService.RunAsync("/installtunnelservice", entryConfPath);
        if (entryResult.ExitCode != 0)
            throw new InvalidOperationException($"Failed to start the entry tunnel: {entryResult.StdErr.Trim()}");

        string? exitEndpointIp = null;
        try
        {
            var entryNic = await WaitForTunnelAdapterAsync(entryName)
                ?? throw new InvalidOperationException("The entry tunnel came up but its network adapter couldn't be found.");
            var entryV4 = entryNic.GetIPProperties().GetIPv4Properties()
                ?? throw new InvalidOperationException("The entry tunnel has no IPv4 configuration yet.");
            var entryLocalIp = entryNic.GetIPProperties().UnicastAddresses
                .Select(u => u.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? throw new InvalidOperationException("The entry tunnel has no IPv4 address yet.");

            exitEndpointIp = ExtractEndpointIp(exitConfigText);
            if (exitEndpointIp is not null)
                await SplitTunnelService.AddHostRouteViaInterfaceAsync(exitEndpointIp, entryLocalIp, entryV4.Index);

            await File.WriteAllTextAsync(exitConfPath, exitConfigText);
            var exitResult = await WireGuardService.RunAsync("/installtunnelservice", exitConfPath);
            if (exitResult.ExitCode != 0)
                throw new InvalidOperationException($"Failed to start the exit tunnel: {exitResult.StdErr.Trim()}");

            await SetInterfaceMetricAsync(entryNic.Name, DeprioritizedMetric);
        }
        catch
        {
            if (exitEndpointIp is not null) await SplitTunnelService.RemoveHostRouteAsync(exitEndpointIp);
            await WireGuardService.RunAsync("/uninstalltunnelservice", exitName);
            await WireGuardService.RunAsync("/uninstalltunnelservice", entryName);
            throw;
        }

        EntryServerId = entryServerId;
        ExitServerId = exitServerId;
        IsActive = true;
    }

    public static async Task DisconnectAsync()
    {
        if (!IsActive) return;
        string entryName = WireGuardService.TunnelNameFor(EntryServerId!.Value);
        string exitName = WireGuardService.TunnelNameFor(ExitServerId!.Value);

        await WireGuardService.RunAsync("/uninstalltunnelservice", exitName);

        string exitConfPath = Path.Combine(TunnelDir, $"{exitName}.conf");
        if (File.Exists(exitConfPath))
        {
            string? exitEndpointIp = ExtractEndpointIp(await File.ReadAllTextAsync(exitConfPath));
            if (exitEndpointIp is not null) await SplitTunnelService.RemoveHostRouteAsync(exitEndpointIp);
        }

        await WireGuardService.RunAsync("/uninstalltunnelservice", entryName);

        IsActive = false;
        EntryServerId = null;
        ExitServerId = null;
    }

    private static string? ExtractEndpointIp(string configText)
    {
        var match = EndpointRegex.Match(configText.Replace("\r\n", "\n"));
        return match.Success ? match.Groups[1].Value : null;
    }

    private static async Task<NetworkInterface?> WaitForTunnelAdapterAsync(string tunnelName)
    {
        for (int i = 0; i < 20; i++)
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.Name.Equals(tunnelName, StringComparison.OrdinalIgnoreCase));
            if (nic is not null && nic.OperationalStatus == OperationalStatus.Up
                && nic.GetIPProperties().UnicastAddresses.Any(u => u.Address.AddressFamily == AddressFamily.InterNetwork))
                return nic;
            await Task.Delay(250);
        }
        return null;
    }

    private static async Task SetInterfaceMetricAsync(string interfaceName, int metric)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("interface");
            psi.ArgumentList.Add("ipv4");
            psi.ArgumentList.Add("set");
            psi.ArgumentList.Add("interface");
            psi.ArgumentList.Add(interfaceName);
            psi.ArgumentList.Add($"metric={metric}");
            using var proc = Process.Start(psi)!;
            await proc.StandardOutput.ReadToEndAsync();
            await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
        }
        catch
        {
        }
    }
}
