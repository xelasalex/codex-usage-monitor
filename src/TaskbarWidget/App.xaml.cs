using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Windows;
using Application = System.Windows.Application;

namespace TaskbarWidget;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    static Mutex? _mutex;
    protected override async void OnStartup(StartupEventArgs e) {
      if (e.Args.Contains("--selftest-all")) {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try { TaskbarTracker.SelfTest(); Autostart.SelfTest(); TaskbarWidget.MainWindow.DragTest(); TaskbarWidget.MainWindow.StyleTest(); UiSettings.SelfTest(); await TaskbarWidget.MainWindow.SecuritySelfTestAsync(); Shutdown(0); }
        catch { Shutdown(1); }
        return;
      }
      _mutex = new Mutex(true, "CodexUsageMonitorWidget", out bool fresh);
      if (!fresh) { _mutex.Dispose(); _mutex = null; Shutdown(); return; }
      base.OnStartup(e);
      MainWindow = new MainWindow();
      MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) {
      if (_mutex is not null) { _mutex.ReleaseMutex(); _mutex.Dispose(); _mutex = null; }
      base.OnExit(e);
    }
    public static void RestartApp() {
      try {
        var exe = Environment.ProcessPath;
        Current.MainWindow?.Close();
        if (_mutex is not null) { _mutex.ReleaseMutex(); _mutex.Dispose(); _mutex = null; }
        if (exe is not null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
      } catch {}
      Current.Shutdown();
    }
}

