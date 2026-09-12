// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LizTerm.App.Bell;
using LizTerm.App.Clipboard;
using LizTerm.App.Dialogs;
using LizTerm.App.Files;
using LizTerm.App.Menus;
using LizTerm.App.Startup;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Profiles;
using LizTerm.Core.Security;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App;

public partial class App : Application
{
    private readonly List<SessionWindow> _sessions = [];
    /// <summary>The process's one ringer: what it can ring is the answer Preferences shows, so they cannot drift.</summary>
    private readonly SystemBellRinger _bellRinger = new();
    /// <summary>The session window the user was in most recently, which is what About describes when it is
    /// opened from the application menu with something else — the picker, a dialog — in front.</summary>
    private SessionWindow? _lastActiveSession;
    private ProfilePickerWindow? _picker;
    private ProfileStore? _store;
    private SettingsViewModel? _settings;

    /// <summary>The process's one settings object, for every session window and for Preferences. Lazy with ??=
    /// for the same reason _store is: the headless test lifetime never runs OnFrameworkInitializationCompleted.</summary>
    internal SettingsViewModel Settings => _settings ??= new SettingsViewModel(new SettingsStore(AppPaths.SettingsFile()));

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
            _settings = new SettingsViewModel(new SettingsStore(AppPaths.SettingsFile()));
            // LIZTERM_MENU seeds this instance and nothing else: in memory, never written, and overridable from
            // Preferences for the rest of the session (#70). A variable naming no style leaves the saved
            // preference to decide.
            if (MenuStrategy.FromVariable(Environment.GetEnvironmentVariable(MenuStrategy.Variable)) is { } seeded)
                _settings.SeedMenuStyle(seeded);
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
        var window = new SessionWindow(MenuStrategy.Resolve(Settings.MenuStyle, OperatingSystem.IsMacOS()));
        var store = _store ??= new ProfileStore(AppPaths.ProfilesDirectory());
        var viewModel = new SessionViewModel(
            SessionFactory.Create(profile),
            action => Dispatcher.UIThread.Post(action),
            new AvaloniaTextClipboard(window),
            new AvaloniaCertificatePrompt(window),
            fromStore ? updated => WritePinBack(store, updated) : null,
            new AvaloniaFolderOpener(window),
            new SslStreamCertificateFetcher(),
            async profile =>
            {
                // The existing editor, pre-filled: it already carries every row, validates them, and knows the
                // model and code-page catalogues. Saving by name overwrites, exactly as the picker's New does.
                if (await new ProfileEditorWindow(profile).ShowDialog<ProfileEdit?>(window) is not { } edit) return;
                // The same read-back ProfilePickerViewModel.EditAsync does, for the same reason: this window's
                // profile was fixed at construction, so the file under that name can already hold a pin written
                // since — by the picker, or by another session window's WritePinBack. Overwriting the rest is
                // what the user asked for; dropping a pin they never saw in this editor is not.
                store.Save(edit.Profile with
                {
                    PinnedCertificate = PinMerge.Resolve(edit.Profile, store.Load(edit.Profile.Name), edit.PinCleared),
                });
            },
            settings: Settings,
            bellRinger: _bellRinger);
        window.DataContext = viewModel;
        _sessions.Add(window);
        _lastActiveSession ??= window;
        window.Activated += (_, _) => _lastActiveSession = window;
        // Read in Closing, because Closed carries no reason and by then the shutdown that is closing this window
        // is already counting the windows that are left. A close the owned File Transfer dialog refuses never
        // reaches Closing at all (Window.ShouldCancelClose asks the children first), and the next close attempt
        // overwrites this, so it always describes the close that is actually finishing.
        var shutdownClose = false;
        window.Closing += (_, e) => shutdownClose = ShutdownPolicy.IsShutdown(e.CloseReason);
        window.Closed += async (_, _) =>
        {
            _sessions.Remove(window);
            // A closed window must not keep answering for About; fall back to whichever session is left.
            if (ReferenceEquals(_lastActiveSession, window)) _lastActiveSession = _sessions.LastOrDefault();
            try { await viewModel.DisposeAsync(); }
            catch { /* the window is gone; nothing more to do with a failed disposal */ }
            if (ShutdownPolicy.UserClosedLastWindow(_quitting, shutdownClose, _sessions.Count)) ShowPicker();
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
        // ??=, as OpenSession does: one store for the process, rather than a throwaway here and a cached one
        // there. Nothing depends on the identity today — a ProfileStore holds only its directory — but two
        // spellings a few lines apart read as a distinction that does not exist.
        _picker = new ProfilePickerWindow(_store ??= new ProfileStore(AppPaths.ProfilesDirectory()), (profile, fromStore) => OpenSession(profile, fromStore), Quit);
        // The same reason test the session windows get, for the mirror-image failure: a shutdown that closes the
        // picker would otherwise be answered with Quit() -> Shutdown(), a second DoShutdown re-entered inside the
        // first, which fires Exit twice.
        var shutdownClose = false;
        _picker.Closing += (_, e) => shutdownClose = ShutdownPolicy.IsShutdown(e.CloseReason);
        _picker.Closed += (_, _) => { if (ShutdownPolicy.UserClosedLastWindow(_quitting, shutdownClose, _sessions.Count)) { /* picker closed with the X: treat as quit */ Quit(); } };
        _picker.Show();
    }

    private bool _quitting;

    public void Quit()
    {
        _quitting = true;
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    /// <summary>The application menu fires with no owner of its own: ShowAboutAsync takes the active window.
    /// A failure here has no error banner to reach, and About is not worth taking the process down for.</summary>
    private async void OnAboutClick(object? sender, EventArgs e)
    {
        try { await ShowAboutAsync(null); }
        catch { /* nothing to report it on, and nothing about About is worth a crash */ }
    }

    private AboutWindow? _about;

    /// <summary>The one spelling of About, shared by a session window's Help item and the macOS application
    /// menu. The application menu may fire with no session at all, which is why the engine has a session-less
    /// fallback and the owner has a null one.</summary>
    public async Task ShowAboutAsync(Window? preferredOwner)
    {
        // One at a time. A session's Help item cannot be reached while About is modal over that window, but the
        // macOS menu bar stays live over a modal dialog, so the application menu could open About again — owned
        // by the About already showing, and reporting no engine, because an AboutWindow is not a session.
        if (_about is { } showing)
        {
            showing.Activate();
            return;
        }

        // A session's own Help item stays modal to that session even if another window is active; the
        // application menu passes null and takes whatever the user is looking at.
        var owner = preferredOwner ?? ActiveWindow();
        var about = new AboutWindow(AppVersion.Current, AboutEngine(preferredOwner), SessionFactory.OverrideOrigin);
        _about = about;
        about.Closed += (_, _) => { if (ReferenceEquals(_about, about)) _about = null; };
        if (owner is null) about.Show();
        else await about.ShowDialog(owner);
    }

    private void OnPreferencesClick(object? sender, EventArgs e) => ShowPreferences();

    private PreferencesWindow? _preferences;

    /// <summary>The one route to Preferences, for the macOS application menu and a session's Edit item alike.
    /// Modeless and unowned so the user keeps working while it is open, and one at a time: a second request
    /// activates the first. Works with only the picker open, since the settings live on the app.</summary>
    public void ShowPreferences() => ShowPreferences(Settings);

    /// <summary>The rule with the settings object as an argument, so a test can exercise it without the real
    /// settings file.</summary>
    internal PreferencesWindow ShowPreferences(SettingsViewModel settings)
    {
        if (_preferences is { } showing)
        {
            showing.Activate();
            return showing;
        }
        var window = new PreferencesWindow(
            settings, _bellRinger.CanRing(BellSound.SystemAlert), menuStyleChoosable: OperatingSystem.IsMacOS());
        _preferences = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_preferences, window)) _preferences = null; };
        window.Show();
        return window;
    }

    /// <summary>Which engine About describes. Deliberately not "whatever the owner window happens to be": the
    /// application menu's owner is only the window in front, and a File Transfer dialog, the picker or the
    /// splash would each answer with no session and send About back to the located binary — reporting no
    /// version for an engine that has been running and has told us one. The session the user was last in is the
    /// honest answer, and only a run with no session at all falls back to the binary on disk.</summary>
    private EngineInfo AboutEngine(Window? preferredOwner) =>
        AboutEngine(preferredOwner?.DataContext, _lastActiveSession?.DataContext, SessionFactory.CheckBackendOrUnknown);

    /// <summary>The rule alone, so it can be asserted without a window manager.</summary>
    internal static EngineInfo AboutEngine(object? ownerContext, object? lastSessionContext, Func<EngineInfo> located) =>
        (ownerContext as SessionViewModel)?.Engine
        ?? (lastSessionContext as SessionViewModel)?.Engine
        ?? located();

    /// <summary>The window an ownerless About should be modal over. The splash is skipped: it closes itself on a
    /// timer and takes its owned windows with it, so an About parented to it would vanish mid-read.</summary>
    private Window? ActiveWindow()
    {
        var windows = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
            .Where(w => w is not SplashWindow).ToList();
        return windows?.FirstOrDefault(w => w.IsActive) ?? windows?.FirstOrDefault();
    }
}
