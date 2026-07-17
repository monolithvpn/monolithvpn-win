using System.IO;

namespace MonolithVpnClient.Services;

public static class NpcapService
{
    public const string OfficialDownloadPage = "https://npcap.com/#download";

    private static readonly string[] MarkerPaths =
    {
        @"C:\Windows\System32\Npcap\wpcap.dll",
        @"C:\Windows\System32\drivers\npcap.sys",
    };

    public static bool IsInstalled() => MarkerPaths.Any(File.Exists);
}
