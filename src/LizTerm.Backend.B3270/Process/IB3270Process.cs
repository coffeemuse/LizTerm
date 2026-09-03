namespace LizTerm.Backend.B3270.Process;

/// <summary>A b3270 process with line-oriented stdin/stdout. Real implementation spawns a child; tests use a fake.</summary>
public interface IB3270Process : IDisposable
{
    void Start(IReadOnlyList<string> arguments);
    TextReader StandardOutput { get; }
    TextWriter StandardInput { get; }
    Task<int> WaitForExitAsync();
    IReadOnlyList<string> StderrTail { get; }
    void Kill();
}
