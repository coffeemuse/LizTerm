// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class SessionViewModelConnectTests
{
    private static (SessionViewModel Vm, FakeEmulatorSession Session) Create(SessionProfile? profile = null)
    {
        var session = new FakeEmulatorSession();
        if (profile is not null) session.Profile = profile;
        var vm = new SessionViewModel(session, action => action(), new FakeTextClipboard()) { ConnectTimeout = TimeSpan.FromMilliseconds(100) };
        return (vm, session);
    }

    private static TaskCompletionSource Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public void Default_timeout_is_thirty_seconds()
    {
        var session = new FakeEmulatorSession();
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard());
        Assert.Equal(TimeSpan.FromSeconds(30), vm.ConnectTimeout);
        Assert.Equal(SessionViewModel.DefaultConnectTimeout, vm.ConnectTimeout);
    }

    [Fact]
    public async Task Timeout_after_the_socket_opened_says_so_and_offers_tls()
    {
        var (vm, session) = Create();
        session.ConnectCompletion = Pending();
        var attempt = vm.ConnectCommand.ExecuteAsync(null);
        session.RaiseConnection(ConnectionState.TcpPending);
        session.RaiseConnection(ConnectionState.TelnetPending);
        await attempt;
        Assert.True(session.ConnectToken.IsCancellationRequested);
        Assert.Equal("Connection to fake.host:3270 timed out after 0 seconds. The host accepted the connection but never started a 3270 session. If that port expects TLS, turn it on in the profile.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Timeout_before_the_socket_opened_just_reports_the_timeout()
    {
        var (vm, session) = Create();
        session.ConnectCompletion = Pending();
        var attempt = vm.ConnectCommand.ExecuteAsync(null);
        session.RaiseConnection(ConnectionState.TcpPending);
        await attempt;
        Assert.Equal("Connection to fake.host:3270 timed out after 0 seconds.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Disconnect_during_a_pending_connect_cancels_it_silently()
    {
        var (vm, session) = Create();
        session.ConnectCompletion = Pending();
        var attempt = vm.ConnectCommand.ExecuteAsync(null);
        await vm.DisconnectCommand.ExecuteAsync(null);
        await attempt;
        Assert.True(session.ConnectToken.IsCancellationRequested);
        Assert.Null(vm.ErrorMessage);
        Assert.DoesNotContain("disconnect", session.Calls);
    }

    [Fact]
    public async Task Disconnect_while_connected_still_disconnects()
    {
        var (vm, session) = Create();
        await vm.ConnectCommand.ExecuteAsync(null);
        session.RaiseConnection(ConnectionState.Connected3270);
        await vm.DisconnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "disconnect"], session.Calls);
    }

    [Fact]
    public async Task A_completed_connect_clears_the_pending_state_so_the_next_attempt_gets_its_own_token()
    {
        var (vm, session) = Create();
        await vm.ConnectCommand.ExecuteAsync(null);
        var first = session.ConnectToken;
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.NotEqual(first, session.ConnectToken);
        Assert.Null(vm.ErrorMessage);
    }

    private static readonly ConnectionFailedException CertFailure = new(
        ["Connection failed:", "TLS: Host certificate verification failed:", "self-signed certificate (18)"], certificateVerificationFailed: true);

    private static (SessionViewModel Vm, FakeEmulatorSession Session, FakeCertificatePrompt Prompt, FakeCertificateFetcher Fetcher, List<SessionProfile> Saved)
        CreateWithPrompt(bool saveable, bool tls = true, CertificatePin? pinned = null, Func<SessionProfile, Task>? saveAsProfile = null)
    {
        var session = new FakeEmulatorSession { ConnectException = CertFailure };
        session.Profile = session.Profile with { UseTls = tls, Port = 4270, PinnedCertificate = pinned };
        var prompt = new FakeCertificatePrompt();
        var fetcher = new FakeCertificateFetcher();
        var saved = new List<SessionProfile>();
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard(), prompt, saveable ? saved.Add : null,
            certificateFetcher: fetcher, saveAsProfile: saveAsProfile);
        return (vm, session, prompt, fetcher, saved);
    }

    [Fact]
    public async Task Declined_prompt_shows_the_failure_and_the_request_carries_the_presented_certificate()
    {
        var (vm, session, prompt, fetcher, _) = CreateWithPrompt(saveable: true);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["fetch:fake.host:4270"], fetcher.Calls);
        Assert.Equal(["ask:fake.host:True"], prompt.Calls);
        var request = prompt.LastRequest!;
        Assert.Equal(["TLS: Host certificate verification failed:", "self-signed certificate (18)"], request.Reason);
        Assert.Same(fetcher.Result, request.Presented);
        Assert.Null(request.FetchError);
        Assert.Null(request.Previous);
        Assert.Null(request.CannotPinReason);
        Assert.Equal(["connect"], session.Calls);
        Assert.Equal(CertFailure.Message, vm.ErrorMessage);
    }

    [Fact]
    public async Task Accepted_prompt_reconnects_without_verification_once()
    {
        var (vm, session, prompt, _, saved) = CreateWithPrompt(saveable: true);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: false);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "connect:noverify"], session.Calls);
        Assert.Null(vm.ErrorMessage);
        Assert.Empty(saved);

        // Not remembered: the next attempt verifies again and asks again.
        session.ConnectException = CertFailure;
        prompt.Decision = CertificateDecision.Declined;
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "connect:noverify", "connect"], session.Calls);
        Assert.Equal(2, prompt.Calls.Count);
    }

    [Fact]
    public async Task Trusting_the_certificate_pins_it_saves_the_profile_and_stops_asking()
    {
        var (vm, session, prompt, fetcher, saved) = CreateWithPrompt(saveable: true);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);

        var profile = Assert.Single(saved);
        Assert.True(profile.VerifyCertificate);
        Assert.Equal(new CertificatePin(fetcher.Result.Sha256, fetcher.Result.Subject, fetcher.Result.Pem), profile.PinnedCertificate);
        Assert.Equal(session.Profile.Name, profile.Name);
        Assert.Null(vm.ErrorMessage);

        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "connect:pin:AA:BB", "connect:pin:AA:BB"], session.Calls);
        Assert.Single(prompt.Calls);
    }

    /// <summary>Save as Profile folds a pin taken THIS session into the profile it offers: the session's own
    /// Profile is fixed at construction, so a pin accepted mid-session lives only in _pinOverride, and without the
    /// fold a certificate the user deliberately trusted would be silently dropped by the profile the session
    /// becomes.</summary>
    [Fact]
    public async Task Save_as_profile_folds_a_pin_taken_this_session_into_the_offered_profile()
    {
        SessionProfile? offered = null;
        var (vm, session, prompt, fetcher, saved) = CreateWithPrompt(saveable: true,
            saveAsProfile: p => { offered = p; return Task.CompletedTask; });
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Single(saved); // the pin round trip itself is covered elsewhere; this just puts one in _pinOverride.

        await vm.SaveAsProfileAsync();

        Assert.NotNull(offered);
        Assert.Equal(new CertificatePin(fetcher.Result.Sha256, fetcher.Result.Subject, fetcher.Result.Pem), offered!.PinnedCertificate);
        Assert.True(offered.VerifyCertificate);
        Assert.Equal(session.Profile.Host, offered.Host);
    }

    [Fact]
    public async Task A_changed_certificate_prompts_with_the_previous_pin_and_accepting_re_pins()
    {
        var old = new CertificatePin("00:11", "CN=old", "old-pem");
        var (vm, session, prompt, _, saved) = CreateWithPrompt(saveable: true, pinned: old);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["ask:fake.host:True"], prompt.Calls);
        Assert.Same(old, prompt.LastRequest!.Previous);
        Assert.Equal(["connect", "connect:pin:AA:BB"], session.Calls);
        Assert.Equal("AA:BB", Assert.Single(saved).PinnedCertificate!.Sha256);
    }

    [Fact]
    public async Task A_failed_fetch_prompts_without_a_fingerprint_and_cannot_pin()
    {
        var (vm, session, prompt, fetcher, saved) = CreateWithPrompt(saveable: true);
        fetcher.Exception = new IOException("No TLS answer from fake.host:4270 within 10 s.");
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);
        var request = prompt.LastRequest!;
        Assert.Null(request.Presented);
        Assert.Equal(fetcher.Exception.Message, request.FetchError);
        Assert.False(request.CanPin);
        Assert.Null(request.CannotPinReason);
        // Remember cannot mean pin without a certificate, so the retry is the one-time allow.
        Assert.Equal(["connect", "connect:noverify"], session.Calls);
        Assert.Empty(saved);
    }

    [Fact]
    public async Task A_certificate_that_cannot_be_pinned_gets_the_one_time_allow_only()
    {
        var (vm, session, prompt, fetcher, saved) = CreateWithPrompt(saveable: true);
        fetcher.Result = fetcher.Result with { Pinnable = false, NotPinnableReason = "the chain is missing its root" };
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);
        var request = prompt.LastRequest!;
        Assert.False(request.CanPin);
        Assert.Equal("This certificate cannot be pinned: the chain is missing its root. Connect Anyway applies to this attempt only.", request.CannotPinReason);
        Assert.Equal(["connect", "connect:noverify"], session.Calls);
        Assert.Empty(saved);
    }

    /// <summary>Spec item 6 (plan 3d task 8): a session whose engine cannot honour a pin at all (Windows/Schannel)
    /// must never offer one, even for an otherwise-perfectly-pinnable certificate on a saved TLS profile — offering
    /// it would let a user check "Trust this certificate" believing it protects them, when the engine has no
    /// caFile toggle to enforce it with. CanPin must read false and the reason must say why, reusing the same
    /// session-level rule B3270Session enforces before ever connecting rather than recomputing it here.</summary>
    [Fact]
    public async Task An_engine_that_cannot_pin_never_offers_to()
    {
        var (vm, session, prompt, _, saved) = CreateWithPrompt(saveable: true);
        session.CanPinCertificates = false;
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);

        var request = prompt.LastRequest!;
        Assert.False(request.CanPin);
        Assert.Equal("This engine cannot verify a pinned certificate; connecting anyway applies to this attempt only.", request.CannotPinReason);
        // Remember cannot mean pin against an engine that cannot honour one, so the retry is the one-time allow.
        Assert.Equal(["connect", "connect:noverify"], session.Calls);
        Assert.Empty(saved);
    }

    /// <summary>The reader's verdict and OpenSSL's can differ (a weak key, a SHA-1 signature, an unsuitable
    /// purpose), so the pin is written only once the engine has accepted it: a refused retry saves nothing, is
    /// reported as the engine rejecting the pin, and does not hold the pin for the window, so the next prompt can
    /// offer Remember again.</summary>
    [Fact]
    public async Task A_pin_the_engine_refuses_on_the_retry_is_not_saved_and_is_offered_again_later()
    {
        var (vm, session, prompt, _, saved) = CreateWithPrompt(saveable: true);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        // The second ask (the refused retry's prompt) declines; the first has already read its decision.
        prompt.OnAsk = () => { if (prompt.Calls.Count == 1) return; prompt.Decision = CertificateDecision.Declined; };
        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(["connect", "connect:pin:AA:BB"], session.Calls);
        Assert.Equal(2, prompt.Calls.Count);
        Assert.Equal("The engine rejected the pinned certificate; connecting anyway applies to this attempt only.", prompt.LastRequest!.CannotPinReason);
        Assert.Empty(saved);
        Assert.Equal(CertFailure.Message, vm.ErrorMessage);

        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "connect:pin:AA:BB", "connect"], session.Calls);
        Assert.True(prompt.LastRequest!.CanPin);
    }

    /// <summary>The window and the view model decide "same certificate" with one comparison, so a profile file whose
    /// fingerprint was hand-edited to lower case still reads as the pin the engine just rejected.</summary>
    [Fact]
    public async Task A_lower_case_pinned_fingerprint_still_counts_as_the_same_certificate()
    {
        var current = new CertificatePin("aa:bb", "CN=fake", "pem");
        var (vm, _, prompt, _, saved) = CreateWithPrompt(saveable: true, pinned: current);
        await vm.ConnectCommand.ExecuteAsync(null);
        var request = prompt.LastRequest!;
        Assert.False(request.CanPin);
        Assert.Equal("The engine rejected the pinned certificate; connecting anyway applies to this attempt only.", request.CannotPinReason);
        Assert.Empty(saved);
    }

    [Fact]
    public async Task A_pin_the_engine_rejects_is_not_offered_again()
    {
        var current = new CertificatePin("AA:BB", "CN=fake", "pem");
        var (vm, _, prompt, _, saved) = CreateWithPrompt(saveable: true, pinned: current);
        await vm.ConnectCommand.ExecuteAsync(null);
        var request = prompt.LastRequest!;
        Assert.False(request.CanPin);
        Assert.Equal("The engine rejected the pinned certificate; connecting anyway applies to this attempt only.", request.CannotPinReason);
        Assert.Empty(saved);
    }

    [Fact]
    public async Task A_plain_profile_never_fetches_and_cannot_pin()
    {
        var (vm, _, prompt, fetcher, _) = CreateWithPrompt(saveable: true, tls: false);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Empty(fetcher.Calls);
        Assert.Equal(["ask:fake.host:False"], prompt.Calls);
        Assert.Null(prompt.LastRequest!.Presented);
        Assert.Null(prompt.LastRequest.CannotPinReason);
    }

    [Fact]
    public async Task Ad_hoc_profile_sees_the_certificate_but_cannot_pin()
    {
        var (vm, _, prompt, fetcher, _) = CreateWithPrompt(saveable: false);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Single(fetcher.Calls);
        Assert.Equal(["ask:fake.host:False"], prompt.Calls);
        Assert.NotNull(prompt.LastRequest!.Presented);
        Assert.Null(prompt.LastRequest.CannotPinReason);
    }

    [Fact]
    public async Task Without_a_prompt_the_failure_is_shown()
    {
        var session = new FakeEmulatorSession { ConnectException = CertFailure };
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard());
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect"], session.Calls);
        Assert.Equal(CertFailure.Message, vm.ErrorMessage);
    }

    [Fact]
    public async Task A_failure_after_a_one_time_allow_is_shown_and_not_asked_about()
    {
        var (vm, session, prompt, _, _) = CreateWithPrompt(saveable: true);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: false);
        prompt.OnAsk = () => session.ConnectException = new ConnectionFailedException(["Connection failed:", "Connection refused"]);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Single(prompt.Calls);
        Assert.Equal("Connection failed: Connection refused", vm.ErrorMessage);
    }

    /// <summary>The prompt is a modal window and the fetch is a socket; either can outlive the session window.</summary>
    [Fact]
    public async Task A_prompt_answered_after_the_window_closed_does_nothing()
    {
        var (vm, session, prompt, _, saved) = CreateWithPrompt(saveable: true);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        prompt.OnAsk = () => _ = vm.DisposeAsync().AsTask();
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "dispose"], session.Calls);
        Assert.Empty(saved);
    }

    /// <summary>Regression: OfferConnectAnywayAsync was awaited from inside a catch clause, so a failing profile
    /// save escaped every sibling handler and faulted the command (an unhandled UI-thread exception from the
    /// File > Connect menu).</summary>
    [Fact]
    public async Task A_profile_save_that_fails_is_reported_and_does_not_fault_the_command()
    {
        var session = new FakeEmulatorSession { ConnectException = CertFailure };
        session.Profile = session.Profile with { UseTls = true, Port = 4270 };
        var prompt = new FakeCertificatePrompt { Decision = new CertificateDecision(ConnectAnyway: true, Remember: true) };
        prompt.OnAsk = () => session.ConnectException = null;
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard(), prompt,
            _ => throw new IOException("disk full"), certificateFetcher: new FakeCertificateFetcher());
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "connect:pin:AA:BB"], session.Calls);
        Assert.Equal("Could not save the profile: disk full", vm.ErrorMessage);
    }

    [Fact]
    public async Task A_prompt_that_fails_is_reported_and_does_not_fault_the_command()
    {
        var (vm, session, prompt, _, _) = CreateWithPrompt(saveable: true);
        prompt.AskException = new InvalidOperationException("owner closed");
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect"], session.Calls);
        Assert.Equal("Could not ask about the certificate: owner closed", vm.ErrorMessage);
    }

    /// <summary>#39. Connect was a bare RelayCommand, so it re-enabled the moment a connect finished and clicking
    /// it put "Unexpected error: verifyHostCert cannot change while connected" in the banner. The boundary is
    /// the raw state, not IsConnected: b3270 refuses the Set whenever it has a host session, which begins before
    /// the 3270 session does.</summary>
    [Fact]
    public async Task Connect_is_disabled_once_the_engine_holds_a_socket()
    {
        var session = new FakeEmulatorSession();
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard());

        Assert.True(vm.ConnectCommand.CanExecute(null));

        session.RaiseConnection(ConnectionState.TelnetPending);
        Assert.False(vm.ConnectCommand.CanExecute(null));
        Assert.False(vm.IsConnected);

        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.False(vm.ConnectCommand.CanExecute(null));

        session.RaiseConnection(ConnectionState.Disconnected);
        Assert.True(vm.ConnectCommand.CanExecute(null));

        await vm.DisposeAsync();
    }

    /// <summary>Disconnect must be live for every state but Disconnected, because a connect going nowhere is
    /// exactly when a user wants to cancel it.</summary>
    [Fact]
    public async Task Disconnect_stays_live_for_a_pending_connect_and_greys_when_idle()
    {
        var session = new FakeEmulatorSession();
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard());

        Assert.False(vm.DisconnectCommand.CanExecute(null));

        session.RaiseConnection(ConnectionState.Connected3270);
        Assert.True(vm.DisconnectCommand.CanExecute(null));

        session.RaiseConnection(ConnectionState.Disconnected);
        Assert.False(vm.DisconnectCommand.CanExecute(null));

        await vm.DisposeAsync();
    }

    /// <summary>Review finding on #39: the test above never sets ConnectPending, so its whole
    /// false-to-true-to-false sequence is explained by the reported state alone and gives the "ConnectPending ||"
    /// half of CanDisconnect no regression net. This one holds a connect in flight while the state stays
    /// Disconnected throughout, so a true CanExecute here can only come from ConnectPending, and the assertion
    /// once the connect finishes pins ConnectWithAsync's finally clearing it back to false.</summary>
    [Fact]
    public async Task Disconnect_stays_live_for_a_pending_connect_that_never_reaches_a_socket()
    {
        var (vm, session) = Create();
        session.ConnectCompletion = Pending();
        Assert.False(vm.DisconnectCommand.CanExecute(null));

        var attempt = vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(ConnectionState.Disconnected, vm.Connection);
        Assert.True(vm.DisconnectCommand.CanExecute(null));

        session.ConnectCompletion.SetResult();
        await attempt;

        Assert.Equal(ConnectionState.Disconnected, vm.Connection);
        Assert.False(vm.DisconnectCommand.CanExecute(null));

        await vm.DisposeAsync();
    }

    /// <summary>Reconnecting is not a socket (Core's HasSocket excludes it), so without this the engine's own
    /// reconnect would leave Connect enabled — offering to start a second attempt over one already running —
    /// and Disconnect disabled, which is the one thing the user actually wants at that moment.</summary>
    [Fact]
    public void Connect_is_disabled_and_Disconnect_enabled_while_the_engine_reconnects()
    {
        var fake = new FakeEmulatorSession();
        var vm = new SessionViewModel(fake, a => a(), new FakeTextClipboard());

        fake.RaiseConnection(ConnectionState.Reconnecting);

        Assert.Equal(ConnectionState.Reconnecting, vm.Connection);
        Assert.False(vm.CanConnect);
        Assert.True(vm.CanDisconnect);
        Assert.False(vm.ConnectCommand.CanExecute(null));
        Assert.True(vm.DisconnectCommand.CanExecute(null));
    }

    [Fact]
    public void Leaving_the_reconnect_restores_the_two_commands()
    {
        var fake = new FakeEmulatorSession();
        var vm = new SessionViewModel(fake, a => a(), new FakeTextClipboard());

        fake.RaiseConnection(ConnectionState.Reconnecting);
        fake.RaiseConnection(ConnectionState.Disconnected);

        Assert.Equal(ConnectionState.Disconnected, vm.Connection);
        Assert.True(vm.CanConnect);
        Assert.False(vm.CanDisconnect);
    }

    /// <summary>Spec 6.4's rule over every state an engine-driven reconnect actually passes through. The guards
    /// used to derive from HasSocket, which is false for Resolving and TcpPending as well as Disconnected, so
    /// both of those offered Connect and refused Disconnect. That was invisible while those states were
    /// reachable only inside ConnectCommand, whose AsyncRelayCommand disables itself while it runs; auto-reconnect
    /// (#28) made the engine cycle Reconnecting → TcpPending → Reconnecting unprompted against a host that stays
    /// down, with no command running and ConnectPending false, so the two menu items flickered on that cycle.
    /// Asserted as a table rather than one state at a time, since the bug was in exactly the two rows the
    /// single-state tests above never covered.</summary>
    [Theory]
    [InlineData(ConnectionState.Disconnected, true, false)]
    [InlineData(ConnectionState.Reconnecting, false, true)]
    [InlineData(ConnectionState.Resolving, false, true)]
    [InlineData(ConnectionState.TcpPending, false, true)]
    [InlineData(ConnectionState.TelnetPending, false, true)]
    [InlineData(ConnectionState.Connected3270, false, true)]
    public void The_two_guards_follow_the_state_the_engine_reported(
        ConnectionState state, bool canConnect, bool canDisconnect)
    {
        var fake = new FakeEmulatorSession();
        var vm = new SessionViewModel(fake, a => a(), new FakeTextClipboard());

        fake.RaiseConnection(state);

        Assert.Equal(canConnect, vm.CanConnect);
        Assert.Equal(canDisconnect, vm.CanDisconnect);
        Assert.Equal(canConnect, vm.ConnectCommand.CanExecute(null));
        Assert.Equal(canDisconnect, vm.DisconnectCommand.CanExecute(null));
    }

    /// <summary>The two tests above prove the guard properties are correct, but IRelayCommand.CanExecute
    /// re-evaluates its predicate on every call regardless of whether CanExecuteChanged ever fired -- so they
    /// pass whether or not Connection's [NotifyCanExecuteChangedFor] attributes are present. Those
    /// attributes are what make a bound menu item actually re-evaluate; without them the guard stays correct but
    /// the UI goes stale. This asserts the notification itself.</summary>
    [Fact]
    public void Reconnecting_raises_CanExecuteChanged_on_both_commands()
    {
        var fake = new FakeEmulatorSession();
        var vm = new SessionViewModel(fake, a => a(), new FakeTextClipboard());

        var connectRaised = false;
        var disconnectRaised = false;
        vm.ConnectCommand.CanExecuteChanged += (_, _) => connectRaised = true;
        vm.DisconnectCommand.CanExecuteChanged += (_, _) => disconnectRaised = true;

        fake.RaiseConnection(ConnectionState.Reconnecting);

        Assert.True(connectRaised);
        Assert.True(disconnectRaised);
    }
}
