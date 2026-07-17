using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace MonolithVpnClient.Services;

public static class SplitTunnelService
{
    public readonly record struct GatewaySnapshot(IPAddress Gateway, IPAddress LocalIp, int InterfaceIndex);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetBestInterface(uint dwDestAddr, out uint pdwBestIfIndex);

    public static GatewaySnapshot? CaptureCurrentGateway()
    {
        try
        {
            uint dest = IpToUint(IPAddress.Parse("1.1.1.1"));
            if (GetBestInterface(dest, out uint ifIndex) != 0) return null;

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                IPInterfaceProperties props;
                try { props = nic.GetIPProperties(); }
                catch { continue; }

                var v4 = props.GetIPv4Properties();
                if (v4 is null || v4.Index != (int)ifIndex) continue;

                var gateway = props.GatewayAddresses
                    .Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
                var localIp = props.UnicastAddresses
                    .Select(u => u.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                return gateway is null || localIp is null ? null : new GatewaySnapshot(gateway, localIp, (int)ifIndex);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static async Task ApplyAsync(GatewaySnapshot? gateway)
    {
        if (!AppSettings.Current.SplitTunnelEnabled) return;
        if (gateway is not GatewaySnapshot gw) return;

        foreach (var range in AppSettings.Current.SplitTunnelExcludedRanges)
        {
            if (!TryParseCidr(range, out uint network, out uint mask)) continue;
            await RunRouteAsync("ADD", network, mask, gw.Gateway, gw.InterfaceIndex);
        }
    }

    public static async Task RemoveAsync()
    {
        foreach (var range in AppSettings.Current.SplitTunnelExcludedRanges)
        {
            if (!TryParseCidr(range, out uint network, out uint mask)) continue;
            await RunRouteAsync("DELETE", network, mask, null, null);
        }
    }

    public static async Task RemoveControlPlaneBypassAsync()
    {
        foreach (uint ip in await ResolveControlPlaneAddressesAsync())
            await RunRouteAsync("DELETE", ip, 0xFFFFFFFFu, null, null);
    }

    private static async Task<List<uint>> ResolveControlPlaneAddressesAsync()
    {
        try
        {
            string host = new Uri(ApiClient.BaseUrl).Host;
            var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork);
            return addresses.Select(IpToUint).ToList();
        }
        catch
        {
            return new List<uint>();
        }
    }

    public static async Task AddHostRouteViaInterfaceAsync(string ip, IPAddress viaLocalIp, int viaIfIndex)
    {
        if (!IPAddress.TryParse(ip, out var addr) || addr.AddressFamily != AddressFamily.InterNetwork) return;
        await RunRouteAsync("ADD", IpToUint(addr), 0xFFFFFFFFu, viaLocalIp, viaIfIndex);
    }

    public static async Task RemoveHostRouteAsync(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr) || addr.AddressFamily != AddressFamily.InterNetwork) return;
        await RunRouteAsync("DELETE", IpToUint(addr), 0xFFFFFFFFu, null, null);
    }

    public static bool TryNormalize(string input, out string normalized)
    {
        normalized = "";
        if (!TryParseCidr(input, out uint network, out uint mask)) return false;
        int prefixLen = CountSetBits(mask);
        normalized = prefixLen == 32 ? FormatIp(network) : $"{FormatIp(network)}/{prefixLen}";
        return true;
    }

    private static bool TryParseCidr(string input, out uint network, out uint mask)
    {
        network = 0;
        mask = 0;
        input = input.Trim();
        if (input.Length == 0) return false;

        string ipPart = input;
        int prefixLen = 32;
        int slash = input.IndexOf('/');
        if (slash >= 0)
        {
            ipPart = input[..slash];
            if (!int.TryParse(input[(slash + 1)..], out prefixLen) || prefixLen is < 0 or > 32) return false;
        }

        if (!IPAddress.TryParse(ipPart, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;

        uint ipValue = IpToUint(ip);
        uint maskValue = prefixLen == 0 ? 0u : 0xFFFFFFFFu << (32 - prefixLen);
        network = ipValue & maskValue;
        mask = maskValue;
        return true;
    }

    private static uint IpToUint(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    private static string FormatIp(uint value) =>
        $"{(value >> 24) & 0xFF}.{(value >> 16) & 0xFF}.{(value >> 8) & 0xFF}.{value & 0xFF}";

    private static int CountSetBits(uint mask)
    {
        int count = 0;
        while (mask != 0) { count += (int)(mask & 1); mask >>= 1; }
        return count;
    }

    private static async Task RunRouteAsync(string verb, uint network, uint mask, IPAddress? gateway, int? interfaceIndex)
    {
        var psi = new ProcessStartInfo("route.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(verb);
        psi.ArgumentList.Add(FormatIp(network));
        psi.ArgumentList.Add("MASK");
        psi.ArgumentList.Add(FormatIp(mask));
        if (gateway is not null)
        {
            psi.ArgumentList.Add(gateway.ToString());
            if (interfaceIndex is int idx)
            {
                psi.ArgumentList.Add("METRIC");
                psi.ArgumentList.Add("1");
                psi.ArgumentList.Add("IF");
                psi.ArgumentList.Add(idx.ToString());
            }
        }

        try
        {
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
