using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;

namespace MonolithVpnClient.Services;

public static class AppSplitTunnelService
{
    private readonly record struct FlowKey(byte Protocol, ushort LocalPort);
    private readonly record struct TranslatedKey(byte Protocol, ushort Port);

    private sealed class Flow
    {
        public required ushort TranslatedPort { get; init; }
        public required uint RemoteAddr { get; init; }
        public required ushort RemotePort { get; init; }
        public required byte Protocol { get; init; }
        public DateTime LastSeen { get; set; }
    }

    private const int IdleTimeoutSeconds = 120;
    private const int BufferSize = 0x10000 + 40;
    private const int RescanIntervalMs = 2000;
    private const byte ProtoTcp = 6;
    private const byte ProtoUdp = 17;

    private static readonly ConcurrentDictionary<FlowKey, Flow> _flows = new();
    private static readonly ConcurrentDictionary<TranslatedKey, FlowKey> _flowsByTranslatedPort = new();

    private static IntPtr _outboundHandle = IntPtr.Zero;
    private static IntPtr _inboundHandle = IntPtr.Zero;
    private static CancellationTokenSource? _monitorCts;
    private static Task? _monitorTask;
    private static Task? _outboundTask;
    private static Task? _inboundTask;
    private static readonly SemaphoreSlim _handleLock = new(1, 1);

    public static bool IsRunning => _monitorTask is { IsCompleted: false };

    public static async Task StartAsync(int tunnelIfIdx, IPAddress tunnelLocalIp, int physicalIfIdx, IPAddress physicalLocalIp, IReadOnlyList<string> exePaths)
    {
        await StopAsync();
        var normalizedPaths = exePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedPaths.Count == 0) return;

        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;
        _monitorTask = Task.Run(() => MonitorProcessesAsync(tunnelIfIdx, tunnelLocalIp, physicalIfIdx, physicalLocalIp, normalizedPaths, token), token);
    }

    public static async Task StopAsync()
    {
        _monitorCts?.Cancel();
        if (_monitorTask is not null)
        {
            try { await _monitorTask; } catch { }
        }
        _monitorCts = null;
        _monitorTask = null;

        await CloseHandlesAsync();
        _flows.Clear();
        _flowsByTranslatedPort.Clear();
    }

    private static async Task MonitorProcessesAsync(int tunnelIfIdx, IPAddress tunnelLocalIp, int physicalIfIdx, IPAddress physicalLocalIp, List<string> exePaths, CancellationToken token)
    {
        var currentPids = new HashSet<int>();
        while (!token.IsCancellationRequested)
        {
            var matchedPids = FindMatchingPids(exePaths);
            if (!matchedPids.SetEquals(currentPids))
            {
                currentPids = matchedPids;
                await ReopenHandlesAsync(tunnelIfIdx, tunnelLocalIp, physicalIfIdx, physicalLocalIp, currentPids, token);
            }

            try { await Task.Delay(RescanIntervalMs, token); }
            catch (OperationCanceledException) { break; }
        }

        await CloseHandlesAsync();
    }

    private static HashSet<int> FindMatchingPids(List<string> exePaths)
    {
        var pids = new HashSet<int>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                string? path = proc.MainModule?.FileName;
                if (path is not null && exePaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
                    pids.Add(proc.Id);
            }
            catch
            {
            }
            finally
            {
                proc.Dispose();
            }
        }
        return pids;
    }

    private static async Task ReopenHandlesAsync(int tunnelIfIdx, IPAddress tunnelLocalIp, int physicalIfIdx, IPAddress physicalLocalIp, HashSet<int> pids, CancellationToken token)
    {
        await _handleLock.WaitAsync(token);
        try
        {
            await CloseHandlesAsync();
            if (pids.Count == 0 || token.IsCancellationRequested) return;

            string pidExpr = string.Join(" or ", pids.Select(pid => $"processId == {pid}"));
            string outboundFilter = $"outbound and ifIdx == {tunnelIfIdx} and (tcp or udp) and ({pidExpr})";

            string inboundFilter = $"inbound and ifIdx == {physicalIfIdx} and (tcp or udp)";

            try
            {
                _outboundHandle = WinDivertInterop.Open(outboundFilter);
                _inboundHandle = WinDivertInterop.Open(inboundFilter);
            }
            catch
            {
                await CloseHandlesAsync();
                return;
            }

            var innerCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var innerToken = innerCts.Token;
            _outboundTask = Task.Run(() => PumpOutbound(physicalIfIdx, physicalLocalIp, innerToken), innerToken);
            _inboundTask = Task.Run(() => PumpInbound(tunnelIfIdx, tunnelLocalIp, innerToken), innerToken);
        }
        finally
        {
            _handleLock.Release();
        }
    }

    private static async Task CloseHandlesAsync()
    {
        if (_outboundHandle != IntPtr.Zero) { WinDivertInterop.Close(_outboundHandle); _outboundHandle = IntPtr.Zero; }
        if (_inboundHandle != IntPtr.Zero) { WinDivertInterop.Close(_inboundHandle); _inboundHandle = IntPtr.Zero; }

        var pending = new[] { _outboundTask, _inboundTask }.Where(t => t is not null).Select(t => t!).ToArray();
        if (pending.Length > 0)
        {
            try { await Task.WhenAll(pending); } catch { }
        }
        _outboundTask = null;
        _inboundTask = null;
    }

    private static void PumpOutbound(int physicalIfIdx, IPAddress physicalLocalIp, CancellationToken token)
    {
        var buffer = new byte[BufferSize];
        uint physicalIp = IpToUint(physicalLocalIp);
        var handle = _outboundHandle;

        while (!token.IsCancellationRequested)
        {
            if (!WinDivertInterop.TryReceive(handle, buffer, out uint length, out var addr)) break;
            try
            {
                if (!TryGetTransportHeaderOffsets(buffer, length, out int ipHdrLen, out byte protocol))
                {
                    Resend(handle, buffer, length, addr);
                    continue;
                }

                ushort localPort = ReadUInt16(buffer, ipHdrLen);
                uint remoteAddr = ReadUInt32(buffer, 16);
                ushort remotePort = ReadUInt16(buffer, ipHdrLen + 2);
                var key = new FlowKey(protocol, localPort);

                CleanupExpiredFlows();

                var flow = _flows.GetOrAdd(key, _ => CreateFlow(key, remoteAddr, remotePort, protocol));
                if (flow.RemoteAddr != remoteAddr || flow.RemotePort != remotePort)
                {
                    Resend(handle, buffer, length, addr);
                    continue;
                }
                flow.LastSeen = DateTime.UtcNow;

                WriteUInt32(buffer, 12, physicalIp);
                WriteUInt16(buffer, ipHdrLen, flow.TranslatedPort);
                WinDivertInterop.RecalculateChecksums(buffer, length, ref addr);
                addr.IfIdx = (uint)physicalIfIdx;
                addr.SubIfIdx = 0;
                addr.Outbound = true;
                WinDivertInterop.Send(handle, buffer, length, in addr);
            }
            catch
            {
            }
        }
    }

    private static void PumpInbound(int tunnelIfIdx, IPAddress tunnelLocalIp, CancellationToken token)
    {
        var buffer = new byte[BufferSize];
        uint tunnelIp = IpToUint(tunnelLocalIp);
        var handle = _inboundHandle;

        while (!token.IsCancellationRequested)
        {
            if (!WinDivertInterop.TryReceive(handle, buffer, out uint length, out var addr)) break;
            try
            {
                if (!TryGetTransportHeaderOffsets(buffer, length, out int ipHdrLen, out byte protocol))
                {
                    Resend(handle, buffer, length, addr);
                    continue;
                }

                ushort translatedPort = ReadUInt16(buffer, ipHdrLen + 2);
                uint remoteAddr = ReadUInt32(buffer, 12);
                ushort remotePort = ReadUInt16(buffer, ipHdrLen);
                var translatedKey = new TranslatedKey(protocol, translatedPort);

                if (!_flowsByTranslatedPort.TryGetValue(translatedKey, out var flowKey) ||
                    !_flows.TryGetValue(flowKey, out var flow) ||
                    flow.RemoteAddr != remoteAddr || flow.RemotePort != remotePort)
                {
                    Resend(handle, buffer, length, addr);
                    continue;
                }
                flow.LastSeen = DateTime.UtcNow;

                WriteUInt32(buffer, 16, tunnelIp);
                WriteUInt16(buffer, ipHdrLen + 2, flowKey.LocalPort);
                WinDivertInterop.RecalculateChecksums(buffer, length, ref addr);
                addr.IfIdx = (uint)tunnelIfIdx;
                addr.SubIfIdx = 0;
                addr.Outbound = false;
                WinDivertInterop.Send(handle, buffer, length, in addr);
            }
            catch
            {
            }
        }
    }

    private static Flow CreateFlow(FlowKey key, uint remoteAddr, ushort remotePort, byte protocol)
    {
        ushort translated = key.LocalPort;
        var translatedKey = new TranslatedKey(protocol, translated);
        if (_flowsByTranslatedPort.ContainsKey(translatedKey))
        {
            translated = FindFreeTranslatedPort(protocol);
            translatedKey = new TranslatedKey(protocol, translated);
        }

        var flow = new Flow { TranslatedPort = translated, RemoteAddr = remoteAddr, RemotePort = remotePort, Protocol = protocol, LastSeen = DateTime.UtcNow };
        _flowsByTranslatedPort[translatedKey] = key;
        return flow;
    }

    private static ushort FindFreeTranslatedPort(byte protocol)
    {
        var rnd = Random.Shared;
        for (int attempt = 0; attempt < 64; attempt++)
        {
            ushort candidate = (ushort)rnd.Next(40000, 60000);
            if (!_flowsByTranslatedPort.ContainsKey(new TranslatedKey(protocol, candidate))) return candidate;
        }
        return (ushort)rnd.Next(40000, 60000);
    }

    private static void CleanupExpiredFlows()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-IdleTimeoutSeconds);
        foreach (var (key, flow) in _flows)
        {
            if (flow.LastSeen > cutoff) continue;
            _flows.TryRemove(key, out _);
            _flowsByTranslatedPort.TryRemove(new TranslatedKey(flow.Protocol, flow.TranslatedPort), out _);
        }
    }

    private static void Resend(IntPtr handle, byte[] buffer, uint length, in WinDivertInterop.WINDIVERT_ADDRESS addr) =>
        WinDivertInterop.Send(handle, buffer, length, in addr);

    private static bool TryGetTransportHeaderOffsets(byte[] buffer, uint length, out int ipHeaderLength, out byte protocol)
    {
        ipHeaderLength = 0;
        protocol = 0;
        if (length < 24) return false;
        if ((buffer[0] >> 4) != 4) return false;
        int ihl = (buffer[0] & 0x0F) * 4;
        if (ihl < 20) return false;

        byte proto = buffer[9];
        if (proto != ProtoTcp && proto != ProtoUdp) return false;
        if (length < ihl + 4) return false;

        ipHeaderLength = ihl;
        protocol = proto;
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
