using LizTerm.Core.Screen;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>Waits on host screens by text, never by coordinates. Every timeout throws with the last screen's
/// text, so a failing live run shows what the host actually displayed.</summary>
internal sealed class ScreenWaiter : IDisposable
{
    private readonly IEmulatorSession _session;
    private readonly object _lock = new();
    private ScreenSnapshot _latest;
    private DateTime _lastChange = DateTime.UtcNow;
    private TaskCompletionSource _changed = NewSignal();

    public ScreenWaiter(IEmulatorSession session)
    {
        _session = session;
        _latest = session.CurrentScreen;
        session.ScreenUpdated += OnScreen;
    }

    public ScreenSnapshot Latest { get { lock (_lock) return _latest; } }

    public string LatestText => Latest.ToText();

    /// <summary>Returns the first screen (the current one included) whose text satisfies the predicate.</summary>
    public async Task<ScreenSnapshot> WaitForAsync(Func<string, bool> predicate, TimeSpan timeout, string what)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            ScreenSnapshot screen;
            Task changed;
            lock (_lock) { screen = _latest; changed = _changed.Task; }
            if (predicate(screen.ToText())) return screen;
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException($"Timed out waiting for {what}. Last screen:\n{screen.ToText()}");
            await Task.WhenAny(changed, Task.Delay(remaining));
        }
    }

    /// <summary>b3270 rejects String() while the keyboard is locked, so every keystroke waits for the unlock.</summary>
    public async Task WaitForUnlockedKeyboardAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (_session.KeyboardStatus.Lock != KeyboardLock.Unlocked)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Keyboard still locked ({_session.KeyboardStatus.Lock}). Last screen:\n{LatestText}");
            await Task.Delay(50);
        }
    }

    /// <summary>Returns once no screen update has arrived for <paramref name="quiet"/>, or when the timeout
    /// expires. A host paints a screen in several bursts and keeps writing after the text that identifies the
    /// screen has appeared, so a caller that types straight away sends its text into a layout the host is still
    /// changing; TSO answers that with "INVALID COMMAND NAME SYNTAX".</summary>
    public async Task WaitForQuietAsync(TimeSpan quiet, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            DateTime last;
            lock (_lock) last = _lastChange;
            var idle = DateTime.UtcNow - last;
            if (idle >= quiet || DateTime.UtcNow >= deadline) return;
            await Task.Delay(quiet - idle);
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void OnScreen(object? sender, ScreenSnapshot snapshot)
    {
        lock (_lock)
        {
            _latest = snapshot;
            _lastChange = DateTime.UtcNow;
            _changed.TrySetResult();
            _changed = NewSignal();
        }
    }

    public void Dispose() => _session.ScreenUpdated -= OnScreen;
}
