using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using MonolithVpnClient.Models;

namespace MonolithVpnClient.Services;

public class ApiException : Exception
{
    public int StatusCode { get; }

    public bool RequiresUpdate { get; }

    public ApiException(int statusCode, string message, bool requiresUpdate = false) : base(message)
    {
        StatusCode = statusCode;
        RequiresUpdate = requiresUpdate;
    }
}

public class ApiClient
{
    public static string BaseUrl { get; set; } =
        Obfuscation.Reveal("JRsaHx9TW0c7PyBCXllGXlsbC1chAAJADRkdRyBh");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http = new(new SigningHandler(new SocketsHttpHandler { UseProxy = false })) { Timeout = TimeSpan.FromSeconds(45) };

    private static HttpClient CreateTunnelRoutedClient(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(
                    context.DnsEndPoint.Host, AddressFamily.InterNetwork, cancellationToken);
                if (addresses.Length == 0)
                    throw new SocketException((int)SocketError.HostNotFound);

                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true,
                };
                try
                {
                    await socket.ConnectAsync(addresses[0], context.DnsEndPoint.Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        return new HttpClient(new SigningHandler(handler)) { Timeout = timeout };
    }

    private static readonly string UserAgentValue =
        $"MonolithVPN-Client/{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";

    private sealed class SigningHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (timestamp, signature) = RequestSigning.Sign(request.Method.Method, request.RequestUri!.AbsolutePath);
            request.Headers.Add("X-Client-Timestamp", timestamp);
            request.Headers.Add("X-Client-Signature", signature);
            request.Headers.UserAgent.ParseAdd(UserAgentValue);
            return base.SendAsync(request, cancellationToken);
        }
    }

    public string? Token { get; private set; }

    public void SetToken(string? token)
    {
        Token = token;
        var auth = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Authorization = auth;
    }

    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        var hwidHash = HardwareId.ComputeHash();
        var resp = await _http.PostAsJsonAsync(
            $"{BaseUrl}/auth/login",
            new { username, password, hwid_hash = hwidHash, platform = HardwareId.Platform });
        await EnsureSuccessAsync(resp);
        return await ReadRequiredAsync<LoginResponse>(resp);
    }

    public async Task LogoutAsync()
    {
        try
        {
            await _http.PostAsync($"{BaseUrl}/auth/logout", null);
        }
        catch
        {
        }
    }

    public async Task<MeResponse> GetMeAsync()
    {
        var resp = await _http.GetAsync($"{BaseUrl}/me");
        await EnsureSuccessAsync(resp);
        return await ReadRequiredAsync<MeResponse>(resp);
    }

    public async Task<List<ServerInfo>> GetServersAsync()
    {
        var resp = await _http.GetAsync($"{BaseUrl}/servers");
        await EnsureSuccessAsync(resp);
        var data = await resp.Content.ReadFromJsonAsync<ServersResponse>(JsonOptions);
        return data?.Servers ?? new List<ServerInfo>();
    }

    public async Task<List<GameInfo>> GetGamesAsync()
    {
        var resp = await _http.GetAsync($"{BaseUrl}/games");
        await EnsureSuccessAsync(resp);
        var data = await resp.Content.ReadFromJsonAsync<GamesResponse>(JsonOptions);
        return data?.Games ?? new List<GameInfo>();
    }

    public async Task<ServerInfo?> GetServerStatusAsync(int serverId)
    {
        var resp = await _http.GetAsync($"{BaseUrl}/servers/{serverId}/status");
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<ServerInfo>(JsonOptions);
    }

    public async Task<string> ConnectAsync(int serverId, string? endpointIp = null)
    {
        var resp = endpointIp is null
            ? await _http.PostAsync($"{BaseUrl}/servers/{serverId}/connect", null)
            : await _http.PostAsJsonAsync($"{BaseUrl}/servers/{serverId}/connect", new { endpoint_ip = endpointIp });
        await EnsureSuccessAsync(resp);
        var data = await resp.Content.ReadFromJsonAsync<ConnectResponse>(JsonOptions);
        return data?.Config ?? "";
    }

    public async Task NotifyDisconnectAsync(int serverId)
    {
        var resp = await _http.PostAsync($"{BaseUrl}/servers/{serverId}/disconnect", null);
        await EnsureSuccessAsync(resp);
    }

    public async Task<List<ChangelogEntryInfo>> GetChangelogAsync()
    {
        var resp = await _http.GetAsync($"{BaseUrl}/changelog");
        await EnsureSuccessAsync(resp);
        var data = await resp.Content.ReadFromJsonAsync<ChangelogResponse>(JsonOptions);
        return data?.Entries ?? new List<ChangelogEntryInfo>();
    }

    public async Task<FreeModeStatusResponse?> GetFreeModeStatusAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"{BaseUrl}/free-mode");
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<FreeModeStatusResponse>(JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<MyIpResponse> GetMyIpAsync()
    {
        using var http = CreateTunnelRoutedClient(TimeSpan.FromSeconds(15));
        if (Token is not null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        var resp = await http.GetAsync($"{BaseUrl}/my-ip");
        await EnsureSuccessAsync(resp);
        return await ReadRequiredAsync<MyIpResponse>(resp);
    }

    public async Task<AppVersionResponse> GetAppVersionAsync()
    {
        using var http = new HttpClient(new SigningHandler(new SocketsHttpHandler { UseProxy = false })) { Timeout = TimeSpan.FromSeconds(10) };
        var resp = await http.GetAsync($"{BaseUrl}/app/version");
        await EnsureSuccessAsync(resp);
        return await ReadRequiredAsync<AppVersionResponse>(resp);
    }

    public async Task<byte[]> DownloadAppUpdateAsync(string downloadUrl)
    {
        var resp = await _http.GetAsync(downloadUrl);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadAsByteArrayAsync();
    }

    private static async Task<T> ReadRequiredAsync<T>(HttpResponseMessage resp)
    {
        var data = await resp.Content.ReadFromJsonAsync<T>(JsonOptions);
        if (data is null) throw new ApiException((int)resp.StatusCode, "The server sent back an unreadable response. Try again.");
        return data;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;

        string message = $"Request failed ({(int)resp.StatusCode})";
        bool requiresUpdate = false;
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>(JsonOptions);
            if (err?.Error == "device_limit_reached")
                message = $"You've reached your plan's device limit ({err.Limit}). Log out from another device first.";
            else if (err?.Error == "too_many_new_devices")
                message = "Too many new devices were added to this account recently - wait a while and try again.";
            else if (err?.Error == "maintenance")
                message = "MonolithVPN is currently undergoing maintenance. Please try again shortly.";
            else if (err?.Error == "server_at_capacity")
                message = "This server is at capacity right now - try again shortly, or pick another one from the list.";
            else if (err?.Error == "account_terminated")
                message = "This account has been terminated. Contact support if you believe this is a mistake.";
            else if (err?.Error == "no_active_plan")
                message = "You'll need an active plan to use the app. Get one at monolithvpn.lol/pricing.";
            else if (err?.Error == "invalid_endpoint_ip")
                message = "That IP isn't available on this server anymore - refresh the server list and try again.";
            else if (err?.Error == "invalid_client_signature")
            {
                message = "This version of MonolithVPN is out of date and can no longer sign in. Update the app to continue.";
                requiresUpdate = true;
            }
            else if (!string.IsNullOrWhiteSpace(err?.Error))
                message = err!.Error!;
        }
        catch
        {
        }
        throw new ApiException((int)resp.StatusCode, message, requiresUpdate);
    }
}
