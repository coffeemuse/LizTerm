using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LizTerm.App.Clipboard;
using LizTerm.App.Dialogs;
using LizTerm.App.Files;
using LizTerm.App.Startup;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Profiles;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.App;

public partial class App : Application
{
    private readonly List<SessionWindow> _sessions = [];
    private ProfilePickerWindow? _picker;
    private ProfileStore? _store;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Closing the last session window returns to the picker; only Quit ends the process.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Subscribe before Show(): a splash whose maximum has already elapsed closes from inside Opened, i.e.
            // inside Show() itself. The gate then runs the plan once the splash has closed and the plan is known,
            // in whichever order those happen — under OnExplicitShutdown a missed plan would leave the process
            // running with no window and no way to quit.
            var gate = new StartupGate(Execute);
            var splash = new SplashWindow();
            splash.Closed += (_, _) => gate.SplashClosed();
            splash.Show();
            splash.Activate();

            _store = new ProfileStore(AppPaths.ProfilesDirectory());
            string? backendError = null;
            try
            {
                SessionFactory.CheckBackend();
            }
            catch (BackendUnavailableException ex)
            {
                backendError = ex.Message;
            }
            var arguments = StartupArguments.Parse(desktop.Args ?? []);
            if (arguments.Error is not null) Console.Error.WriteLine(arguments.Error);

            // Nothing else opens until the splash has closed, so no window renders under it.
            gate.PlanReady(StartupPlan.Decide(backendError, arguments, _store.LoadAll()));
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void Execute(StartupPlan plan)
    {
        try
        {
            switch (plan)
            {
                case StartupPlan.ShowError error:
                    var window = new StartupErrorWindow(error.Message);
                    window.Closed += (_, _) => Quit();
                    window.Show();
                    break;
                case StartupPlan.OpenSession open:
                    OpenSession(open.Profile, open.FromStore);
                    break;
                default:
                    ShowPicker();
                    break;
            }
        }
        catch (Exception ex)
        {
            // Guards against a plan opening no window at all: with ShutdownMode.OnExplicitShutdown, a stranded
            // process with no window and no way to quit would keep running invisibly. A ShowError plan already
            // tried its own window, so retrying it here would risk the same failure; just quit.
            if (plan is StartupPlan.ShowError)
            {
                Quit();
                return;
            }
            var window = new StartupErrorWindow("LizTerm could not open its first window: " + ex.Message);
            window.Closed += (_, _) => Quit();
            window.Show();
        }
    }

    /// <param name="fromStore">True for a saved profile, whose "Always allow" choice can be written back; false for
    /// an ad hoc command-line profile.</param>
    public void OpenSession(SessionProfile profile, bool fromStore)
    {
        var window = new SessionWindow();
        var store = _store ??= new ProfileStore(AppPaths.ProfilesDirectory());
        var viewModel = new SessionViewModel(
            SessionFactory.Create(profile),
            action => Dispatcher.UIThread.Post(action),
            new AvaloniaTextClipboard(window),
            new AvaloniaCertificatePrompt(window),
            fromStore ? updated => WritePinBack(store, updated) : null,
            new AvaloniaFolderOpener(window),
            new SslStreamCertificateFetcher());
        window.DataContext = viewModel;
        _sessions.Add(window);
        window.Closed += async (_, _) =>
        {
            _sessions.Remove(window);
            try { await viewModel.DisposeAsync(); }
            catch { /* the window is gone; nothing more to do with a failed disposal */ }
            if (!_quitting && _sessions.Count == 0) ShowPicker();
        };
        _picker?.Close();
        window.Show();
        _ = viewModel.ConnectCommand.ExecuteAsync(null);
    }

    /// <summary>The session's profile is fixed at construction, so the pin (and the verification it implies) is
    /// merged into the profile as it is on disk now rather than written over edits saved from the picker since.</summary>
    private static void WritePinBack(ProfileStore store, SessionProfile updated) =>
        store.Update(updated, current => current with { PinnedCertificate = updated.PinnedCertificate, VerifyCertificate = updated.VerifyCertificate });

    public void ShowPicker()
    {
        if (_picker is { IsVisible: true })
        {
            _picker.Activate();
            return;
        }
        _picker = new ProfilePickerWindow(_store ?? new ProfileStore(AppPaths.ProfilesDirectory()), profile => OpenSession(profile, fromStore: true), Quit);
        _picker.Closed += (_, _) => { if (_sessions.Count == 0 && !_quitting) { /* picker closed with the X: treat as quit */ Quit(); } };
        _picker.Show();
    }

    private bool _quitting;

    public void Quit()
    {
        _quitting = true;
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
