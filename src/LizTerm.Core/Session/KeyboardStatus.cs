namespace LizTerm.Core.Session;

public sealed record KeyboardStatus(KeyboardLock Lock, string? LockDetail, bool InsertMode, bool Typeahead, string? LuName)
{
    public static readonly KeyboardStatus Initial = new(KeyboardLock.NotConnected, null, false, false, null);
}
