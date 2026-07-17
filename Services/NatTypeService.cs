using System.Net;
using System.Net.Sockets;

namespace MonolithVpnClient.Services;

public enum NatType { Open, Moderate, Strict, Unknown }

public record NatCheckResult(NatType Type, string? ExternalIp, int? ExternalPort);

public static class NatTypeService
{
    private const uint MagicCookie = 0x2112A442;

    private static readonly (string Host, int Port)[] StunServers =
    {
        ("stun.l.google.com", 19302),
        ("stun1.l.google.com", 19302),
    };

    public static async Task<NatCheckResult> CheckAsync(int timeoutMs = 3000)
    {
        using var client = new UdpClient(0);
        int localPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;

        var results = new List<(string Ip, int Port)>();
        foreach (var (host, port) in StunServers)
        {
            var probe = await ProbeAsync(client, host, port, timeoutMs);
            if (probe is not null) results.Add(probe.Value);
        }

        if (results.Count == 0) return new NatCheckResult(NatType.Unknown, null, null);

        var first = results[0];
        if (results.Count == 1)
        {
            var soloType = first.Port == localPort ? NatType.Open : NatType.Moderate;
            return new NatCheckResult(soloType, first.Ip, first.Port);
        }

        bool consistent = results.All(r => r.Ip == first.Ip && r.Port == first.Port);
        if (!consistent) return new NatCheckResult(NatType.Strict, first.Ip, first.Port);

        var natType = first.Port == localPort ? NatType.Open : NatType.Moderate;
        return new NatCheckResult(natType, first.Ip, first.Port);
    }

    private static async Task<(string Ip, int Port)?> ProbeAsync(UdpClient client, string host, int port, int timeoutMs)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (address is null) return null;

            var request = BuildBindingRequest(out var transactionId);
            var remote = new IPEndPoint(address, port);
            await client.SendAsync(request, request.Length, remote);

            return await ReceiveMatchingAsync(client, transactionId, timeoutMs);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(string Ip, int Port)?> ReceiveMatchingAsync(UdpClient client, byte[] transactionId, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return null;

            var receiveTask = client.ReceiveAsync();
            var completed = await Task.WhenAny(receiveTask, Task.Delay(remaining));
            if (completed != receiveTask) return null;

            var buffer = receiveTask.Result.Buffer;
            if (!IsMatchingBindingResponse(buffer, transactionId)) continue;

            return ParseXorMappedAddress(buffer);
        }
    }

    private static byte[] BuildBindingRequest(out byte[] transactionId)
    {
        transactionId = new byte[12];
        Random.Shared.NextBytes(transactionId);

        var message = new byte[20];
        message[0] = 0x00; message[1] = 0x01;
        message[2] = 0x00; message[3] = 0x00;
        WriteUInt32BigEndian(message, 4, MagicCookie);
        Array.Copy(transactionId, 0, message, 8, 12);
        return message;
    }

    private static bool IsMatchingBindingResponse(byte[] response, byte[] transactionId)
    {
        if (response.Length < 20) return false;
        if (response[0] != 0x01 || response[1] != 0x01) return false;
        for (int i = 0; i < 12; i++)
            if (response[8 + i] != transactionId[i]) return false;
        return true;
    }

    private static (string Ip, int Port)? ParseXorMappedAddress(byte[] response)
    {
        int messageLength = (response[2] << 8) | response[3];
        int offset = 20;
        int end = Math.Min(response.Length, 20 + messageLength);

        (string Ip, int Port)? fallback = null;
        while (offset + 4 <= end)
        {
            int attrType = (response[offset] << 8) | response[offset + 1];
            int attrLength = (response[offset + 2] << 8) | response[offset + 3];
            int valueOffset = offset + 4;
            if (valueOffset + attrLength > end) break;

            if (attrType == 0x0020 && attrLength >= 8)
            {
                var parsed = ParseAddressAttribute(response, valueOffset, xored: true);
                if (parsed is not null) return parsed;
            }
            else if (attrType == 0x0001 && attrLength >= 8 && fallback is null)
            {
                fallback = ParseAddressAttribute(response, valueOffset, xored: false);
            }

            offset = valueOffset + ((attrLength + 3) & ~3);
        }

        return fallback;
    }

    private static (string Ip, int Port)? ParseAddressAttribute(byte[] data, int offset, bool xored)
    {
        byte family = data[offset + 1];
        if (family != 0x01) return null;

        int port = (data[offset + 2] << 8) | data[offset + 3];
        byte[] addressBytes = new byte[4];
        Array.Copy(data, offset + 4, addressBytes, 0, 4);

        if (xored)
        {
            port ^= (int)(MagicCookie >> 16);
            addressBytes[0] ^= (byte)((MagicCookie >> 24) & 0xFF);
            addressBytes[1] ^= (byte)((MagicCookie >> 16) & 0xFF);
            addressBytes[2] ^= (byte)((MagicCookie >> 8) & 0xFF);
            addressBytes[3] ^= (byte)(MagicCookie & 0xFF);
        }

        return (new IPAddress(addressBytes).ToString(), port);
    }

    private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
