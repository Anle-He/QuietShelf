using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace QuietShelf;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\QuietShelf.SingleInstance";
    private const string ActivationEventName = @"Local\QuietShelf.Activate";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        _instanceMutex = new Mutex(initiallyOwned: false, InstanceMutexName);
        try
        {
            _ownsInstanceMutex = _instanceMutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsInstanceMutex = true;
        }
        if (!_ownsInstanceMutex)
        {
            _activationEvent.Set();
            Shutdown();
            return;
        }

        _activationWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            static (state, timedOut) =>
            {
                if (!timedOut && state is App app)
                {
                    _ = app.Dispatcher.BeginInvoke(app.ActivateCurrentWindow);
                }
            },
            this,
            Timeout.Infinite,
            executeOnlyOnce: false);

        ApplicationAccentColorManager.Apply(
            Color.FromRgb(0x35, 0x66, 0x4F),
            ApplicationTheme.Light,
            systemGlassColor: false,
            systemAccentColor: false);

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationWait?.Unregister(null);
        if (_ownsInstanceMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }
        _instanceMutex?.Dispose();
        _activationEvent?.Dispose();
        base.OnExit(e);
    }

    private void ActivateCurrentWindow()
    {
        var window = Windows.OfType<Window>().FirstOrDefault(candidate => candidate.IsActive)
                     ?? Windows.OfType<Window>().LastOrDefault(candidate => candidate.IsVisible)
                     ?? MainWindow;
        if (window is null)
        {
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }
}
