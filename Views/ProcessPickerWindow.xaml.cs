using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace MonolithVpnClient.Views;

public partial class ProcessPickerWindow : Window
{
    public sealed record ProcessEntry(string DisplayName, string ExePath);

    public string? SelectedPath { get; private set; }

    private readonly List<ProcessEntry> _allEntries;

    public ProcessPickerWindow()
    {
        InitializeComponent();
        _allEntries = EnumerateCandidateProcesses();
        Render(_allEntries);
    }

    private static List<ProcessEntry> EnumerateCandidateProcesses()
    {
        string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        int currentPid = Environment.ProcessId;
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ProcessEntry>();

        foreach (var proc in Process.GetProcesses())
        {
            using (proc)
            {
                try
                {
                    if (proc.Id == currentPid) continue;
                    string? path = proc.MainModule?.FileName;
                    if (string.IsNullOrEmpty(path)) continue;
                    if (path.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!seenPaths.Add(path)) continue;

                    entries.Add(new ProcessEntry(DescribeExe(path), path));
                }
                catch
                {
                }
            }
        }

        return entries.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string DescribeExe(string path)
    {
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription!;
        }
        catch
        {
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    private void Render(IEnumerable<ProcessEntry> entries)
    {
        var list = entries.ToList();
        ProcessList.ItemsSource = list;
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchBox.Text.Trim();
        Render(query.Length == 0
            ? _allEntries
            : _allEntries.Where(en =>
                en.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                en.ExePath.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private void ProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectButton.IsEnabled = ProcessList.SelectedItem is not null;
    }

    private void ProcessList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ProcessList.SelectedItem is ProcessEntry) Confirm();
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Confirm()
    {
        if (ProcessList.SelectedItem is not ProcessEntry entry) return;
        SelectedPath = entry.ExePath;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
