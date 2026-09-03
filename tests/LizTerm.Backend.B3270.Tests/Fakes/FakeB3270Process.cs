using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using LizTerm.Backend.B3270.Process;

namespace LizTerm.Backend.B3270.Tests.Fakes;

public sealed partial class FakeB3270Process : IB3270Process
{
    public const string MinimalInitialize =
        """{"initialize":[{"hello":{"version":"4.5.6","build":"fake b3270"}},{"screen-mode":{"model":2,"rows":24,"columns":80,"color":true,"oversize":false,"extended":true}},{"erase":{"logical-rows":24,"logical-columns":80,"fg":"blue","bg":"neutralBlack"}},{"oia":{"field":"lock","value":"not-connected"}}]}""";

    private readonly BlockingCollection<string> _stdout = new();
    private readonly List<string> _stdin = [];
    private readonly object _lock = new();
    private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeB3270Process()
    {
        StandardOutput = new QueueReader(_stdout);
        StandardInput = new LineWriter(OnInputLine);
    }

    public bool AutoInitialize { get; init; } = true;
    public bool Started { get; private set; }
    public IReadOnlyList<string> StartedArguments { get; private set; } = [];
    public Func<string, IReadOnlyList<string>>? RunResponder { get; set; }
    public TextReader StandardOutput { get; }
    public TextWriter StandardInput { get; }
    public IReadOnlyList<string> StderrTail => ["fake stderr line"];

    public IReadOnlyList<string> InputLines
    {
        get { lock (_lock) return _stdin.ToArray(); }
    }

    public void Start(IReadOnlyList<string> arguments)
    {
        Started = true;
        StartedArguments = arguments;
        if (AutoInitialize) Emit(MinimalInitialize);
    }

    public void Emit(string line)
    {
        if (!_stdout.IsAddingCompleted) _stdout.Add(line);
    }

    public void Exit(int code)
    {
        if (!_stdout.IsAddingCompleted) _stdout.CompleteAdding();
        _exit.TrySetResult(code);
    }

    public Task<int> WaitForExitAsync() => _exit.Task;

    public void Kill() => Exit(-1);

    public void Dispose() => Exit(0);

    public async Task<string> WaitForInputAsync(Func<string, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var match = InputLines.FirstOrDefault(predicate);
            if (match is not null) return match;
            await Task.Delay(10);
        }
        throw new TimeoutException("No matching input line within " + timeout);
    }

    private void OnInputLine(string line)
    {
        lock (_lock) _stdin.Add(line);
        if (RunResponder is not null)
        {
            foreach (var reply in RunResponder(line)) Emit(reply);
            return;
        }
        var tag = TagRegex().Match(line);
        if (tag.Success)
            Emit($$$"""{"run-result":{"r-tag":"{{{tag.Groups[1].Value}}}","success":true,"time":0}}""");
    }

    [GeneratedRegex("\"r-tag\":\"([^\"]+)\"")]
    private static partial Regex TagRegex();

    private sealed class QueueReader(BlockingCollection<string> queue) : TextReader
    {
        public override string? ReadLine() => queue.TryTake(out var line, Timeout.Infinite) ? line : null;
    }

    private sealed class LineWriter(Action<string> onLine) : TextWriter
    {
        private readonly StringBuilder _pending = new();
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                var line = _pending.ToString();
                _pending.Clear();
                onLine(line);
            }
            else if (value != '\r')
            {
                _pending.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (value is null) return;
            foreach (var c in value) Write(c);
        }
    }
}
