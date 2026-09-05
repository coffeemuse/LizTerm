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
    public async Task Timeout_reports_the_host_with_the_tls_hint_after_telnet_pending()
    {
        var (vm, session) = Create();
        session.ConnectCompletion = Pending();
        var attempt = vm.ConnectCommand.ExecuteAsync(null);
        session.RaiseConnection(ConnectionState.TcpPending);
        session.RaiseConnection(ConnectionState.TelnetPending);
        await attempt;
        Assert.True(session.ConnectToken.IsCancellationRequested);
        Assert.Equal("Connection to fake.host:3270 timed out after 0 seconds. The host may require TLS. Enable it in the profile.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Timeout_without_telnet_pending_has_no_hint()
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

    private static (SessionViewModel Vm, FakeEmulatorSession Session, FakeCertificatePrompt Prompt, List<SessionProfile> Saved) CreateWithPrompt(bool saveable)
    {
        var session = new FakeEmulatorSession { ConnectException = CertFailure };
        var prompt = new FakeCertificatePrompt();
        var saved = new List<SessionProfile>();
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard(), prompt, saveable ? saved.Add : null);
        return (vm, session, prompt, saved);
    }

    [Fact]
    public async Task Declined_prompt_shows_the_failure()
    {
        var (vm, session, prompt, _) = CreateWithPrompt(saveable: true);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["ask:fake.host:True"], prompt.Calls);
        Assert.Equal(["TLS: Host certificate verification failed:", "self-signed certificate (18)"], prompt.LastReason);
        Assert.Equal(["connect"], session.Calls);
        Assert.Equal(CertFailure.Message, vm.ErrorMessage);
    }

    [Fact]
    public async Task Accepted_prompt_reconnects_without_verification_once()
    {
        var (vm, session, prompt, saved) = CreateWithPrompt(saveable: true);
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
    public async Task Remembered_prompt_saves_the_profile_and_stops_asking()
    {
        var (vm, session, prompt, saved) = CreateWithPrompt(saveable: true);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: true);
        prompt.OnAsk = () => session.ConnectException = null;
        await vm.ConnectCommand.ExecuteAsync(null);
        var profile = Assert.Single(saved);
        Assert.False(profile.VerifyCertificate);
        Assert.Equal(session.Profile.Name, profile.Name);

        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["connect", "connect:noverify", "connect:noverify"], session.Calls);
        Assert.Single(prompt.Calls);
    }

    [Fact]
    public async Task Ad_hoc_profile_cannot_remember()
    {
        var (vm, _, prompt, _) = CreateWithPrompt(saveable: false);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(["ask:fake.host:False"], prompt.Calls);
    }

    [Fact]
    public async Task Without_a_prompt_the_failure_is_shown()
    {
        var session = new FakeEmulatorSession { ConnectException = CertFailure };
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard());
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(CertFailure.Message, vm.ErrorMessage);
    }

    [Fact]
    public async Task A_failure_after_connect_anyway_does_not_ask_again()
    {
        var (vm, session, prompt, _) = CreateWithPrompt(saveable: true);
        prompt.Decision = new CertificateDecision(ConnectAnyway: true, Remember: false);
        prompt.OnAsk = () => session.ConnectException = new ConnectionFailedException(["Connection failed:", "Connection refused"]);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Single(prompt.Calls);
        Assert.Equal("Connection failed: Connection refused", vm.ErrorMessage);
    }
}
