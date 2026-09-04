using System.Text.RegularExpressions;
using LizTerm.Backend.B3270.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests;

/// <summary>The transfer state machine on the fake process. The fake answers every run at once except a Transfer
/// (not Cancel), which stays pending until the test emits its run-result, exactly as b3270 behaves.</summary>
public class B3270SessionTransferTests
{
    private static readonly SessionProfile Profile = new() { Name = "t", Host = "h", Port = 23 };
    private static readonly FileTransferRequest Request = new() { Direction = TransferDirection.Send, LocalPath = "/nonexistent/a.txt", HostFile = "A.B" };
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(2);

    private static async Task<(B3270Session Session, FakeB3270Process Fake)> StartAsync()
    {
        var fake = new FakeB3270Process();
        fake.RunResponder = line => IsTransferStart(line) ? Array.Empty<string>() : new[] { AutoResult(line) };
        var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        return (session, fake);
    }

    private static bool IsTransferStart(string line) => line.Contains("\"action\":\"Transfer\"") && !line.Contains("\"Cancel\"");

    private static string Tag(string inputLine) => Regex.Match(inputLine, "\"r-tag\":\"([^\"]+)\"").Groups[1].Value;

    private static string AutoResult(string inputLine) => $$$"""{"run-result":{"r-tag":"{{{Tag(inputLine)}}}","success":true,"time":0}}""";

    private static Task<string> TransferLineAsync(FakeB3270Process fake) => fake.WaitForInputAsync(IsTransferStart, Wait);

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

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Sends_the_mapped_action_reports_progress_and_returns_text_and_count()
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

        var result = await transfer.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        Assert.Equal("Transfer complete, 2048 bytes transferred\n2.0 Kbytes/sec in DFT mode", result.Message);
        Assert.Equal(2048, result.Bytes);
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

        var result = await transfer.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.False(result.Succeeded);
        Assert.Equal("TRANS17 Miscellaneous I/O error", result.Message);
        Assert.Equal(0, result.Bytes);
        Assert.False(session.IsTransferInProgress);
    }

    [Fact]
    public async Task Cancel_sends_the_cancel_action_and_the_failed_result_becomes_cancellation()
    {
        var (session, fake) = await StartAsync();
        using var cts = new CancellationTokenSource();
        var transfer = session.TransferAsync(Request, cancellationToken: cts.Token);
        var line = await TransferLineAsync(fake);
        fake.Emit("""{"ft":{"state":"running","bytes":512,"cause":"ui"}}""");

        cts.Cancel();
        var cancelLine = await fake.WaitForInputAsync(l => l.Contains("\"action\":\"Transfer\",\"args\":[\"Cancel\"]"), Wait);
        Assert.NotEqual(Tag(line), Tag(cancelLine));
        fake.Emit("""{"ft":{"state":"aborting","cause":"ui"}}""");
        fake.Emit("""{"ft":{"state":"complete","success":false,"text":"Transfer canceled by user","cause":"ui"}}""");
        fake.Emit(FailureResult(line, "Transfer canceled by user"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.WaitAsync(Wait, TestContext.Current.CancellationToken));
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
        await fake.WaitForInputAsync(l => l.Contains("\"Cancel\""), Wait);
        fake.Emit(SuccessResult(line, "Transfer complete, 10 bytes transferred"));

        var result = await transfer.WaitAsync(Wait, TestContext.Current.CancellationToken);
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
        Assert.True((await first.WaitAsync(Wait, TestContext.Current.CancellationToken)).Succeeded);
    }

    [Fact]
    public async Task Engine_exit_mid_transfer_throws_and_frees_the_slot()
    {
        var (session, fake) = await StartAsync();
        var transfer = session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken);
        await TransferLineAsync(fake);

        fake.Exit(1);

        await Assert.ThrowsAsync<BackendUnavailableException>(() => transfer.WaitAsync(Wait, TestContext.Current.CancellationToken));
        Assert.False(session.IsTransferInProgress);
    }

    [Fact]
    public async Task Ft_indications_with_no_transfer_in_flight_are_ignored()
    {
        var (session, fake) = await StartAsync();
        fake.Emit("""{"ft":{"state":"running","bytes":99,"cause":"ui"}}""");
        // An oia line behind it proves the reader thread has consumed the stray ft line before the transfer starts.
        fake.Emit("""{"oia":{"field":"insert","value":"true"}}""");
        await WaitUntilAsync(() => session.KeyboardStatus.InsertMode, "the oia line");

        var transfer = session.TransferAsync(Request, cancellationToken: TestContext.Current.CancellationToken);
        var line = await TransferLineAsync(fake);
        fake.Emit(SuccessResult(line, "Transfer complete, 0 bytes transferred"));

        var result = await transfer.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Equal(0, result.Bytes);
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
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => session.TransferAsync(Request));
        Assert.Equal("The session has not been started.", ex.Message);
    }
}
