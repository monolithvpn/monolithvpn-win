using System.Collections.ObjectModel;
using System.Windows;

namespace MonolithVpnClient.Services;

public class AlertEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public Views.ToastKind Kind { get; init; } = Views.ToastKind.Info;
}

public static class AlertLog
{
    public static ObservableCollection<AlertEntry> Entries { get; } = new();

    public static void Add(string message, string toastTitle = "Alert", Views.ToastKind toastKind = Views.ToastKind.Info)
    {
        void DoAdd() => Entries.Insert(0, new AlertEntry { Title = toastTitle, Message = message, Kind = toastKind });
        if (Application.Current?.Dispatcher.CheckAccess() == false)
            Application.Current.Dispatcher.Invoke(DoAdd);
        else
            DoAdd();

        ToastService.Show(toastTitle, message, toastKind);
    }
}
