// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using System.Text.RegularExpressions;
using LizTerm.Backend.B3270.Process;
using LizTerm.Backend.B3270.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests;

/// <summary>The transfer state machine on the fake process. The fake answers every run at once except a Transfer
/// (not Cancel), which stays pending until the test emits its run-result, exactly as b3270 behaves.</summary>
public class B3270SessionTransferTests
{
    private static readonly SessionProfile Profile = new() { Name = "t", Host = "h", Port = 23 };
    private static readonly FileTransferRequest Request = new() { Direction = TransferDirection.Send, LocalPath = "/nonexistent/a.txt", HostFile = "A.B" };
    private static readonly TimeSpan WaitTime = TimeSpan.FromSeconds(5);

    private static async Task<(B3270Session Session, FakeB3270Process Fake)> StartAsync(B3270Location? location = null)
    {
        var fake = new FakeB3270Process();
        fake.RunResponder = line => IsTransferStart(line) ? Array.Empty<string>() : new[] { AutoResult(line) };
        var session = new B3270Session(Profile, () => fake, location: location);
        await session.StartProcessAsync(CancellationToken.None);
        return (session, fake);
    }

    private static bool IsTransferStart(string line) => line.Contains("\"action\":\"Transfer\"") && !line.Contains("\"Cancel\"");

    private static string Tag(string inputLine) => Regex.Match(inputLine, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;

    private static string AutoResult(string inputLine) => $$$"""{"run-result":{"r-tag":"{{{Tag(inputLine)}}}","success":true,"time":0}}""";

    private static Task<string> TransferLineAsync(FakeB3270Process fake) => fake.WaitForInputAsync(IsTransferStart, WaitTime);

    private static string SuccessResult(string transferLine, string text) =>
        $$$"""{"run-result":{"r-tag":"{{{Tag(transferLine)}}}","success":true,"text":["{{{text}}}"],"time":0.5}}""";

    private static string FailureResult(string transferLine, string text) =>
        $$$"""{"run-result":{"r-tag":"{{{Tag(transferLine)}}}","success":false,"text":["{{{text}}}"],"time":0.5}}""";

    private sealed class Reports : IProgress<long>
    {
        private readonly List<long> _values = [];
        public long[] Values { get { lock (_values) return _values.ToArray(); } }
        public void Report(long value) { lock (_values) _values.Add(value); }
    }

    [Fact]
    public async Task Sends_the_mapped_action_reports_progress_and_returns_the_text()
    {
        var (session, fake) = await StartAsync();
        var reports = new Reports();
        var transfer = session.TransferAsync(Request, reports, TestContext.Current.CancellationToken);
        var line = await TransferLineAsync(fake);
        Assert.Contains("\"action\":\"Transfer\",\"args\":[\"direction=send\",\"hostfile=A.B\",\"localfile=/nonexistent/a.txt\",\"host=tso\",\"mode=ascii\",\"cr=remove\",\"remap=yes\"]", line);
        Assert.True(session.IsTransferInProgress);

        fake.Emit("""{"ft":{"state":"awaiting","cause":"ui"}}""");
        fake.Emit("""{"ft":{"state":"running","bytes":0,"cause":"ui"}}""");
        fake.Emit("""{"ft":{"state":"running","bytes":2048,"cause":"ui"}}""");
        fake.Emit("""{"ft":{"state":"complete","success":true,"text":"Transfer complete, 2048 bytes transferred","cause":"ui"}}""");
        fake.Emit($$$"""{"run-result":{"r-tag":"{{{Tag(line)}}}","success":true,"text":["Transfer complete, 2048 bytes transferred","2.0 Kbytes/sec in DFT mode"],"time":1.5}}""");

        var result = await transfer.WaitAsync(WaitTime, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        Assert.Equal("Transfer complete, 2048 bytes transferred\n2.0 Kbytes/sec in DFT mode", result.Message);
        Assert.Equal([0L, 2048L], reports.Values);
        Assert.False(session.IsTransferInProgress);
    }

    [Fact]
    public async Task A_failed_run_result_is_a_failed_result_with_the_host_text()
    {
        var (session, fake) = await StartAsync();
        var transfer = session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken);
        var line = await TransferLineAsync(fake);
        fake.Emit("""{"ft":{"state":"complete","success":false,"text":"TRANS17 Miscellaneous I/O error","cause":"ui"}}""");
        fake.Emit(FailureResult(line, "TRANS17 Miscellaneous I/O error"));

        var result = await transfer.WaitAsync(WaitTime, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Equal("TRANS17 Miscellaneous I/O error", result.Message);
        Assert.False(session.IsTransferInProgress);
    }

    [Fact]
    public async Task Cancel_sends_the_cancel_action_and_the_failed_result_keeps_the_engine_text()
    {
        var (session, fake) = await StartAsync();
        using var cts = new CancellationTokenSource();
        var transfer = session.TransferAsync(Request, cancellationToken: cts.Token);
        var line = await TransferLineAsync(fake);
        fake.Emit("""{"ft":{"state":"running","bytes":512,"cause":"ui"}}""");

        cts.Cancel();
        var cancelLine = await fake.WaitForInputAsync(l => l.Contains("\"action\":\"Transfer\",\"args\":[\"Cancel\"]"), WaitTime);
        Assert.NotEqual(Tag(line), Tag(cancelLine));
        fake.Emit("""{"ft":{"state":"aborting","cause":"ui"}}""");
        fake.Emit("""{"ft":{"state":"complete","success":false,"text":"Transfer canceled by user","cause":"ui"}}""");
        fake.Emit(FailureResult(line, "Transfer canceled by user"));

        var result = await transfer.WaitAsync(WaitTime, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Equal("Transfer canceled by user", result.Message);
        Assert.False(session.IsTransferInProgress);
    }

    [Fact]
    public async Task A_host_failure_that_lands_after_a_cancel_keeps_the_host_text()
    {
        var (session, fake) = await StartAsync();
        using var cts = new CancellationTokenSource();
        var transfer = session.TransferAsync(Request, cancellationToken: cts.Token);
        var line = await TransferLineAsync(fake);

        cts.Cancel();
        await fake.WaitForInputAsync(l => l.Contains("\"Cancel\""), WaitTime);
        fake.Emit(FailureResult(line, "TRANS17 Miscellaneous I/O error"));

        var result = await transfer.WaitAsync(WaitTime, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Equal("TRANS17 Miscellaneous I/O error", result.Message);
    }

    [Fact]
    public async Task The_slot_is_held_until_the_cancel_has_been_answered()
    {
        // Hold both the Transfer run and the Cancel run open, so the test controls their order.
        var fake = new FakeB3270Process();
        fake.RunResponder = line => line.Contains("\"action\":\"Transfer\"") ? Array.Empty<string>() : new[] { AutoResult(line) };
        var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var transfer = session.TransferAsync(Request, cancellationToken: cts.Token);
        var line = await TransferLineAsync(fake);

        cts.Cancel();
        var cancelLine = await fake.WaitForInputAsync(l => l.Contains("\"Cancel\""), WaitTime);
        fake.Emit(SuccessResult(line, "Transfer complete, 10 bytes transferred"));

        // The transfer's own run has succeeded, but its cancel is still on the wire unanswered: the slot stays
        // taken, so a transfer started now cannot be the one that late cancel lands on.
        Assert.NotSame(transfer, await Task.WhenAny(transfer, Task.Delay(200, TestContext.Current.CancellationToken)));
        Assert.True(session.IsTransferInProgress);

        fake.Emit(AutoResult(cancelLine));
        var result = await transfer.WaitAsync(WaitTime, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        Assert.False(session.IsTransferInProgress);
    }

    [Fact]
    public async Task Transfer_after_engine_death_reports_the_fault_not_a_missing_start()
    {
        var (session, fake) = await StartAsync();
        var faulted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, _) => faulted.TrySetResult();
        fake.Exit(1);
        await faulted.Task.WaitAsync(WaitTime, TestContext.Current.CancellationToken);

        // Bounded like every other await here: this one hung CI for the 5-minute blame timeout on 2026-09-13
        // (see the lifecycle test A_run_started_from_inside_the_fault_handler_fails_with_the_fault_instead_of_hanging),
        // and a regression should fail in seconds with a TimeoutException rather than wedge the run.
        var ex = await Assert.ThrowsAsync<BackendUnavailableException>(
            () => session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken).WaitAsync(WaitTime, TestContext.Current.CancellationToken));
        Assert.Contains("exited unexpectedly", ex.Message);
        Assert.False(session.IsTransferInProgress);
    }

    [Fact]
    public async Task A_success_that_beats_the_cancel_is_still_a_success()
    {
        var (session, fake) = await StartAsync();
        using var cts = new CancellationTokenSource();
        var transfer = session.TransferAsync(Request, cancellationToken: cts.Token);
        var line = await TransferLineAsync(fake);
        cts.Cancel();
        await fake.WaitForInputAsync(l => l.Contains("\"Cancel\""), WaitTime);
        fake.Emit(SuccessResult(line, "Transfer complete, 10 bytes transferred"));

        var result = await transfer.WaitAsync(WaitTime, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_second_transfer_while_one_is_pending_throws_and_sends_nothing()
    {
        var (session, fake) = await StartAsync();
        var first = session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken);
        var line = await TransferLineAsync(fake);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("A file transfer is already in progress.", ex.Message);
        Assert.Single(fake.InputLines, IsTransferStart);

        fake.Emit(SuccessResult(line, "Transfer complete, 1 bytes transferred"));
        Assert.True((await first.WaitAsync(WaitTime, TestContext.Current.CancellationToken)).Succeeded);
    }

    [Fact]
    public async Task Engine_exit_mid_transfer_throws_and_frees_the_slot()
    {
        var (session, fake) = await StartAsync();
        var transfer = session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken);
        await TransferLineAsync(fake);

        fake.Exit(1);

        await Assert.ThrowsAsync<BackendUnavailableException>(() => transfer.WaitAsync(WaitTime, TestContext.Current.CancellationToken));
        Assert.False(session.IsTransferInProgress);
    }

    [Fact]
    public async Task Ft_indications_with_no_transfer_in_flight_are_ignored()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"ft":{"state":"running","bytes":99,"cause":"ui"}}""");
        // An oia line behind it proves the reader thread has consumed the stray ft line before the transfer starts.
        fake.Emit("""{"oia":{"field":"insert","value":"true"}}""");
        await Wait.UntilAsync(() => session.KeyboardStatus.InsertMode, "the oia line", WaitTime);

        var transfer = session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken);
        var line = await TransferLineAsync(fake);
        fake.Emit(SuccessResult(line, "Transfer complete, 0 bytes transferred"));

        var result = await transfer.WaitAsync(WaitTime, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_cancelled_token_throws_before_anything_is_sent()
    {
        var (session, fake) = await StartAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.TransferAsync(Request, cancellationToken: cts.Token));
        Assert.DoesNotContain(fake.InputLines, l => l.Contains("Transfer"));
        Assert.False(session.IsTransferInProgress);
    }

    [Fact]
    public async Task Transfer_before_start_throws()
    {
        var session = new B3270Session(Profile, () => new FakeB3270Process());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("The session has not been started.", ex.Message);
    }

    /// <summary>A stand-in engine binary on disk: arbitrary bytes, with or without the patch marker among them.</summary>
    private static B3270Location EngineFile(bool patched)
    {
        var path = Path.Combine(Path.GetTempPath(), "lizterm-engine-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, [0x7f, 0x45, 0x4c, 0x46, .. Encoding.ASCII.GetBytes(patched ? "OtherOptions\0CommandPrefix\0" : "OtherOptions\0")]);
        return new B3270Location(path, EngineSource.Override);
    }

    [Fact]
    public async Task An_ispf_transfer_on_an_engine_without_the_patch_fails_at_once_and_sends_nothing()
    {
        var engine = EngineFile(patched: false);
        try
        {
            var (session, fake) = await StartAsync(engine);
            var result = await session.TransferAsync(Request with { HostType = TransferHostType.Ispf }, cancellationToken: TestContext.Current.CancellationToken)
                .WaitAsync(WaitTime, TestContext.Current.CancellationToken);
            Assert.False(result.Succeeded);
            Assert.Equal(EnginePatches.MissingCommandPrefixMessage, result.Message);
            Assert.DoesNotContain(fake.InputLines, IsTransferStart);
            Assert.False(session.IsTransferInProgress);

            // Only ISPF needs the patch: a TSO transfer on the same engine goes out as usual.
            var tso = session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken);
            var line = await TransferLineAsync(fake);
            fake.Emit(SuccessResult(line, "Transfer complete, 1 bytes transferred"));
            Assert.True((await tso.WaitAsync(WaitTime, TestContext.Current.CancellationToken)).Succeeded);
        }
        finally
        {
            File.Delete(engine.Path);
        }
    }

    [Fact]
    public async Task An_ispf_transfer_on_a_patched_engine_sends_the_command_prefix()
    {
        var engine = EngineFile(patched: true);
        try
        {
            await AssertIspfTransferIsSentAsync(engine);
        }
        finally
        {
            File.Delete(engine.Path);
        }
    }

    /// <summary>No engine file to read (a session built without a location) is assumed patched, so the transfer is
    /// attempted as it was before the check existed.</summary>
    [Fact]
    public Task An_ispf_transfer_with_no_known_engine_file_is_attempted() => AssertIspfTransferIsSentAsync(null);

    private static async Task AssertIspfTransferIsSentAsync(B3270Location? engine)
    {
        var (session, fake) = await StartAsync(engine);
        var transfer = session.TransferAsync(Request with { HostType = TransferHostType.Ispf }, cancellationToken: TestContext.Current.CancellationToken);
        var line = await TransferLineAsync(fake);
        Assert.Contains("\"host=tso\",\"commandprefix=TSO\",\"mode=ascii\"", line);
        fake.Emit(SuccessResult(line, "Transfer complete, 1 bytes transferred"));
        Assert.True((await transfer.WaitAsync(WaitTime, TestContext.Current.CancellationToken)).Succeeded);
    }
}
