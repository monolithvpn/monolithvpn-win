using System.Windows;
using MonolithVpnClient.Views;

namespace MonolithVpnClient.Services;

public static class ToastService
{
    private const double Margin = 16;
    private const double Gap = 10;

    private static readonly List<ToastWindow> _active = new();

    public static void Show(string title, string message, ToastKind kind = ToastKind.Info)
    {
        void DoShow()
        {
            var toast = new ToastWindow(title, message, kind);
            toast.Closed += (_, _) =>
            {
                _active.Remove(toast);
                RestackAll();
            };

            var area = SystemParameters.WorkArea;
            toast.Left = area.Right - toast.Width - Margin;
            toast.Top = NextTop(area);

            _active.Add(toast);
            toast.Show();
        }

        if (Application.Current?.Dispatcher.CheckAccess() == false)
            Application.Current.Dispatcher.Invoke(DoShow);
        else
            DoShow();
    }

    private static double NextTop(Rect area)
    {
        double top = area.Bottom - Margin;
        foreach (var t in _active)
            top -= t.ActualHeight > 0 ? t.ActualHeight + Gap : 90 + Gap;
        return top - 90;
    }

    private static void RestackAll()
    {
        var area = SystemParameters.WorkArea;
        double top = area.Bottom - Margin;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var t = _active[i];
            double height = t.ActualHeight > 0 ? t.ActualHeight : 90;
            top -= height;
            t.Top = top;
            top -= Gap;
        }
    }
}
