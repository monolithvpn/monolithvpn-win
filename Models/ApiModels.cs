using System.Text.Json.Serialization;

namespace MonolithVpnClient.Models;

public class LoginResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("free_mode")]
    public bool FreeMode { get; set; }

    [JsonPropertyName("free_mode_message")]
    public string? FreeModeMessage { get; set; }
}

public class FreeModeStatusResponse
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class MeResponse
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("plan")]
    public PlanInfo? Plan { get; set; }

    [JsonPropertyName("devices_used")]
    public int DevicesUsed { get; set; }

    [JsonPropertyName("device_limit")]
    public int? DeviceLimit { get; set; }
}

public class PlanInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("tier")]
    public string? Tier { get; set; }

    [JsonPropertyName("device_limit")]
    public int? DeviceLimit { get; set; }
}

public class ServerInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("flag_url")]
    public string? FlagUrl { get; set; }

    [JsonPropertyName("protected")]
    public bool IsProtected { get; set; }

    [JsonPropertyName("maintenance")]
    public bool Maintenance { get; set; }

    [JsonPropertyName("online")]
    public bool? Online { get; set; }

    [JsonPropertyName("under_attack")]
    public bool UnderAttack { get; set; }

    [JsonPropertyName("load_pct")]
    public int LoadPct { get; set; }

    [JsonPropertyName("tags")]
    public List<ServerTagInfo> Tags { get; set; } = new();

    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("obfuscation_port")]
    public int? ObfuscationPort { get; set; }

    [JsonPropertyName("extra_ips")]
    public List<string> ExtraIps { get; set; } = new();

    [JsonPropertyName("free_eligible")]
    public bool IsFreeEligible { get; set; }
}

public class ServerTagInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("color")]
    public string Color { get; set; } = "gray";
}

public class ServersResponse
{
    [JsonPropertyName("servers")]
    public List<ServerInfo> Servers { get; set; } = new();
}

public class ConnectResponse
{
    [JsonPropertyName("config")]
    public string Config { get; set; } = "";
}

public class ApiErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }
}

public class ChangelogEntryInfo
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = "";
}

public class ChangelogResponse
{
    [JsonPropertyName("entries")]
    public List<ChangelogEntryInfo> Entries { get; set; } = new();
}

public class AppVersionResponse
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("release_notes")]
    public string? ReleaseNotes { get; set; }

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; set; }
}

public class GameInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("cidr_ranges")]
    public List<string> CidrRanges { get; set; } = new();

    [JsonPropertyName("ports")]
    public List<string> Ports { get; set; } = new();
}

public class GamesResponse
{
    [JsonPropertyName("games")]
    public List<GameInfo> Games { get; set; } = new();
}

public class MyIpResponse
{
    [JsonPropertyName("ip")]
    public string? Ip { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }
}
