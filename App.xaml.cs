using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using MonolithVpnClient.Services;

namespace MonolithVpnClient;

public partial class App : Application
{
    private const string MutexName = "MonolithVPN-SingleInstance";
    private static Mutex? _singleInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        bool allowMultiple = e.Args.Contains("--allow-multiple");
        if (!allowMultiple)
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "MonolithVPN is already running. Check your system tray.",
                    "MonolithVPN", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }

        await KillSwitchService.ClearStaleRuleAsync();

        SystemEvents.SessionEnding += OnSessionEnding;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.SessionEnding -= OnSessionEnding;
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
    {
        try
        {
            KillSwitchService.DisarmAsync().Wait(TimeSpan.FromSeconds(3));
        }
        catch { }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        try
        {
            AlertLog.Add(
                "Something unexpected happened in the background, but the app recovered - your connection wasn't affected.",
                "Recovered from an error", Views.ToastKind.Error);
        }
        catch { }
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) LogCrash(ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.SetObserved();
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonolithVPN");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now:u}] {ex}\n\n");
        }
        catch { }
    }
}
