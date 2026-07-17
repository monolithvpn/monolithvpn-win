using System.Security.Cryptography;
using System.Text;

namespace MonolithVpnClient.Services;

internal static class RequestSigning
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes(
        Obfuscation.Reveal("KAwPX1UKTVFnMntOAAMADx0JXEl/WFcMXFgXXzNndk5TBgYFGVNSTHUKWF5UC0NYMmZ4GgoJUFJPCFYfelZXWQ=="));

    public static (string Timestamp, string Signature) Sign(string method, string path)
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string message = $"{method.ToUpperInvariant()}\n{path}\n{timestamp}";
        using var hmac = new HMACSHA256(Key);
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
        return (timestamp, Convert.ToHexString(hash).ToLowerInvariant());
    }
}
