using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LizTerm.App.Clipboard;
using LizTerm.App.Dialogs;
using LizTerm.App.Startup;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Profiles;
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
            _store = new ProfileStore(AppPaths.ProfilesDirectory());
            var startupArguments = StartupArguments.Parse(desktop.Args ?? []);
            var profile = startupArguments.Resolve(_store.LoadAll());
            if (profile is not null) OpenSession(profile, fromStore: startupArguments.ProfileName is not null);
            else ShowPicker();
        }
        base.OnFrameworkInitializationCompleted();
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
            fromStore ? store.Save : null);
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
