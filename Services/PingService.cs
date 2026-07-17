using System.Net.NetworkInformation;

namespace MonolithVpnClient.Services;

public static class PingService
{
    public static async Task<int?> PingMsAsync(string? host, int timeoutMs = 1500)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : null;
        }
        catch
        {
            return null;
        }
    }
}
