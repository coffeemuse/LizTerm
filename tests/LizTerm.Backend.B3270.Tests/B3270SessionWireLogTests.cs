// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

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
        await Wait.UntilAsync(() => session.KeyboardStatus.InsertMode, "insert");
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
    public async Task Start_on_an_empty_path_throws_IOException()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Profile, () => fake);
        Assert.Throws<IOException>(() => session.StartWireLog(""));
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
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.Disconnected, "fault");
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
        await Wait.UntilAsync(() => session.ConnectionState == ConnectionState.Disconnected, "first exit");
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await session.StartProcessAsync(CancellationToken.None);

        Assert.Single(messages, m => m == "Wire log disabled: boom");
    }

    /// <summary>Reads the log while the session still holds it open, so a test can see what had been written at
    /// a given moment.</summary>
    private static string ReadWhileOpen(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Regression: the outbound line was logged after the flush that sends it, and the reader thread
    /// logs inbound lines without the write lock, so b3270's answer could be timestamped and written ahead of
    /// the run that provoked it — the exact ordering a reader uses to attribute a failure.</summary>
    [Fact]
    public async Task An_outbound_line_reaches_the_log_before_it_reaches_the_engine()
    {
        var fake = new FakeB3270Process();
        await using var session = new B3270Session(Profile, () => fake, new WireLog(LogPath()));
        await session.StartProcessAsync(TestContext.Current.CancellationToken);

        string? logWhenTheEngineSawIt = null;
        fake.BeforeWrite = () => logWhenTheEngineSawIt ??= ReadWhileOpen(LogPath());
        await session.SendKeyAsync(TerminalKey.Enter);

        Assert.NotNull(logWhenTheEngineSawIt);
        Assert.Contains("Enter", logWhenTheEngineSawIt);
    }

    /// <summary>Regression: DisposeAsync closed the log before writing Quit, so a report about a hang on close
    /// ended before the one exchange that would show whether LizTerm ever asked the engine to stop.</summary>
    [Fact]
    public async Task Dispose_logs_the_quit_before_it_closes_the_log()
    {
        var fake = new FakeB3270Process();
        var session = new B3270Session(Profile, () => fake, new WireLog(LogPath()));
        await session.StartProcessAsync(TestContext.Current.CancellationToken);

        await session.DisposeAsync();

        Assert.Null(session.WireLogPath);
        Assert.Contains("\"Quit\"", File.ReadAllText(LogPath()));
    }

    [Fact]
    public async Task Stop_when_no_log_is_active_is_a_no_op()
    {
        await using var session = new B3270Session(new SessionProfile { Name = "t", Host = "h" }, () => new FakeB3270Process());
        session.StopWireLog();
        Assert.Null(session.WireLogPath);
    }
}
