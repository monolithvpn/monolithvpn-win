using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace MonolithVpnClient.Services;

public record ConnectedDevice(string IpAddress, string MacAddress);

public static class HotspotService
{
    private const string TunnelAdapterPrefix = "monolithvpn-";

    private static ConnectionProfile? FindShareableUplinkProfile()
    {
        var tunnelAdapterIds = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.Name.StartsWith(TunnelAdapterPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(n => n.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool IsOurTunnel(ConnectionProfile p) =>
            p.NetworkAdapter is not null && tunnelAdapterIds.Contains(p.NetworkAdapter.NetworkAdapterId.ToString());

        var current = NetworkInformation.GetInternetConnectionProfile();
        if (current is not null && !IsOurTunnel(current))
            return current;

        return NetworkInformation.GetConnectionProfiles()
            .FirstOrDefault(p => p.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess && !IsOurTunnel(p));
    }

    public static bool UplinkIsWiFi() =>
        FindShareableUplinkProfile()?.NetworkAdapter?.IanaInterfaceType == 71;

    private static string? _lastSsid;
    private static string? _lastPassword;

    public static bool IsSupported()
    {
        try
        {
            var profile = FindShareableUplinkProfile();
            if (profile is null) return false;
            NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool? IsOn()
    {
        try
        {
            var profile = FindShareableUplinkProfile();
            if (profile is null) return null;
            var manager = NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
            return manager.TetheringOperationalState == TetheringOperationalState.On;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<(bool Success, string? Error)> StartAsync(string ssid, string password)
    {
        try
        {
            var profile = FindShareableUplinkProfile();
            if (profile is null)
                return (false, "No active internet connection to share right now.");

            var manager = NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
            var config = new NetworkOperatorTetheringAccessPointConfiguration
            {
                Ssid = ssid,
                Passphrase = password,
            };
            await manager.ConfigureAccessPointAsync(config);

            var result = await manager.StartTetheringAsync();
            if (result.Status == TetheringOperationStatus.Success)
            {
                _lastSsid = ssid;
                _lastPassword = password;
                return (true, null);
            }
            return (false, DescribeFailure(result.Status));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static async Task StopAsync()
    {
        _lastSsid = null;
        _lastPassword = null;
        try
        {
            var profile = FindShareableUplinkProfile();
            if (profile is not null)
            {
                var manager = NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
                await manager.StopTetheringAsync();
            }
        }
        catch
        {
        }

        try
        {
            var psi = new ProcessStartInfo("ipconfig", "/flushdns") { UseShellExecute = false, CreateNoWindow = true };
            using var proc = Process.Start(psi);
            if (proc is not null) await proc.WaitForExitAsync();
        }
        catch
        {
        }
    }

    private static string DescribeFailure(TetheringOperationStatus status) => status switch
    {
        TetheringOperationStatus.WiFiDeviceOff => "Turn on WiFi first, then try again.",
        _ => $"Couldn't start ({status}).",
    };

    public static async Task ReassertIfNeededAsync()
    {
        if (_lastSsid is null || _lastPassword is null) return;
        if (IsOn() == true) return;
        await StartAsync(_lastSsid, _lastPassword);
    }

    private const string HotspotGatewayIp = "192.168.137.1";

    public static async Task<List<ConnectedDevice>> GetConnectedDevicesAsync()
    {
        var devices = new List<ConnectedDevice>();
        try
        {
            var psi = new ProcessStartInfo("arp", $"-a {HotspotGatewayIp}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return devices;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            string output;
            try
            {
                output = await proc.StandardOutput.ReadToEndAsync(cts.Token);
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return devices;
            }

            foreach (Match m in Regex.Matches(output, @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s+([0-9a-fA-F-]{17})\s+(\w+)"))
            {
                string ip = m.Groups[1].Value;
                string mac = m.Groups[2].Value;
                string kind = m.Groups[3].Value;
                if (ip == HotspotGatewayIp || !kind.Equals("dynamic", StringComparison.OrdinalIgnoreCase))
                    continue;
                devices.Add(new ConnectedDevice(ip, mac));
            }
        }
        catch
        {
        }
        return devices;
    }
}
