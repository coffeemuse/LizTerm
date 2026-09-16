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
using LizTerm.App.HostFiles;
using LizTerm.App.Menus;
using LizTerm.App.Sessions;
using LizTerm.App.Startup;
using LizTerm.App.Updates;
using LizTerm.App.ViewModels;
using LizTerm.App.Views;
using LizTerm.Core.Profiles;
using LizTerm.Core.Security;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;
using LizTerm.Core.Updates;

namespace LizTerm.App;

public partial class App : Application
{
    /// <summary>The process's one record of open sessions (#46): opening order for the Window and Dock menus' numbers,
    /// use order for the switcher and for "the session the user was last in", which About and the update check read.
    /// Each window adds itself through AttachSessions and removes itself in OnClosed, before Closed is raised.</summary>
    private readonly SessionList _sessions = new();

    public App() => _sessions.Changed += (_, _) => KeepAppWindowsOnTop();

    /// <summary>For tests, which seed sessions to see what the app's own windows do.</summary>
    internal SessionList Sessions => _sessions;

    /// <summary>The app's own windows belong to no session, so none can follow one owner's Keep on Top the way a
    /// dialog does (ModalDialogs). On macOS a Keep on Top session window would cover them, so they are kept on top
    /// while any session is. An owned About or update check follows its owner instead.</summary>
    private void KeepAppWindowsOnTop()
    {
        foreach (var window in new Window?[] { _picker, _preferences, _about, _updateCheck })
            if (window is { Owner: null }) KeepOnTopWithSessions(window);
    }

    private void KeepOnTopWithSessions(Window window) => window.Topmost = _sessions.AnyKeepOnTop;
    /// <summary>The process's one ringer: what it can ring is the answer Preferences shows, so they cannot drift.</summary>
    private readonly SystemBellRinger _bellRinger = new();
    /// <summary>The process's one release checker (#107).</summary>
    private readonly IReleaseChecker _releaseChecker = GitHubReleaseChecker.Create();
    private ProfilePickerWindow? _picker;
    private ProfileStore? _store;
    private TagRegistryStore? _tags;
    private RecentHostsStore? _recentHosts;
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

            // The Dock icon's menu, on macOS only (#46): the open sessions and New Session..., current for the life
            // of the process.
            DockMenu.Attach(this, _sessions, ShowPicker, OperatingSystem.IsMacOS());

            // Settings before anything opens: whether there is a splash at all is one of them (#108). Load never
            // throws, so reading them first adds no way for startup to fail before a window can say so.
            _settings = new SettingsViewModel(new SettingsStore(AppPaths.SettingsFile()));
            // LIZTERM_MENU seeds this instance and nothing else: in memory, never written, and overridable from
            // Preferences for the rest of the session (#70). A variable naming no style leaves the saved
            // preference to decide.
            if (MenuStrategy.FromVariable(Environment.GetEnvironmentVariable(MenuStrategy.Variable)) is { } seeded)
                _settings.SeedMenuStyle(seeded);

            var gate = new StartupGate(Execute);
            OpenSplash(gate, _settings.Current);

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

    /// <summary>The splash, or none (#108). Turned off, nothing opens and the gate is told at once, so the plan runs
    /// the moment it is known: no splash at all rather than a zero-length one. On, the gate runs the plan once the
    /// splash has closed and the plan is known, in whichever order those happen — under OnExplicitShutdown a missed
    /// plan would leave the process running with no window and no way to quit.</summary>
    internal static SplashWindow? OpenSplash(StartupGate gate, AppSettings settings)
    {
        if (!settings.ShowSplashOnLaunch)
        {
            gate.SplashClosed();
            return null;
        }
        // Subscribe before Show(): a splash whose maximum has already elapsed closes from inside Opened, i.e.
        // inside Show() itself.
        var splash = new SplashWindow();
        splash.Closed += (_, _) => gate.SplashClosed();
        splash.Show();
        splash.Activate();
        return splash;
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
                    _ = CheckForUpdatesOnStartupAsync();
                    break;
                default:
                    ShowPicker();
                    _ = CheckForUpdatesOnStartupAsync();
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
        var window = new SessionWindow(Settings.MenuStyle, OperatingSystem.IsMacOS());
        var store = _store ??= new ProfileStore(AppPaths.ProfilesDirectory());
        // One snapshot of the tag colours for both the window's chips and its row in the switcher, so the two agree.
        // Load never throws (an unreadable file is an empty registry, and every chip draws grey).
        var tags = (_tags ??= new TagRegistryStore(TagRegistryStore.DefaultFile())).Load();
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
                if (await new ProfileEditorWindow(profile).ShowDialogAbove<ProfileEdit?>(window) is not { } edit) return;
                // The same read-back ProfilePickerViewModel.EditAsync does, for the same reason: this window's
                // profile was fixed at construction, so the file under that name can already hold a pin written
                // since — by the picker, or by another session window's WritePinBack. Overwriting the rest is
                // what the user asked for; dropping a pin they never saw in this editor is not — the REST pin included.
                store.Save(PinMerge.Apply(edit.Profile, store.Load(edit.Profile.Name), edit.PinCleared, edit.HostFilesPinCleared));
            },
            settings: Settings,
            bellRinger: _bellRinger,
            uriOpener: new AvaloniaUriOpener(window),
            // A snapshot, like the profile: a Manage Tags recolour reaches the next window rather than this one.
            tags: tags);
        window.DataContext = viewModel;
        var entry = new SessionEntry(viewModel, new ProfileRow(profile, tags, isSaved: fromStore), fromStore, window);
        _sessions.Add(entry);
        window.AttachSessions(_sessions, entry);
        if (!string.IsNullOrWhiteSpace(profile.HostFilesUrl))
        {
            // A saved profile can keep a certificate the user trusts; an ad hoc one has nowhere to put it.
            window.AttachHostFiles(new HostFileAccess(profile, HostFileServiceFactory.Create,
                fromStore ? pin => WriteHostFilesPinBack(store, profile, pin) : null));
        }
        // Read in Closing, because Closed carries no reason and by then the shutdown that is closing this window
        // is already counting the windows that are left. A close the owned File Transfer dialog refuses never
        // reaches Closing at all (Window.ShouldCancelClose asks the children first), and the next close attempt
        // overwrites this, so it always describes the close that is actually finishing.
        var shutdownClose = false;
        window.Closing += (_, e) => shutdownClose = ShutdownPolicy.IsShutdown(e.CloseReason);
        window.Closed += async (_, _) =>
        {
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

    /// <summary>The REST pin's write-back, merged into the profile as it is on disk now, for the reason
    /// <see cref="WritePinBack"/> gives.</summary>
    private static void WriteHostFilesPinBack(ProfileStore store, SessionProfile profile, CertificatePin pin) =>
        store.Update(profile, current => current with { HostFilesPinnedCertificate = pin });

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
        _picker = new ProfilePickerWindow(_store ??= new ProfileStore(AppPaths.ProfilesDirectory()),
            (profile, fromStore) => OpenSession(profile, fromStore), Quit,
            _tags ??= new TagRegistryStore(TagRegistryStore.DefaultFile()),
            _recentHosts ??= new RecentHostsStore(RecentHostsStore.DefaultFile()));
        // The same reason test the session windows get, for the mirror-image failure: a shutdown that closes the
        // picker would otherwise be answered with Quit() -> Shutdown(), a second DoShutdown re-entered inside the
        // first, which fires Exit twice.
        var shutdownClose = false;
        _picker.Closing += (_, e) => shutdownClose = ShutdownPolicy.IsShutdown(e.CloseReason);
        _picker.Closed += (_, _) => { if (ShutdownPolicy.UserClosedLastWindow(_quitting, shutdownClose, _sessions.Count)) { /* picker closed with the X: treat as quit */ Quit(); } };
        KeepOnTopWithSessions(_picker);
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
        if (owner is null)
        {
            KeepOnTopWithSessions(about);
            about.Show();
        }
        else await about.ShowDialogAbove(owner);
    }

    private void OnPreferencesClick(object? sender, EventArgs e) => ShowPreferences();

    private PreferencesWindow? _preferences;
    private UpdateCheckWindow? _updateCheck;

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
            settings,
            _bellRinger.CanRing(BellSound.SystemAlert),
            MenuStrategy.MenuStyleChoosable(OperatingSystem.IsMacOS()));
        _preferences = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_preferences, window)) _preferences = null; };
        KeepOnTopWithSessions(window);
        window.Show();
        return window;
    }

    /// <summary>Runs once at startup (fired from Execute, never after ShowError or a failed open). Silent unless
    /// CheckForUpdatesAutomatically is on, the check finds a newer release, and that release is not the one the
    /// user already skipped.</summary>
    public Task<UpdateCheckWindow?> CheckForUpdatesOnStartupAsync() => CheckForUpdatesOnStartupAsync(_releaseChecker, Settings);

    /// <summary>The rule with the checker and settings as arguments, so a test can exercise it with a
    /// FakeReleaseChecker and an in-memory SettingsViewModel and never touch the network or the real settings
    /// file — ShowPreferences(SettingsViewModel)'s shape.</summary>
    internal async Task<UpdateCheckWindow?> CheckForUpdatesOnStartupAsync(IReleaseChecker checker, SettingsViewModel settings)
    {
        if (!settings.Current.CheckForUpdatesAutomatically) return null;
        try
        {
            var result = await CheckOnceAsync(checker);
            // Asked again after the wait, not only before it: the user may have turned automatic checking off in
            // Preferences while the request was out.
            if (!settings.Current.CheckForUpdatesAutomatically
                || !UpdateNotificationPolicy.ShouldShowAutomatically(result, settings.Current.SkippedUpdateVersion)) return null;
            // Nobody asked for this result, so it belongs to the window the user works in — the session they were
            // last in, else the picker — and never to whatever is in front of it, such as a certificate prompt
            // still waiting for an answer.
            return await ShowUpdateCheckResultAsync(result, (_sessions.Current?.Host as Window) ?? _picker, settings);
        }
        catch
        {
            // An unattended check is not worth a crash.
            return null;
        }
    }

    /// <summary>Help &gt; Check for Updates..., always reporting something — newer, up to date, or the failure
    /// reason — and ignoring any skipped version, because the user asked directly.</summary>
    public Task<UpdateCheckWindow> CheckForUpdatesManuallyAsync(Window? owner) => CheckForUpdatesManuallyAsync(owner, _releaseChecker, Settings);

    internal async Task<UpdateCheckWindow> CheckForUpdatesManuallyAsync(Window? owner, IReleaseChecker checker, SettingsViewModel settings)
    {
        // A result already on screen is brought forward before GitHub is asked again: the one-at-a-time rule in
        // ShowUpdateCheckResultAsync would only throw a fresh answer away for it.
        if (_updateCheck is { } showing)
        {
            showing.Activate();
            return showing;
        }
        var result = await CheckOnceAsync(checker);
        return await ShowUpdateCheckResultAsync(result, owner, settings);
    }

    /// <summary>The request still out, so checks that overlap — a second click while GitHub is slow, a click during
    /// the startup check — share one request and so one dialog.</summary>
    private Task<UpdateCheckResult>? _checking;

    private Task<UpdateCheckResult> CheckOnceAsync(IReleaseChecker checker)
    {
        if (_checking is { } running) return running;
        var check = RunCheckAsync(checker);
        // Only a request still running is kept: a finished one must never answer the next check.
        if (!check.IsCompleted) _checking = check;
        return check;
    }

    private async Task<UpdateCheckResult> RunCheckAsync(IReleaseChecker checker)
    {
        try
        {
            return await UpdateChecker.CheckAsync(checker, AppVersion.Current, CancellationToken.None);
        }
        finally
        {
            _checking = null;
        }
    }

    /// <summary>One at a time, the _about/_preferences shape. The dialog opens its release page through an
    /// AvaloniaUriOpener it builds on itself (there is no app-wide opener; it is always tied to whichever window it
    /// acts through, as SessionWindow's own is). Skip writes SkippedUpdateVersion through the settings object this
    /// call was given, so a test's in-memory settings are what change, never the real file.</summary>
    private async Task<UpdateCheckWindow> ShowUpdateCheckResultAsync(UpdateCheckResult result, Window? owner, SettingsViewModel settings)
    {
        if (_updateCheck is { } showing)
        {
            showing.Activate();
            return showing;
        }
        var window = new UpdateCheckWindow(result, AppVersion.Current, onSkip: version => settings.SkippedUpdateVersion = version);
        _updateCheck = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_updateCheck, window)) _updateCheck = null; };
        // Unlike About's, this owner was chosen before the request went out, and the user may have closed it while
        // waiting; ShowDialog throws for an owner that is closed or hidden.
        var target = owner is { IsVisible: true } ? owner : ActiveWindow();
        try
        {
            if (target is null)
            {
                KeepOnTopWithSessions(window);
                window.Show();
            }
            else await window.ShowDialogAbove(target);
        }
        catch
        {
            // A window that never opened never raises Closed, so the slot is given back here, or every later check
            // would activate an invisible window and show nothing.
            if (ReferenceEquals(_updateCheck, window)) _updateCheck = null;
            throw;
        }
        return window;
    }

    /// <summary>Which engine About describes. Deliberately not "whatever the owner window happens to be": the
    /// application menu's owner is only the window in front, and a File Transfer dialog, the picker or the
    /// splash would each answer with no session and send About back to the located binary — reporting no
    /// version for an engine that has been running and has told us one. The session the user was last in is the
    /// honest answer, and only a run with no session at all falls back to the binary on disk.</summary>
    private EngineInfo AboutEngine(Window? preferredOwner) =>
        AboutEngine(preferredOwner?.DataContext, _sessions.Current?.Session, SessionFactory.CheckBackendOrUnknown);

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
