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
}
