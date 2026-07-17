using System.Diagnostics;

namespace MonolithVpnClient.Services;

public static class WebsiteLinks
{
    private static readonly string BaseUrl = Obfuscation.Reveal("JRsaHx9TW0c7PyBCXllGXlsbC1chAAI=");

    public static void Open(string path)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(BaseUrl + path) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
