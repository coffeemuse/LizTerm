using LizTerm.Backend.B3270.Process;
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
        fake.RunResponder = _ => [];
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
        fake.RunResponder = _ => [];

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

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        var faulted = new TaskCompletionSource<BackendFault>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, f) => faulted.TrySetResult(f);

        fake1!.Exit(137);
        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, calls);
        Assert.True(fake2!.Started);
    }

    [Fact]
    public async Task Fault_after_a_failed_start_and_a_retry_is_still_reported()
    {
        var calls = 0;
        FakeB3270Process? fake2 = null;
        var session = new B3270Session(Profile, () =>
        {
            calls++;
            // The first process never says hello; the second is healthy.
            var fake = new FakeB3270Process { AutoInitialize = calls != 1 };
            if (calls == 2) fake2 = fake;
            return fake;
        }) { StartupTimeout = TimeSpan.FromMilliseconds(200) };
        var faults = new List<BackendFault>();
        var faulted = new TaskCompletionSource<BackendFault>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, f) => { faults.Add(f); faulted.TrySetResult(f); };

        await Assert.ThrowsAsync<BackendUnavailableException>(() => session.StartProcessAsync(CancellationToken.None));
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(faults);

        fake2!.Exit(137);
        var fault = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(137, fault.ExitCode);

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Engine_reports_the_location_before_start_and_the_version_after_hello()
    {
        var fake = new FakeB3270Process();
        var location = new B3270Location("/opt/x/b3270", EngineSource.Override);
        await using var session = new B3270Session(Profile, () => fake, location: location);
        Assert.Equal(new EngineInfo("b3270", null, "/opt/x/b3270", EngineSource.Override), session.Engine);
        await session.StartProcessAsync(CancellationToken.None);
        Assert.Equal("4.5.6 (fake b3270)", session.Engine.Version);
        Assert.Equal(EngineSource.Override, session.Engine.Source);
    }

    /// <summary>Regression: the locator now runs once in SessionFactory, so a binary that vanishes afterwards fails
    /// inside process.Start() rather than in the factory. Start must not leave the slot filled, or every later
    /// attempt short-circuits on a process that was never started.</summary>
    [Fact]
    public async Task A_failed_process_start_leaves_the_session_able_to_try_again()
    {
        var first = new FakeB3270Process { FaultStart = new BackendUnavailableException("Could not start /gone/b3270") };
        var second = new FakeB3270Process();
        var queue = new Queue<FakeB3270Process>([first, second]);
        await using var session = new B3270Session(Profile, () => queue.Dequeue());

        await Assert.ThrowsAsync<BackendUnavailableException>(() => session.StartProcessAsync(CancellationToken.None));
        await session.StartProcessAsync(TestContext.Current.CancellationToken);

        Assert.True(second.Started, "the retry must spawn a fresh process");
        Assert.Equal("4.5.6 (fake b3270)", session.Engine.Version);
    }

    /// <summary>Regression: OnProcessEnded clears the process slot from the reader thread after it has published
    /// Disconnected, so DisposeAsync must work from its own snapshot rather than re-reading the field.</summary>
    [Fact]
    public async Task Dispose_survives_the_engine_dying_part_way_through_the_quit_write()
    {
        var fake = new FakeB3270Process();
        var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(TestContext.Current.CancellationToken);

        // Faulted is raised on the reader thread from inside OnProcessEnded, after Disconnected is published and
        // before the process slot is cleared. Park it there so DisposeAsync passes its null check with the slot
        // still filled, then let it clear the slot while DisposeAsync is mid-write.
        var parked = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        session.Faulted += (_, _) => { parked.Set(); release.Wait(TimeSpan.FromSeconds(5)); };
        fake.Exit(1);
        Assert.True(parked.Wait(TimeSpan.FromSeconds(5)), "the reader thread never reached Faulted");

        fake.BeforeWrite = () =>
        {
            fake.BeforeWrite = null;
            fake.FaultWrite = true;
            release.Set();
            SpinWait.SpinUntil(() => !session.HasProcess, TimeSpan.FromSeconds(5));
        };

        await session.DisposeAsync();
        Assert.False(session.HasProcess);
    }

    /// <summary>The real engine exits on Quit rather than answering it. When the fake did not, every DisposeAsync
    /// waited out its full two-second timeout and then killed the process, which is both slow across the suite
    /// and a different shutdown path from the one the app actually takes.</summary>
    [Fact]
    public async Task Dispose_quits_the_engine_instead_of_killing_it_after_the_timeout()
    {
        var fake = new FakeB3270Process();
        var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(TestContext.Current.CancellationToken);

        await session.DisposeAsync();

        Assert.Contains(fake.InputLines, l => l.Contains("\"Quit\""));
        Assert.Equal(0, await fake.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    /// <summary>Regression: StartProcessAsync never consulted the shutdown flag, and DisposeAsync clears the
    /// process slot, so a connect arriving after disposal (a modal dialog's continuation outliving its window)
    /// spawned a fresh engine that nothing owned and nothing would ever dispose.</summary>
    [Fact]
    public async Task A_connect_after_dispose_is_refused_rather_than_spawning_another_engine()
    {
        var spawned = 0;
        var session = new B3270Session(Profile, () => { spawned++; return new FakeB3270Process(); });
        await session.StartProcessAsync(TestContext.Current.CancellationToken);
        await session.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, spawned);
    }

    /// <summary>Disposing a session that was never started has no process to tear down, so it must still mark
    /// itself disposed rather than leaving the next connect free to spawn one.</summary>
    [Fact]
    public async Task A_connect_after_disposing_an_unstarted_session_is_refused_too()
    {
        var spawned = 0;
        var session = new B3270Session(Profile, () => { spawned++; return new FakeB3270Process(); });
        await session.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, spawned);
    }
}
