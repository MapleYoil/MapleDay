using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using MapleDay.Services;

namespace MapleDay;

public partial class App : Application
{
    internal static DiagnosticQueue Diagnostics { get; } = new();
    private Window? _window;
    private AppInstance? _instance;

    public App()
    {
        Diagnostics.SetEnabled(AppSettings.Load().AutomaticErrorReports);
        UnhandledException += (_, args) => Capture(args.Exception, "unhandled", true);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        { if (args.ExceptionObject is Exception error) Capture(error, "unhandled", true); };
        TaskScheduler.UnobservedTaskException += (_, args) => Capture(args.Exception, "background", false);
        try { InitializeComponent(); }
        catch (Exception error) { Capture(error, "startup", true); throw; }
    }
    private static void Capture(Exception error, string category, bool fatal) => Diagnostics.Capture(error, category,
        typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0, 0), fatal);

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _instance = AppInstance.FindOrRegisterForKey("MapleDay");
        if (!_instance.IsCurrent)
        {
            await _instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
            Exit();
            return;
        }
        _instance.Activated += (_, activation) => _window?.DispatcherQueue.TryEnqueue(() => ((MainWindow)_window).ShowFromActivation(activation));
        var initialActivation = AppInstance.GetCurrent().GetActivatedEventArgs();
        _window = new MainWindow();
        ((MainWindow)_window).Start(WindowsStartup.IsStartupLaunch(
            initialActivation.Kind == ExtendedActivationKind.StartupTask,
            initialActivation.Kind == ExtendedActivationKind.Launch ? Environment.GetCommandLineArgs().Skip(1) : []));
        if (initialActivation.Kind == ExtendedActivationKind.AppNotification)
            ((MainWindow)_window).ShowFromActivation(initialActivation);
    }
}
