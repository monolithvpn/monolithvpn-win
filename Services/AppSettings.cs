using System.IO;
using System.Text.Json;

namespace MonolithVpnClient.Services;

public class AppSettingsData
{
    public bool KillSwitchEnabled { get; set; }
    public bool AutoReconnectEnabled { get; set; }

    public int AutoReconnectRetryIntervalSeconds { get; set; } = 30;
    public bool StartWithWindows { get; set; }
    public bool AutoConnectLastServer { get; set; }
    public int? LastServerId { get; set; }
    public bool ObfuscationEnabled { get; set; }

    public string? HotspotSsid { get; set; }
    public string? HotspotPassword { get; set; }

    public bool ServersGridViewEnabled { get; set; }

    public bool SplitTunnelEnabled { get; set; }

    public List<int> SplitTunnelSelectedGameIds { get; set; } = new();
    public List<string> SplitTunnelCustomRanges { get; set; } = new();
    public List<string> SplitTunnelExcludedRanges { get; set; } = new();

    public List<string> SplitTunnelPorts { get; set; } = new();

    public List<string> SplitTunnelAppPaths { get; set; } = new();

    public bool SimpleLayoutEnabled { get; set; }
}

public static class AppSettings
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MonolithVPN", "settings.json");

    private static AppSettingsData? _cached;

    public static AppSettingsData Current => _cached ??= Load();

    private static AppSettingsData Load()
    {
        AppSettingsData data;
        try
        {
            data = File.Exists(StorePath)
                ? JsonSerializer.Deserialize<AppSettingsData>(File.ReadAllText(StorePath)) ?? new AppSettingsData()
                : new AppSettingsData();
        }
        catch
        {
            data = new AppSettingsData();
        }

        if (data.KillSwitchEnabled && data.AutoReconnectEnabled)
            data.AutoReconnectEnabled = false;

        return data;
    }

    public static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(Current));
        }
        catch
        {
        }
    }
}
