using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class SessionViewModelTransferTests
{
    private static (SessionViewModel Vm, FakeEmulatorSession Session) Create()
    {
        var session = new FakeEmulatorSession();
        return (new SessionViewModel(session, action => action(), new FakeTextClipboard()), session);
    }

    [Fact]
    public async Task The_next_dialog_opens_as_the_last_one_was_left()
    {
        var (vm, _) = Create();
        var picker = new FakeFilePicker();
        var first = vm.CreateTransfer(picker);
        Assert.Null(vm.LastTransferRequest);
        Assert.True(first.IsSend);

        first.LocalPath = "/tmp/job.jcl";
        first.HostFile = "LIZTERM.JCL(JOB1)";
        first.IsBinary = true;
        first.RecordFormat = RecordFormat.Fixed;
        first.LreclText = "80";
        await first.StartCommand.ExecuteAsync(null);

        Assert.Equal("LIZTERM.JCL(JOB1)", vm.LastTransferRequest?.HostFile);
        var second = vm.CreateTransfer(picker);
        Assert.Equal("/tmp/job.jcl", second.LocalPath);
        Assert.Equal("LIZTERM.JCL(JOB1)", second.HostFile);
        Assert.False(second.IsText);
        Assert.Equal(RecordFormat.Fixed, second.RecordFormat);
        Assert.Equal("80", second.LreclText);
        Assert.True(second.IsForm);
    }

    [Fact]
    public async Task A_dialog_that_never_starts_leaves_the_memory_alone()
    {
        var (vm, _) = Create();
        var first = vm.CreateTransfer(new FakeFilePicker());
        first.LocalPath = "/tmp/a";
        first.HostFile = "A.B";
        await first.StartCommand.ExecuteAsync(null);

        var second = vm.CreateTransfer(new FakeFilePicker());
        second.HostFile = "CHANGED";
        var third = vm.CreateTransfer(new FakeFilePicker());
        Assert.Equal("A.B", third.HostFile);
    }

    [Fact]
    public async Task The_dialog_uses_the_given_picker_and_this_session()
    {
        var (vm, session) = Create();
        var picker = new FakeFilePicker { Result = "/tmp/x" };
        var transfer = vm.CreateTransfer(picker);
        await transfer.BrowseCommand.ExecuteAsync(null);
        Assert.Equal("/tmp/x", transfer.LocalPath);
        transfer.HostFile = "A.B";
        await transfer.StartCommand.ExecuteAsync(null);
        Assert.Contains("transfer:Send:A.B", session.Calls);
    }
}
