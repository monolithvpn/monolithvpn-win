using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace MonolithVpnClient.Services;

public class ConnectionHistoryEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.Now;

    [JsonPropertyName("server_name")]
    public string ServerName { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";
}

public static class ConnectionHistory
{
    private const int MaxEntries = 200;

    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MonolithVPN", "history.json");

    private static ObservableCollection<ConnectionHistoryEntry>? _entries;

    public static ObservableCollection<ConnectionHistoryEntry> Entries => _entries ??= Load();

    public static void Add(string serverName, string action)
    {
        void DoAdd()
        {
            Entries.Insert(0, new ConnectionHistoryEntry { ServerName = serverName, Action = action });
            while (Entries.Count > MaxEntries) Entries.RemoveAt(Entries.Count - 1);
            Save();
        }

        if (Application.Current?.Dispatcher.CheckAccess() == false)
            Application.Current.Dispatcher.Invoke(DoAdd);
        else
            DoAdd();
    }

    private static ObservableCollection<ConnectionHistoryEntry> Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var list = JsonSerializer.Deserialize<List<ConnectionHistoryEntry>>(File.ReadAllText(StorePath));
                if (list is not null) return new ObservableCollection<ConnectionHistoryEntry>(list);
            }
        }
        catch
        {
        }
        return new ObservableCollection<ConnectionHistoryEntry>();
    }

    private static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(Entries.ToList()));
        }
        catch
        {
        }
    }
}
