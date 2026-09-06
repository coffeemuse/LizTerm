using System.Text.RegularExpressions;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Tests.ViewModels;

public class SessionViewModelWireLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-logs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private (SessionViewModel Vm, FakeEmulatorSession Session, FakeFolderOpener Opener) Create()
    {
        var session = new FakeEmulatorSession();
        var opener = new FakeFolderOpener();
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard(), folderOpener: opener) { WireLogDirectory = _dir };
        return (vm, session, opener);
    }

    [Fact]
    public void File_name_comes_from_the_profile_and_the_clock()
    {
        var name = SessionViewModel.WireLogFileName("MVS/CE: test", new DateTime(2026, 9, 5, 14, 3, 9));
        Assert.Equal("wire-MVS_CE__test-20260905-140309.log", name);
    }

    [Fact]
    public void Toggling_on_starts_a_log_in_the_directory_and_lights_the_indicator()
    {
        var (vm, session, _) = Create();
        Assert.False(vm.IsWireLogging);
        Assert.Equal("", vm.WireLogText);

        vm.IsWireLogging = true;
        Assert.True(Directory.Exists(_dir));
        var call = Assert.Single(session.Calls);
        Assert.Matches(new Regex("^wirelog:start:" + Regex.Escape(Path.Combine(_dir, "wire-Fake-")) + @"\d{8}-\d{6}\.log$"), call);
        Assert.Equal("● wire log", vm.WireLogText);
        Assert.Null(vm.ErrorMessage);

        vm.IsWireLogging = false;
        Assert.Equal("wirelog:stop", session.Calls[^1]);
        Assert.Equal("", vm.WireLogText);
        Assert.Null(session.WireLogPath);
    }

    [Fact]
    public void An_environment_log_shows_as_on_from_the_start()
    {
        var session = new FakeEmulatorSession { WireLogPath = "/tmp/env.log" };
        var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard());
        Assert.True(vm.IsWireLogging);
        Assert.Equal("● wire log", vm.WireLogText);
        vm.IsWireLogging = false;
        Assert.Equal(["wirelog:stop"], session.Calls);
    }

    [Fact]
    public void An_unopenable_log_reports_and_stays_off()
    {
        var (vm, session, _) = Create();
        session.WireLogException = new IOException("disk full");
        vm.IsWireLogging = true;
        Assert.False(vm.IsWireLogging);
        Assert.Equal("Could not open the wire log: disk full", vm.ErrorMessage);
        Assert.Equal("", vm.WireLogText);
    }

    [Fact]
    public async Task Show_wire_logs_creates_and_opens_the_directory()
    {
        var (vm, _, opener) = Create();
        await vm.ShowWireLogsCommand.ExecuteAsync(null);
        Assert.True(Directory.Exists(_dir));
        Assert.Equal([_dir], opener.Opened);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Show_wire_logs_falls_back_to_naming_the_path()
    {
        var (vm, _, opener) = Create();
        opener.Result = false;
        await vm.ShowWireLogsCommand.ExecuteAsync(null);
        Assert.Equal($"Could not open the logs folder. Wire logs are in {_dir}.", vm.ErrorMessage);

        opener.Exception = new InvalidOperationException("no launcher");
        await vm.ShowWireLogsCommand.ExecuteAsync(null);
        Assert.Equal($"Could not open the logs folder. Wire logs are in {_dir}.", vm.ErrorMessage);
    }

    [Fact]
    public void A_second_log_in_the_same_second_gets_a_numbered_name()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lizterm-wirelog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(Path.Combine(dir, "wire-a-1.log"), SessionViewModel.UniquePath(dir, "wire-a-1.log"));
            File.WriteAllText(Path.Combine(dir, "wire-a-1.log"), "");
            Assert.Equal(Path.Combine(dir, "wire-a-1-2.log"), SessionViewModel.UniquePath(dir, "wire-a-1.log"));
            File.WriteAllText(Path.Combine(dir, "wire-a-1-2.log"), "");
            Assert.Equal(Path.Combine(dir, "wire-a-1-3.log"), SessionViewModel.UniquePath(dir, "wire-a-1.log"));

            // Through the toggle: whichever second the start lands in, a file with that name already exists.
            var session = new FakeEmulatorSession();
            var vm = new SessionViewModel(session, a => a(), new FakeTextClipboard()) { WireLogDirectory = dir };
            var now = DateTime.Now;
            // A stall between the capture above and the toggle below must not make this test flaky: seed every
            // second the real DateTime.Now inside the toggle could plausibly land on, not just the one after.
            for (var s = -2; s <= 3; s++)
                File.WriteAllText(Path.Combine(dir, SessionViewModel.WireLogFileName(session.Profile.Name, now.AddSeconds(s))), "");
            vm.IsWireLogging = true;
            Assert.EndsWith("-2.log", session.WireLogPath);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
