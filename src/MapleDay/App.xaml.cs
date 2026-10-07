using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using MapleDay.Services;

namespace MapleDay;

public partial class App : Application
{
    private Window? _window;
    private AppInstance? _instance;

    public App() => InitializeComponent();

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
