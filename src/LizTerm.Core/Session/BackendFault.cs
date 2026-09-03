namespace LizTerm.Core.Session;

public sealed record BackendFault(string Message, IReadOnlyList<string> StderrTail, int? ExitCode);
