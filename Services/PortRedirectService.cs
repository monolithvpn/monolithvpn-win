using System.Collections.Concurrent;
using System.Net;

namespace MonolithVpnClient.Services;

public static class PortRedirectService
{
    public readonly record struct PortRange(string Protocol, int Low, int High);

    private sealed class Flow
    {
        public required ushort TranslatedPort { get; init; }
        public required uint RemoteAddr { get; init; }
        public required ushort RemotePort { get; init; }
        public DateTime LastSeen { get; set; }
    }

    private const int IdleTimeoutSeconds = 120;
    private const int BufferSize = 0x10000 + 40;

    private static IntPtr _outboundHandle = IntPtr.Zero;
    private static IntPtr _inboundHandle = IntPtr.Zero;
    private static CancellationTokenSource? _cts;
    private static Task? _outboundTask;
    private static Task? _inboundTask;

    private static readonly ConcurrentDictionary<ushort, Flow> _flows = new();
    private static readonly ConcurrentDictionary<ushort, ushort> _flowsByTranslatedPort = new();

    public static bool IsRunning => _outboundHandle != IntPtr.Zero
        && _outboundTask is { IsCompleted: false } && _inboundTask is { IsCompleted: false };

    public static List<PortRange> ParseUdpRanges(IEnumerable<string> entries)
    {
        var result = new List<PortRange>();
        foreach (var raw in entries)
        {
            var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !parts[0].Equals("udp", StringComparison.OrdinalIgnoreCase)) continue;

            var bounds = parts[1].Split('-');
            if (!int.TryParse(bounds[0], out int low)) continue;
            int high = bounds.Length > 1 && int.TryParse(bounds[1], out int h) ? h : low;
            if (low is < 1 or > 65535 || high is < 1 or > 65535 || low > high) continue;

            result.Add(new PortRange("udp", low, high));
        }
        return result;
    }

    public static async Task StartAsync(int tunnelIfIdx, IPAddress tunnelLocalIp, int physicalIfIdx, IPAddress physicalLocalIp, IReadOnlyList<PortRange> udpRanges)
    {
        await StopAsync();
        if (udpRanges.Count == 0) return;

        string dstPortExpr = BuildPortExpression("DstPort", udpRanges);
        string srcPortExpr = BuildPortExpression("SrcPort", udpRanges);
        if (dstPortExpr.Length == 0) return;

        string outboundFilter = $"outbound and ifIdx == {tunnelIfIdx} and udp and ({dstPortExpr})";
        string inboundFilter = $"inbound and ifIdx == {physicalIfIdx} and udp and ({srcPortExpr})";

        try
        {
            _outboundHandle = WinDivertInterop.Open(outboundFilter);
            _inboundHandle = WinDivertInterop.Open(inboundFilter);
        }
        catch
        {
            await StopAsync();
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _outboundTask = Task.Run(() => PumpOutbound(tunnelIfIdx, physicalIfIdx, physicalLocalIp, token), token);
        _inboundTask = Task.Run(() => PumpInbound(tunnelIfIdx, physicalIfIdx, tunnelLocalIp, token), token);
    }

    public static async Task StopAsync()
    {
        _cts?.Cancel();

        if (_outboundHandle != IntPtr.Zero) { WinDivertInterop.Close(_outboundHandle); _outboundHandle = IntPtr.Zero; }
        if (_inboundHandle != IntPtr.Zero) { WinDivertInterop.Close(_inboundHandle); _inboundHandle = IntPtr.Zero; }

        var pending = new[] { _outboundTask, _inboundTask }.Where(t => t is not null).Select(t => t!).ToArray();
        if (pending.Length > 0)
        {
            try { await Task.WhenAll(pending); } catch { }
        }

        _outboundTask = null;
        _inboundTask = null;
        _cts = null;
        _flows.Clear();
        _flowsByTranslatedPort.Clear();
    }

    private static string BuildPortExpression(string field, IReadOnlyList<PortRange> ranges)
    {
        var terms = ranges.Select(r => r.Low == r.High
            ? $"udp.{field} == {r.Low}"
            : $"(udp.{field} >= {r.Low} and udp.{field} <= {r.High})");
        return string.Join(" or ", terms);
    }

    private static void PumpOutbound(int tunnelIfIdx, int physicalIfIdx, IPAddress physicalLocalIp, CancellationToken token)
    {
        var buffer = new byte[BufferSize];
        uint physicalIp = IpToUint(physicalLocalIp);

        while (!token.IsCancellationRequested)
        {
            if (!WinDivertInterop.TryReceive(_outboundHandle, buffer, out uint length, out var addr)) break;
            try
            {
                if (!TryGetUdpHeaderOffsets(buffer, length, out int ipHdrLen))
                {
                    Resend(_outboundHandle, buffer, length, addr);
                    continue;
                }

                ushort localPort = ReadUInt16(buffer, ipHdrLen);
                uint remoteAddr = ReadUInt32(buffer, 16);
                ushort remotePort = ReadUInt16(buffer, ipHdrLen + 2);

                CleanupExpiredFlows();

                var flow = _flows.GetOrAdd(localPort, _ => CreateFlow(localPort, remoteAddr, remotePort, physicalIp));
                if (flow.RemoteAddr != remoteAddr || flow.RemotePort != remotePort)
                {
                    Resend(_outboundHandle, buffer, length, addr);
                    continue;
                }
                flow.LastSeen = DateTime.UtcNow;

                WriteUInt32(buffer, 12, physicalIp);
                WriteUInt16(buffer, ipHdrLen, flow.TranslatedPort);
                WinDivertInterop.RecalculateChecksums(buffer, length, ref addr);
                addr.IfIdx = (uint)physicalIfIdx;
                addr.SubIfIdx = 0;
                addr.Outbound = true;
                WinDivertInterop.Send(_outboundHandle, buffer, length, in addr);
            }
            catch
            {
            }
        }
    }

    private static void PumpInbound(int tunnelIfIdx, int physicalIfIdx, IPAddress tunnelLocalIp, CancellationToken token)
    {
        var buffer = new byte[BufferSize];
        uint tunnelIp = IpToUint(tunnelLocalIp);

        while (!token.IsCancellationRequested)
        {
            if (!WinDivertInterop.TryReceive(_inboundHandle, buffer, out uint length, out var addr)) break;
            try
            {
                if (!TryGetUdpHeaderOffsets(buffer, length, out int ipHdrLen))
                {
                    Resend(_inboundHandle, buffer, length, addr);
                    continue;
                }

                ushort translatedPort = ReadUInt16(buffer, ipHdrLen + 2);
                uint remoteAddr = ReadUInt32(buffer, 12);
                ushort remotePort = ReadUInt16(buffer, ipHdrLen);

                if (!_flowsByTranslatedPort.TryGetValue(translatedPort, out ushort localPort) ||
                    !_flows.TryGetValue(localPort, out var flow) ||
                    flow.RemoteAddr != remoteAddr || flow.RemotePort != remotePort)
                {
                    Resend(_inboundHandle, buffer, length, addr);
                    continue;
                }
                flow.LastSeen = DateTime.UtcNow;

                WriteUInt32(buffer, 16, tunnelIp);
                WriteUInt16(buffer, ipHdrLen + 2, localPort);
                WinDivertInterop.RecalculateChecksums(buffer, length, ref addr);
                addr.IfIdx = (uint)tunnelIfIdx;
                addr.SubIfIdx = 0;
                addr.Outbound = false;
                WinDivertInterop.Send(_inboundHandle, buffer, length, in addr);
            }
            catch
            {
            }
        }
    }

    private static Flow CreateFlow(ushort localPort, uint remoteAddr, ushort remotePort, uint physicalIp)
    {
        ushort translated = localPort;
        if (_flowsByTranslatedPort.ContainsKey(translated))
        {
            translated = FindFreeTranslatedPort();
        }

        var flow = new Flow { TranslatedPort = translated, RemoteAddr = remoteAddr, RemotePort = remotePort, LastSeen = DateTime.UtcNow };
        _flowsByTranslatedPort[translated] = localPort;
        return flow;
    }

    private static ushort FindFreeTranslatedPort()
    {
        var rnd = Random.Shared;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            ushort candidate = (ushort)rnd.Next(40000, 60000);
            if (!_flowsByTranslatedPort.ContainsKey(candidate)) return candidate;
        }
        return (ushort)rnd.Next(40000, 60000);
    }

    private static void CleanupExpiredFlows()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-IdleTimeoutSeconds);
        foreach (var (localPort, flow) in _flows)
        {
            if (flow.LastSeen > cutoff) continue;
            _flows.TryRemove(localPort, out _);
            _flowsByTranslatedPort.TryRemove(flow.TranslatedPort, out _);
        }
    }

    private static void Resend(IntPtr handle, byte[] buffer, uint length, in WinDivertInterop.WINDIVERT_ADDRESS addr) =>
        WinDivertInterop.Send(handle, buffer, length, in addr);

    private static bool TryGetUdpHeaderOffsets(byte[] buffer, uint length, out int ipHeaderLength)
    {
        ipHeaderLength = 0;
        if (length < 28) return false;
        if ((buffer[0] >> 4) != 4) return false;
        int ihl = (buffer[0] & 0x0F) * 4;
        if (ihl < 20 || buffer[9] != 17 || length < ihl + 8) return false;
        ipHeaderLength = ihl;
        return true;
    }

    private static ushort ReadUInt16(byte[] b, int offset) => (ushort)((b[offset] << 8) | b[offset + 1]);
    private static uint ReadUInt32(byte[] b, int offset) =>
        ((uint)b[offset] << 24) | ((uint)b[offset + 1] << 16) | ((uint)b[offset + 2] << 8) | b[offset + 3];

    private static void WriteUInt16(byte[] b, int offset, ushort value)
    {
        b[offset] = (byte)(value >> 8);
        b[offset + 1] = (byte)value;
    }

    private static void WriteUInt32(byte[] b, int offset, uint value)
    {
        b[offset] = (byte)(value >> 24);
        b[offset + 1] = (byte)(value >> 16);
        b[offset + 2] = (byte)(value >> 8);
        b[offset + 3] = (byte)value;
    }

    private static uint IpToUint(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }
}
