using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MonolithVpnClient.Models;

public class ServerRow : INotifyPropertyChanged
{
    private ServerInfo _info;
    private bool _isBusy;
    private bool _isConnected;
    private string? _errorMessage;
    private int? _pingMs;
    private string _selectedIp = AutoIpLabel;

    public const string AutoIpLabel = "Auto (best available)";

    public ServerRow(ServerInfo info) => _info = info;

    public ServerInfo Info
    {
        get => _info;
        set
        {
            _info = value;
            if (_selectedIp != AutoIpLabel && !value.ExtraIps.Contains(_selectedIp))
                _selectedIp = AutoIpLabel;
            OnPropertyChanged(nameof(Info));
            OnPropertyChanged(nameof(DisplayLocation));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsOnline));
            OnPropertyChanged(nameof(IsOffline));
            OnPropertyChanged(nameof(IsStatusUnknown));
            OnPropertyChanged(nameof(AvatarText));
            OnPropertyChanged(nameof(HasFlag));
            OnPropertyChanged(nameof(HasTags));
            OnPropertyChanged(nameof(HasMultipleIps));
            OnPropertyChanged(nameof(IpOptions));
            OnPropertyChanged(nameof(SelectedIp));
        }
    }

    public bool HasMultipleIps => Info.ExtraIps.Count > 0;

    public IEnumerable<string> IpOptions =>
        new[] { AutoIpLabel }
            .Concat(string.IsNullOrWhiteSpace(Info.Hostname) ? Enumerable.Empty<string>() : new[] { Info.Hostname! })
            .Concat(Info.ExtraIps);

    public string SelectedIp
    {
        get => _selectedIp;
        set { _selectedIp = value; OnPropertyChanged(); }
    }

    public string? SelectedEndpointIp => _selectedIp == AutoIpLabel ? null : _selectedIp;

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(ButtonText)); }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(ButtonText)); }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); }
    }

    public int? PingMs
    {
        get => _pingMs;
        set { _pingMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(PingDisplay)); }
    }

    public string PingDisplay => PingMs switch
    {
        null => "-- ms",
        -1 => "timeout",
        var ms => $"{ms} ms",
    };

    public string DisplayLocation =>
        string.Join(", ", new[] { Info.City, Info.Country }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string StatusText => Info.Online switch
    {
        true when Info.UnderAttack => "Under attack",
        true => "Online",
        false => "Offline",
        null => "Unknown",
    };

    public bool IsOnline => Info.Online == true;
    public bool IsOffline => Info.Online == false;
    public bool IsStatusUnknown => Info.Online is null;

    public string AvatarText =>
        !string.IsNullOrWhiteSpace(Info.CountryCode) ? Info.CountryCode!.ToUpperInvariant() :
        !string.IsNullOrWhiteSpace(Info.Name) ? Info.Name![..1].ToUpperInvariant() : "?";

    public bool HasFlag => !string.IsNullOrWhiteSpace(Info.FlagUrl);
    public bool HasTags => Info.Tags.Count > 0;

    public string ButtonText => IsBusy ? "Working..." : (IsConnected ? "Disconnect" : "Connect");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
