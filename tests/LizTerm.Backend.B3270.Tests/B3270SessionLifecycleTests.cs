using LizTerm.Backend.B3270.Protocol;
using LizTerm.Backend.B3270.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests;

public class B3270SessionLifecycleTests
{
    private static readonly SessionProfile Profile = new() { Name = "t", Host = "host.example", Port = 3270, Model = 3, CodePage = "bracket" };

    [Fact]
    public void BuildArguments_uses_json_utf8_model_and_codepage()
    {
        Assert.Equal(["-json", "-utf8", "-model", "3279-3-E", "-codepage", "bracket"], B3270Session.BuildArguments(Profile));
    }

    [Fact]
    public async Task Start_spawns_with_arguments_and_waits_for_hello()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        Assert.True(fake.Started);
        Assert.Contains("-json", fake.StartedArguments);
        Assert.Equal(24, session.CurrentScreen.Rows);
    }

    [Fact]
    public async Task Start_rejects_old_versions()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        var session = new B3270Session(Profile, () => fake);
        fake.Emit("""{"initialize":[{"hello":{"version":"4.1.0","build":"old"}}]}""");
        var ex = await Assert.ThrowsAsync<BackendUnavailableException>(() => session.StartProcessAsync(CancellationToken.None));
        Assert.Contains("4.1.0", ex.Message);
    }

    [Fact]
    public async Task Start_times_out_without_hello()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        var session = new B3270Session(Profile, () => fake) { StartupTimeout = TimeSpan.FromMilliseconds(200) };
        await Assert.ThrowsAsync<BackendUnavailableException>(() => session.StartProcessAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Run_correlates_results_by_tag_even_out_of_order()
    {
        var fake = new FakeB3270Process { RunResponder = _ => [] };
        await using var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);

        var first = session.RunAsync(new B3270Action("Enter"));
        var second = session.RunAsync(new B3270Action("PF", "3"));
        var firstLine = await fake.WaitForInputAsync(l => l.Contains("\"Enter\""), TimeSpan.FromSeconds(1));
        var secondLine = await fake.WaitForInputAsync(l => l.Contains("\"PF\""), TimeSpan.FromSeconds(1));
        var firstTag = System.Text.RegularExpressions.Regex.Match(firstLine, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;
        var secondTag = System.Text.RegularExpressions.Regex.Match(secondLine, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEqual(firstTag, secondTag);

        fake.Emit($$$"""{"run-result":{"r-tag":"{{{secondTag}}}","success":true,"text":["pf"],"time":0}}""");
        fake.Emit($$$"""{"run-result":{"r-tag":"{{{firstTag}}}","success":true,"text":["enter"],"time":0}}""");
        Assert.Equal(["pf"], (await second).Text);
        Assert.Equal(["enter"], (await first).Text);
    }

    [Fact]
    public async Task Run_failure_throws_with_emulator_text()
    {
        var fake = new FakeB3270Process
        {
            RunResponder = line =>
            {
                var tag = System.Text.RegularExpressions.Regex.Match(line, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;
                return [$$$"""{"run-result":{"r-tag":"{{{tag}}}","success":false,"text":["Keyboard locked"],"text-err":[true],"time":0}}"""];
            },
        };
        await using var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        var ex = await Assert.ThrowsAsync<EmulatorActionException>(() => session.RunAsync(new B3270Action("Enter")));
        Assert.Equal("Keyboard locked", ex.Message);
        var raw = await session.RunRawAsync([new B3270Action("Enter")]);
        Assert.False(raw.Success);
    }

    [Fact]
    public async Task Unexpected_exit_faults_session_and_pending_runs()
    {
        var fake = new FakeB3270Process { RunResponder = _ => [] };
        var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        BackendFault? fault = null;
        var faulted = new TaskCompletionSource<BackendFault>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, f) => { fault = f; faulted.TrySetResult(f); };
        var pending = session.RunAsync(new B3270Action("Enter"));
        await fake.WaitForInputAsync(l => l.Contains("Enter"), TimeSpan.FromSeconds(1));

        fake.Exit(137);

        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(137, fault!.ExitCode);
        Assert.Contains("fake stderr line", fault.StderrTail);
        await Assert.ThrowsAsync<BackendUnavailableException>(() => pending);
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }

    [Fact]
    public async Task Dispose_sends_quit_and_does_not_fault()
    {
        var fake = new FakeB3270Process();
        var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        var faulted = false;
        session.Faulted += (_, _) => faulted = true;
        fake.RunResponder = line => { if (line.Contains("Quit")) fake.Exit(0); return []; };
        await session.DisposeAsync();
        Assert.Contains(fake.InputLines, l => l.Contains("\"Quit\""));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(faulted);
    }

    [Fact]
    public async Task Wire_log_records_both_directions()
    {
        var log = new StringWriter();
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Profile, () => fake, new WireLog(log));
        await session.StartProcessAsync(CancellationToken.None);
        await session.RunAsync(new B3270Action("Enter"));
        var text = log.ToString();
        Assert.Contains(" < {\"initialize\"", text);
        Assert.Contains(" > {\"run\":", text);
        Assert.Contains(" < {\"run-result\"", text);
    }

    [Fact]
    public async Task Unexpected_exit_with_faulting_wait_still_raises_Faulted_with_null_exit_code()
    {
        var fake = new FakeB3270Process { RunResponder = _ => [] };
        await using var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        fake.FaultWaitForExit = true;
        BackendFault? fault = null;
        var faulted = new TaskCompletionSource<BackendFault>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, f) => { fault = f; faulted.TrySetResult(f); };

        fake.Exit(1);

        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Null(fault!.ExitCode);
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
    }

    [Fact]
    public async Task Dispose_with_faulting_wait_does_not_throw_or_fault()
    {
        var fake = new FakeB3270Process { FaultWaitForExit = true };
        var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        var faulted = false;
        session.Faulted += (_, _) => faulted = true;
        fake.RunResponder = line => { if (line.Contains("Quit")) fake.Exit(0); return []; };

        await session.DisposeAsync();

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(faulted);
    }

    [Fact]
    public async Task Run_failure_to_write_removes_pending_entry()
    {
        var fake = new FakeB3270Process();
        var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);

        fake.FaultWrite = true;

        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(new B3270Action("Enter")));
        Assert.Equal(0, session.PendingCount);
    }

    [Fact]
    public async Task Connect_after_fault_spawns_a_new_process()
    {
        var calls = 0;
        FakeB3270Process? fake1 = null;
        FakeB3270Process? fake2 = null;
        var session = new B3270Session(Profile, () =>
        {
            calls++;
            var fake = new FakeB3270Process();
            if (calls == 1) fake1 = fake; else fake2 = fake;
            return fake;
        });

        await session.ConnectAsync(TestContext.Current.CancellationToken);
        var faulted = new TaskCompletionSource<BackendFault>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, f) => faulted.TrySetResult(f);

        fake1!.Exit(137);
        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        await session.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, calls);
        Assert.True(fake2!.Started);
    }
}
