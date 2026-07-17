using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MonolithVpnClient.Services;

public static class TokenStorage
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MonolithVPN", "session.dat");

    public static void Save(string token)
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath)!;
            Directory.CreateDirectory(dir);
            var plainBytes = Encoding.UTF8.GetBytes(token);
            var protectedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(StorePath, protectedBytes);
        }
        catch
        {
        }
    }

    public static string? Load()
    {
        if (!File.Exists(StorePath)) return null;
        try
        {
            var protectedBytes = File.ReadAllBytes(StorePath);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(StorePath)) File.Delete(StorePath);
        }
        catch
        {
        }
    }
}
