using System.Diagnostics;
using System.Windows.Threading;

namespace MonolithVpnClient.Services;

public static class KillSwitchService
{
    private const string RuleName = "MonolithVPN-KillSwitch";
    private static DispatcherTimer? _monitor;
    private static int? _watchedServerId;
    private static int _downStreak;
    private static int _generation;

    public static bool IsEngaged { get; private set; }

    public static async Task ClearStaleRuleAsync()
    {
        await RemoveRuleAsync();
    }

    public static void ArmForTunnel(int serverId)
    {
        if (!AppSettings.Current.KillSwitchEnabled) return;

        _watchedServerId = serverId;
        _downStreak = 0;
        _generation++;
        _monitor ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _monitor.Tick -= OnTick;
        _monitor.Tick += OnTick;
        _monitor.Start();
    }

    public static async Task DisarmAsync()
    {
        _monitor?.Stop();
        _watchedServerId = null;
        _downStreak = 0;
        _generation++;
        await RemoveRuleAsync();
    }

    private static async void OnTick(object? sender, EventArgs e)
    {
        if (_watchedServerId is not int serverId || TunnelTransition.InProgress) return;
        int generation = _generation;

        bool running = await WireGuardService.IsTunnelRunningAsync(serverId);

        if (generation != _generation || _watchedServerId != serverId || TunnelTransition.InProgress) return;

        if (running)
        {
            _downStreak = 0;
            return;
        }

        _downStreak++;
        if (_downStreak >= 3 && !IsEngaged)
            await EngageAsync();
    }

    private static async Task EngageAsync()
    {
        await AddRuleAsync();
        IsEngaged = true;
        AlertLog.Add(
            "The VPN tunnel dropped unexpectedly - kill switch blocked all internet traffic to prevent a leak. Reconnect from the Servers tab to restore access.",
            "Kill switch engaged", Views.ToastKind.Error);
    }

    private static async Task AddRuleAsync()
    {
        await RunNetshAsync($"advfirewall firewall add rule name=\"{RuleName}\" dir=out action=block enable=yes profile=any");
    }

    private static async Task RemoveRuleAsync()
    {
        IsEngaged = false;
        await RunNetshAsync($"advfirewall firewall delete rule name=\"{RuleName}\"");
    }

    private static async Task RunNetshAsync(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi)!;
            await proc.WaitForExitAsync();
        }
        catch
        {
        }
    }
}
