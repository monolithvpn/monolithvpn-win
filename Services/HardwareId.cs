using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace MonolithVpnClient.Services;

public static class HardwareId
{
    private const string Salt = "MonolithVPN-device-salt-2026";

    public static string ComputeHash()
    {
        var raw = ReadMachineGuid() ?? Environment.MachineName;
        var bytes = Encoding.UTF8.GetBytes(Salt + raw);
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public static string Platform => "Windows";

    private static string? ReadMachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }
}
