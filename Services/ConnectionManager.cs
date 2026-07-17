using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using MonolithVpnClient.Models;
using MonolithVpnClient.Views;

namespace MonolithVpnClient.Services;

public static class ConnectionManager
{
    public static int? ConnectedServerId { get; private set; }
    public static int? MultiHopEntryServerId { get; private set; }
    public static bool IsMultiHop => MultiHopEntryServerId is not null;

    private static ApiClient? _lastApi;

    private static readonly Regex TunnelNamePattern = new(@"^monolithvpn-(\d+)$", RegexOptions.IgnoreCase);

    public static async Task TryAdoptExistingTunnelAsync(ApiClient api)
    {
        if (ConnectedServerId is not null) return;

        int? foundServerId = null;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            var match = TunnelNamePattern.Match(nic.Name);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int id))
            {
                foundServerId = id;
                break;
            }
        }
        if (foundServerId is not int serverId) return;

        try
        {
            var server = await api.GetServerStatusAsync(serverId);
            if (server is null || ConnectedServerId is not null) return;

            ConnectedServerId = serverId;
            _lastApi = api;
            ConnectionState.Current = server;
            KillSwitchService.ArmForTunnel(serverId);
            AlertLog.Add(
                $"Picked back up your existing connection to {server.Name} from before the app closed.",
                "Connection resumed", ToastKind.Info);
        }
        catch
        {
        }
    }

    public static void ResetConnectedServerId()
    {
        ConnectedServerId = null;
        MultiHopEntryServerId = null;
    }

    public static async Task<(bool Success, string? Error, int? PreviousServerId)> ConnectAsync(ApiClient api, ServerInfo server, string? endpointIp = null)
    {
        if (!WireGuardService.IsWireGuardInstalled)
            return (false, "WireGuard for Windows isn't installed. Install it from wireguard.com/install first.", null);
        if (AppSettings.Current.ObfuscationEnabled && !NpcapService.IsInstalled())
            return (false, "Traffic obfuscation is on but Npcap isn't installed. Install it from " +
                "npcap.com, or turn obfuscation off in Settings.", null);

        try
        {
            var config = await api.ConnectAsync(server.Id, endpointIp);
            await WireGuardService.ConnectAsync(server.Id, config, server.Hostname, server.ObfuscationPort);

            int? previousId = null;
            if (ConnectedServerId is int existingId && existingId != server.Id)
            {
                previousId = existingId;
                await KillSwitchService.DisarmAsync();
                if (IsMultiHop) await MultiHopService.DisconnectAsync();
                else await WireGuardService.DisconnectAsync(existingId);
            }

            MultiHopEntryServerId = null;
            ConnectedServerId = server.Id;
            _lastApi = api;
            ConnectionState.Current = server;
            KillSwitchService.ArmForTunnel(server.Id);
            AppSettings.Current.LastServerId = server.Id;
            AppSettings.Save();
            ConnectionHistory.Add(server.Name, "Connected");
            AlertLog.Add($"Connected to {server.Name}.", "Connected", ToastKind.Success);

            try
            {
                await HotspotService.ReassertIfNeededAsync();
                IcsService.ReassertIfNeeded();
            }
            catch
            {
            }

            return (true, null, previousId);
        }
        catch (ApiException ex)
        {
            return (false, ex.Message, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    public static async Task<(bool Success, string? Error, int? PreviousServerId)> ConnectMultiHopAsync(
        ApiClient api, ServerInfo entry, ServerInfo exit)
    {
        if (!WireGuardService.IsWireGuardInstalled)
            return (false, "WireGuard for Windows isn't installed. Install it from wireguard.com/install first.", null);
        if (entry.Id == exit.Id)
            return (false, "Entry and exit servers must be different.", null);

        try
        {
            string entryConfig = await api.ConnectAsync(entry.Id);
            string exitConfig = await api.ConnectAsync(exit.Id);

            int? previousId = null;
            if (ConnectedServerId is int existingId)
            {
                previousId = existingId;
                await KillSwitchService.DisarmAsync();
                if (IsMultiHop) await MultiHopService.DisconnectAsync();
                else await WireGuardService.DisconnectAsync(existingId);
            }

            await MultiHopService.ConnectAsync(entry.Id, entryConfig, exit.Id, exitConfig);

            ConnectedServerId = exit.Id;
            MultiHopEntryServerId = entry.Id;
            _lastApi = api;
            ConnectionState.Current = exit;
            KillSwitchService.ArmForTunnel(exit.Id);
            AppSettings.Current.LastServerId = exit.Id;
            AppSettings.Save();
            ConnectionHistory.Add($"{entry.Name} → {exit.Name}", "Connected (multi-hop)");
            AlertLog.Add($"Connected via {entry.Name} → {exit.Name}.", "Connected", ToastKind.Success);

            return (true, null, previousId);
        }
        catch (ApiException ex)
        {
            return (false, ex.Message, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    public static async Task<(bool Success, string? Error)> DisconnectAsync(int serverId, string serverName)
    {
        try
        {
            await KillSwitchService.DisarmAsync();

            bool wasMultiHop = IsMultiHop && ConnectedServerId == serverId;
            int? entryIdToNotify = wasMultiHop ? MultiHopEntryServerId : null;
            if (wasMultiHop) await MultiHopService.DisconnectAsync();
            else await WireGuardService.DisconnectAsync(serverId);

            if (ConnectedServerId == serverId)
            {
                ConnectedServerId = null;
                MultiHopEntryServerId = null;
            }
            ConnectionState.Current = null;

            if (_lastApi is ApiClient api)
            {
                try
                {
                    await api.NotifyDisconnectAsync(serverId);
                    if (entryIdToNotify is int entryId) await api.NotifyDisconnectAsync(entryId);
                }
                catch { }
            }
            ConnectionHistory.Add(serverName, "Disconnected");
            AlertLog.Add($"Disconnected from {serverName}.", "Disconnected", ToastKind.Info);
            return (true, null);
        }
        catch (ApiException ex)
        {
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
