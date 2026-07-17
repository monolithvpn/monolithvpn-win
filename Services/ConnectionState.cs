using MonolithVpnClient.Models;

namespace MonolithVpnClient.Services;

public static class ConnectionState
{
    public static event Action? Changed;

    private static ServerInfo? _current;
    private static bool _isRecovering;
    private static DateTime? _nextRetryAt;

    public static ServerInfo? Current
    {
        get => _current;
        set
        {
            _current = value;
            Changed?.Invoke();
        }
    }

    public static bool IsRecovering
    {
        get => _isRecovering;
        set { _isRecovering = value; Changed?.Invoke(); }
    }

    public static DateTime? NextRetryAt
    {
        get => _nextRetryAt;
        set { _nextRetryAt = value; Changed?.Invoke(); }
    }
}
