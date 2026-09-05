using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Session;

namespace LizTerm.App;

/// <summary>The only place the app names the b3270 backend.</summary>
public static class SessionFactory
{
    public static IEmulatorSession Create(SessionProfile profile) =>
        new B3270Session(profile, () => new B3270ChildProcess(B3270Locator.Find().Path), WireLog.FromEnvironment());
}
