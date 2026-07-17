using System.Text;

namespace MonolithVpnClient.Services;

internal static class Obfuscation
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("MonolithVPN-2026-key");

    public static string Reveal(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        var result = new byte[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            result[i] = (byte)(bytes[i] ^ Key[i % Key.Length]);
        return Encoding.UTF8.GetString(result);
    }

    public static string Conceal(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var result = new byte[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            result[i] = (byte)(bytes[i] ^ Key[i % Key.Length]);
        return Convert.ToBase64String(result);
    }
}
