using System.Net.NetworkInformation;

namespace MonolithVpnClient.Services;

public static class IcsService
{
    private const string ProgId = "HNetCfg.HNetShare";

    public static bool IsSharingEnabled { get; private set; }

    public static string? LastPublicAdapterId { get; private set; }
    public static string? LastPrivateAdapterId { get; private set; }

    public static bool LastPublicIsVpnTunnel { get; private set; }

    public static (bool Success, string? Error) Enable(string publicAdapterId, string privateAdapterId, bool publicIsVpnTunnel = false)
    {
        try
        {
            dynamic? shareManager = CreateShareManager();
            if (shareManager is null)
                return (false, "Couldn't access Windows' Internet Connection Sharing on this PC.");

            var publicConfig = FindConnectionConfig(shareManager, publicAdapterId);
            var privateConfig = FindConnectionConfig(shareManager, privateAdapterId);
            if (publicConfig is null)
                return (false, "Couldn't find the Public adapter in Windows' sharing configuration. Try refreshing the adapter list and reselecting it.");
            if (privateConfig is null)
                return (false, "Couldn't find the Private adapter in Windows' sharing configuration. Try refreshing the adapter list and reselecting it.");

            publicConfig.EnableSharing(0);
            privateConfig.EnableSharing(1);
            IsSharingEnabled = true;
            LastPublicAdapterId = publicAdapterId;
            LastPrivateAdapterId = privateAdapterId;
            LastPublicIsVpnTunnel = publicIsVpnTunnel;
            return (true, null);
        }
        catch (Exception ex)
        {
            IsSharingEnabled = false;
            return (false, $"Couldn't enable sharing: {ex.Message}");
        }
    }

    public static (bool Success, string? Error) Disable(string? publicAdapterId, string? privateAdapterId)
    {
        try
        {
            dynamic? shareManager = CreateShareManager();
            if (shareManager is null)
            {
                IsSharingEnabled = false;
                return (false, "Couldn't access Windows' Internet Connection Sharing on this PC.");
            }

            if (publicAdapterId is not null) FindConnectionConfig(shareManager, publicAdapterId)?.DisableSharing();
            if (privateAdapterId is not null) FindConnectionConfig(shareManager, privateAdapterId)?.DisableSharing();
            IsSharingEnabled = false;
            LastPublicAdapterId = null;
            LastPrivateAdapterId = null;
            LastPublicIsVpnTunnel = false;
            return (true, null);
        }
        catch (Exception ex)
        {
            IsSharingEnabled = false;
            return (false, $"Couldn't disable sharing: {ex.Message}");
        }
    }

    public static void ReassertIfNeeded()
    {
        if (IsSharingEnabled) return;
        if (LastPrivateAdapterId is null) return;

        string? publicId = LastPublicAdapterId;
        if (LastPublicIsVpnTunnel)
        {
            if (ConnectionManager.ConnectedServerId is not int serverId) return;
            string tunnelName = WireGuardService.TunnelNameFor(serverId);
            var tunnelNic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.Name.Equals(tunnelName, StringComparison.OrdinalIgnoreCase));
            if (tunnelNic is null) return;
            publicId = tunnelNic.Id;
        }
        if (publicId is null) return;

        Enable(publicId, LastPrivateAdapterId, LastPublicIsVpnTunnel);
    }

    private static dynamic? CreateShareManager()
    {
        var type = Type.GetTypeFromProgID(ProgId);
        return type is null ? null : Activator.CreateInstance(type);
    }

    private static dynamic? FindConnectionConfig(dynamic shareManager, string adapterId)
    {
        if (!Guid.TryParse(adapterId, out var targetGuid)) return null;

        foreach (var connection in shareManager.EnumEveryConnection)
        {
            var props = shareManager.NetConnectionProps(connection);
            if (Guid.TryParse((string)props.Guid, out var candidateGuid) && candidateGuid == targetGuid)
                return shareManager.INetSharingConfigurationForINetConnection(connection);
        }

        return null;
    }
}
