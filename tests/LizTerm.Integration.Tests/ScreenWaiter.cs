using System.Diagnostics;
using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>Waits on host screens by text, never by coordinates. Every timeout throws with the last screen's text,
/// so a failing live run shows what the host actually displayed. Time comes from one Stopwatch, so a clock step
/// during a run cannot stretch or cut a wait, and each wait is one WaitAsync on the next-change signal rather than
/// an abandoned Task.Delay per update (spec 8).</summary>
internal sealed class ScreenWaiter : IDisposable
{
    private readonly IEmulatorSession _session;
    private readonly object _lock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private ScreenSnapshot _latest;
    private TimeSpan _lastChange;
    private TaskCompletionSource _changed = NewSignal();

    public ScreenWaiter(IEmulatorSession session)
    {
        _session = session;
        _latest = session.CurrentScreen;
        _lastChange = _clock.Elapsed;
        session.ScreenUpdated += OnScreen;
    }

    public ScreenSnapshot Latest { get { lock (_lock) return _latest; } }

    public string LatestText => Latest.ToText();

    /// <summary>Returns the first screen (the current one included) whose text satisfies the predicate.</summary>
    public async Task<ScreenSnapshot> WaitForAsync(Func<string, bool> predicate, TimeSpan timeout, string what)
    {
        var deadline = _clock.Elapsed + timeout;
        while (true)
        {
            ScreenSnapshot screen;
            Task changed;
            lock (_lock) { screen = _latest; changed = _changed.Task; }
            if (predicate(screen.ToText())) return screen;
            var remaining = deadline - _clock.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException($"Timed out waiting for {what}. Last screen:\n{screen.ToText()}");
            await WaitOrTimeoutAsync(changed, remaining);
        }
    }

    /// <summary>b3270 rejects String() while the keyboard is locked, so every keystroke waits for the unlock.</summary>
    public async Task WaitForUnlockedKeyboardAsync(TimeSpan timeout)
    {
        var deadline = _clock.Elapsed + timeout;
        while (_session.KeyboardStatus.Lock != KeyboardLock.Unlocked)
        {
            if (_clock.Elapsed > deadline)
                throw new TimeoutException($"Keyboard still locked ({_session.KeyboardStatus.Lock}). Last screen:\n{LatestText}");
            await Task.Delay(50);
        }
    }

    /// <summary>Returns once no screen update has arrived for <paramref name="quiet"/>, or when the timeout expires,
    /// whichever is first; the wait never overshoots the timeout. A host paints a screen in several bursts and keeps
    /// writing after the text that identifies the screen has appeared, so a caller that types straight away sends
    /// its text into a layout the host is still changing; TSO answers that with "INVALID COMMAND NAME SYNTAX".</summary>
    public async Task WaitForQuietAsync(TimeSpan quiet, TimeSpan timeout)
    {
        var deadline = _clock.Elapsed + timeout;
        while (true)
        {
            TimeSpan last;
            lock (_lock) last = _lastChange;
            var now = _clock.Elapsed;
            var idle = now - last;
            if (idle >= quiet || now >= deadline) return;
            var untilQuiet = quiet - idle;
            var untilDeadline = deadline - now;
            await Task.Delay(untilQuiet < untilDeadline ? untilQuiet : untilDeadline);
        }
    }

    private static async Task WaitOrTimeoutAsync(Task signal, TimeSpan timeout)
    {
        try
        {
            await signal.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            // The caller re-checks its deadline.
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void OnScreen(object? sender, ScreenSnapshot snapshot)
    {
        lock (_lock)
        {
            _latest = snapshot;
            _lastChange = _clock.Elapsed;
            _changed.TrySetResult();
            _changed = NewSignal();
        }
    }

    public void Dispose() => _session.ScreenUpdated -= OnScreen;
}
