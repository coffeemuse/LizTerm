using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LizTerm.App.Clipboard;
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
            _store = new ProfileStore(ProfileStore.DefaultDirectory());
            var profile = StartupArguments.Parse(desktop.Args ?? []).Resolve(_store.LoadAll());
            if (profile is not null) OpenSession(profile);
            else ShowPicker();
        }
        base.OnFrameworkInitializationCompleted();
    }

    public void OpenSession(SessionProfile profile)
    {
        var window = new SessionWindow();
        var viewModel = new SessionViewModel(SessionFactory.Create(profile), action => Dispatcher.UIThread.Post(action), new AvaloniaTextClipboard(window));
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
        _picker = new ProfilePickerWindow(_store ?? new ProfileStore(ProfileStore.DefaultDirectory()), OpenSession, Quit);
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
