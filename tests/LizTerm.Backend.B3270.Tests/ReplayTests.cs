using LizTerm.Backend.B3270.Tests.Fakes;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Backend.B3270.Tests;

public class ReplayTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public async Task Ibmlink_help_screen_replays_to_expected_state()
    {
        var fake = new FakeB3270Process { AutoInitialize = false, RunResponder = _ => [] };
        foreach (var line in File.ReadLines(Fixture("ibmlink-help.jsonl"))) fake.Emit(line);
        fake.Exit(0);

        var session = new B3270Session(new SessionProfile { Name = "replay", Host = "127.0.0.1" }, () => fake);
        var states = new List<ConnectionState>();
        var screens = 0;
        session.ConnectionChanged += (_, s) => states.Add(s);
        session.ScreenUpdated += (_, _) => screens++;
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Faulted += (_, _) => ended.TrySetResult();

        await session.StartProcessAsync(TestContext.Current.CancellationToken);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var screen = session.CurrentScreen;
        Assert.Equal(24, screen.Rows);
        Assert.Equal(80, screen.Columns);
        Assert.Equal("SVM0201P", screen.GetText(0, 1, 8));
        Assert.Equal("SYSTEM: IBM0SM23", screen.GetText(1, 1, 16));
        // The recording ends with Quit, after which b3270 hides the cursor but its position is kept.
        Assert.Equal(new CursorPosition(20, 12, false), screen.Cursor);
        Assert.Equal(HostColor.NeutralWhite, screen[6, 0].Foreground);
        Assert.True(screen[6, 0].Rendition.HasFlag(CellRendition.Highlight));
        // This host negotiates TN3270E from the first exchange (see the recorded "cstate" lines in
        // the original .trc), so the session never passes through the plain Connected3270 state --
        // it goes straight to ConnectedUnbound/ConnectedTn3270E once the BIND completes.
        Assert.Contains(ConnectionState.ConnectedTn3270E, states);
        Assert.Equal(ConnectionState.Disconnected, session.ConnectionState);
        Assert.True(screens > 0);
    }
}
