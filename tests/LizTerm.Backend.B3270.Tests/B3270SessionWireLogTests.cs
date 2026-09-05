using LizTerm.Backend.B3270.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests;

public class B3270SessionWireLogTests : IDisposable
{
    private static readonly SessionProfile Profile = new() { Name = "t", Host = "h", Port = 23 };
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-wl-" + Guid.NewGuid().ToString("N"));

    public B3270SessionWireLogTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string LogPath(string name = "wire.log") => Path.Combine(_dir, name);

    [Fact]
    public async Task Start_logs_both_directions_and_stop_closes_the_file()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        Assert.Null(session.WireLogPath);

        session.StartWireLog(LogPath());
        Assert.Equal(LogPath(), session.WireLogPath);
        await session.SendKeyAsync(TerminalKey.Enter);
        fake.Emit("""{"oia":{"field":"insert","value":"true"}}""");
        await WaitUntilAsync(() => session.KeyboardStatus.InsertMode, "insert");
        session.StopWireLog();
        Assert.Null(session.WireLogPath);

        var text = File.ReadAllText(LogPath());
        Assert.Contains(" > {\"run\"", text);
        Assert.Contains(" < {\"oia\"", text);
        session.StopWireLog(); // no-op when none is active
    }

    [Fact]
    public async Task Start_while_active_throws_and_keeps_the_first_log()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Profile, () => fake);
        await session.StartProcessAsync(CancellationToken.None);
        session.StartWireLog(LogPath("a.log"));
        var ex = Assert.Throws<InvalidOperationException>(() => session.StartWireLog(LogPath("b.log")));
        Assert.Equal("A wire log is already active.", ex.Message);
        Assert.Equal(LogPath("a.log"), session.WireLogPath);
        Assert.False(File.Exists(LogPath("b.log")));
    }

    [Fact]
    public async Task Start_on_an_unopenable_path_throws_IOException_and_stays_inactive()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Profile, () => fake);
        var missingDir = Path.Combine(_dir, "missing", "wire.log");
        Assert.Throws<IOException>(() => session.StartWireLog(missingDir));
        Assert.Null(session.WireLogPath);
    }

    [Fact]
    public async Task Environment_log_is_active_from_construction_and_survives_a_restart()
    {
        var fake = new FakeB3270Process();
        var log = new WireLog(LogPath());
        var session = new B3270Session(Profile, () => fake, log);
        Assert.Equal(LogPath(), session.WireLogPath);
        await session.StartProcessAsync(CancellationToken.None);
        fake.Exit(1);
        await WaitUntilAsync(() => session.ConnectionState == ConnectionState.Disconnected, "fault");
        Assert.Equal(LogPath(), session.WireLogPath);
        await session.DisposeAsync();
        Assert.Null(session.WireLogPath);
    }

    [Fact]
    public async Task Open_error_warning_is_raised_once()
    {
        var first = new FakeB3270Process();
        var second = new FakeB3270Process();
        var processes = new Queue<FakeB3270Process>([first, second]);
        var session = new B3270Session(Profile, () => processes.Dequeue(), wireLogError: "boom");
        var messages = new List<string>();
        session.HostMessage += (_, m) => messages.Add(m);

        await session.StartProcessAsync(CancellationToken.None);
        first.Exit(1);
        await WaitUntilAsync(() => session.ConnectionState == ConnectionState.Disconnected, "first exit");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await session.StartProcessAsync(CancellationToken.None);

        Assert.Single(messages, m => m == "Wire log disabled: boom");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
