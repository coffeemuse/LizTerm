// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

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
        Assert.Equal(["-json", "-utf8", "-model", "3279-3-E", "-codepage", "bracket", "-set", "nopSeconds=60"], B3270Session.BuildArguments(Profile));
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

    /// <summary>Review finding 2 on plan 3d task 8's race fix: completing <c>_hello</c> was correctly moved past
    /// the whole initialize block's foreach (so a later item like tls-hello has already updated session state
    /// before StartProcessAsync's awaiter can resume -- see the InitializeIndication case in Handle), but without
    /// a finally, a later item throwing -- a malformed indication, or an external ScreenUpdated/StatusChanged
    /// subscriber throwing, which the App's dispatch delegate can do during shutdown -- skipped the
    /// TrySetResult entirely. The reader thread survives (ReadLoop's inner try/catch swallows the exception into
    /// a HostMessage), but _hello never completes, so a healthy engine that answered hello fine gets blamed for
    /// a spurious "did not answer within N seconds" timeout caused entirely by our own handler. StartupTimeout is
    /// short so this test fails fast, not after a real 10 s wait, when the finally is missing.</summary>
    [Fact]
    public async Task A_later_initialize_item_that_makes_a_subscriber_throw_still_completes_startup()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        var session = new B3270Session(Profile, () => fake) { StartupTimeout = TimeSpan.FromMilliseconds(500) };
        session.ScreenUpdated += (_, _) => throw new InvalidOperationException("subscriber boom");
        fake.Emit("""{"initialize":[{"hello":{"version":"4.5.6","build":"fake b3270"}},{"screen-mode":{"model":2,"rows":24,"columns":80,"color":true,"oversize":false,"extended":true}}]}""");

        await session.StartProcessAsync(CancellationToken.None);

        Assert.Equal("4.5.6 (fake b3270)", session.Engine.Version);
    }

    [Fact]
    public async Task Run_correlates_results_by_tag_even_out_of_order()
    {
        var fake = new FakeB3270Process { RunResponder = _ => [] };
        await using var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);

        var first = session.RunAsync(new B3270Action("Enter"));
        var second = session.RunAsync(new B3270Action("PF", "3"));
        var firstLine = await fake.WaitForInputAsync(l => l.Contains("\"Enter\""));
        var secondLine = await fake.WaitForInputAsync(l => l.Contains("\"PF\""));
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
        var raw = await session.RunRawAsync([new B3270Action("Enter")], cancellationToken: TestContext.Current.CancellationToken);
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
        await fake.WaitForInputAsync(l => l.Contains("Enter"));

        fake.Exit(137);

        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
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

        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
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
        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, calls);
        Assert.True(fake2!.Started);
    }

    /// <summary>Review finding 4 on plan 3d task 8: the _tlsOptions doc comment claims "on this process", but
    /// nothing enforced that until TearDown/OnProcessEnded started clearing it alongside _process/_hello. A first
    /// engine reports a Schannel-shaped tls-hello (no caFile, so CanPinCertificates reads false); once it dies and
    /// a fresh process takes over -- one that has not sent its own tls-hello yet -- the stale option set must not
    /// keep gating the replacement. Without the clear, CanPinCertificates would wrongly stay false forever, on a
    /// process that never said so.</summary>
    [Fact]
    public async Task A_dead_processs_tls_options_do_not_survive_into_its_replacement()
    {
        var calls = 0;
        FakeB3270Process? fake1 = null;
        var session = new B3270Session(Profile, () =>
        {
            calls++;
            if (calls != 1) return new FakeB3270Process();
            var fake = new FakeB3270Process { AutoInitialize = false };
            fake.Emit("""{"initialize":[{"hello":{"version":"4.5.6","build":"win"}},{"tls-hello":{"supported":true,"provider":"Schannel","options":["clientCert","tlsMinProtocol","tlsMaxProtocol"]}}]}""");
            fake1 = fake;
            return fake;
        });

        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(session.CanPinCertificates);

        var faulted = new TaskCompletionSource<BackendFault>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, f) => faulted.TrySetResult(f);
        fake1!.Exit(137);
        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        // OnProcessEnded raises Faulted before it clears _process (reader thread order), and the TCS above
        // completes its awaiter on a thread-pool continuation rather than blocking that thread, so without this
        // wait the assertion below can race a StartProcessAsync that still sees the dead process's slot filled
        // and short-circuits on it -- the same HasProcess wait B3270SessionLifecycleTests already uses elsewhere
        // for this exact ordering.
        await Wait.UntilAsync(() => !session.HasProcess, "the dead process's slot to clear");

        // The replacement's own FakeB3270Process default AutoInitialize sends MinimalInitialize, which carries no
        // tls-hello at all -- exactly like a real engine's build before this indication existed.
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(session.CanPinCertificates);
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
            // Kept short on purpose: the first start can only end by expiring this, so every millisecond here is
            // a deterministic sleep. The retry needs no headroom from it — AutoInitialize queues hello inside
            // Start(), before StartProcessAsync begins waiting, so 200 ms only has to cover a dequeue.
        }) { StartupTimeout = TimeSpan.FromMilliseconds(200) };
        var faults = new List<BackendFault>();
        var faulted = new TaskCompletionSource<BackendFault>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, f) => { faults.Add(f); faulted.TrySetResult(f); };

        await Assert.ThrowsAsync<BackendUnavailableException>(() => session.StartProcessAsync(CancellationToken.None));
        await session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(faults);

        fake2!.Exit(137);
        var fault = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
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
        Assert.True(parked.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "the reader thread never reached Faulted");

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
        Assert.Equal(0, await fake.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
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

    /// <summary>Both are omitted when the profile asks for what b3270 already does, because argv is evaluated once
    /// per process and there is nothing to override (oversize unset, nopSeconds 0). Note the values here are
    /// <em>b3270's</em> defaults, not <c>SessionProfile</c>'s: LizTerm defaults KeepAliveSeconds to 60, which is
    /// exactly the case that DOES emit <c>-set nopSeconds=60</c> (asserted below), so this test has to set 0
    /// explicitly. That is deliberately NOT the "send every toggle explicitly every time" rule the TLS options
    /// follow: that rule exists because a connect can inherit the previous connect's settings within one engine
    /// process, and argv cannot.</summary>
    [Fact]
    public void BuildArguments_omits_oversize_and_keep_alive_when_they_match_b3270s_own_defaults()
    {
        var args = B3270Session.BuildArguments(new SessionProfile { Name = "p", Host = "h", KeepAliveSeconds = 0 });
        Assert.DoesNotContain("-oversize", args);
        Assert.DoesNotContain("-set", args);
    }

    [Fact]
    public void BuildArguments_passes_the_oversize_geometry_verbatim()
    {
        var args = B3270Session.BuildArguments(new SessionProfile { Name = "p", Host = "h", Oversize = "132x43", KeepAliveSeconds = 0 });
        var i = args.ToList().IndexOf("-oversize");
        Assert.True(i >= 0, "-oversize is missing");
        Assert.Equal("132x43", args[i + 1]);
    }

    /// <summary>"-set name=value" is the only form: "-nopSeconds 60" is rejected by the engine outright as
    /// "Unknown or incomplete option" (measured against 4.5ga6, spec 9.1).</summary>
    [Fact]
    public void BuildArguments_passes_the_keep_alive_as_a_set_assignment()
    {
        var args = B3270Session.BuildArguments(new SessionProfile { Name = "p", Host = "h", KeepAliveSeconds = 60 });
        var i = args.ToList().IndexOf("-set");
        Assert.True(i >= 0, "-set is missing");
        Assert.Equal("nopSeconds=60", args[i + 1]);
    }

    /// <summary>The profile editor is not the only way an oversize gets here: a profile file is user-editable and
    /// nothing on the way in reads this field — ProfileStore.Read sanitises a broken pin and stops there. A
    /// geometry b3270 would refuse is named before a single action is written, rather than becoming a popup that
    /// reaches the app as an unexplained HostMessage with nothing tying it to what the user typed (spec 4.2).</summary>
    [Fact]
    public void BuildArguments_refuses_an_oversize_b3270_would_reject()
    {
        var profile = new SessionProfile { Name = "p", Host = "h", Oversize = "200x200" };
        var refusal = Assert.Throws<ConnectionFailedException>(() => B3270Session.BuildArguments(profile));
        Assert.StartsWith("200 columns by 200 rows is 40,000 cells", refusal.Message);
    }

    /// <summary>And the floor it measures against is the profile's own model's, so an oversize saved under model 2
    /// and then hand-edited to model 5 is caught as well as an outright nonsense one.</summary>
    [Fact]
    public void BuildArguments_measures_the_oversize_against_the_profiles_model()
    {
        var under5 = new SessionProfile { Name = "p", Host = "h", Model = 5, Oversize = "100x30" };
        Assert.Throws<ConnectionFailedException>(() => B3270Session.BuildArguments(under5));
        Assert.Contains("100x30", B3270Session.BuildArguments(under5 with { Model = 2 }));
    }

    /// <summary>b3270's own spelling of "no oversize", which a hand-edited profile can carry: valid, and nothing
    /// to put on the command line.</summary>
    [Fact]
    public void BuildArguments_sends_nothing_for_a_zero_by_zero_oversize()
    {
        var args = B3270Session.BuildArguments(new SessionProfile { Name = "p", Host = "h", Oversize = "0x0" });
        Assert.DoesNotContain("-oversize", args);
    }

    [Fact]
    public void BuildArguments_keeps_the_six_arguments_it_always_had()
    {
        var args = B3270Session.BuildArguments(new SessionProfile { Name = "p", Host = "h", Model = 3 });
        Assert.Equal(["-json", "-utf8", "-model", "3279-3-E", "-codepage", "cp037"], args.Take(6));
    }
}
