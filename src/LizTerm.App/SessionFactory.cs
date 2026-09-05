using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Session;

namespace LizTerm.App;

/// <summary>The only place the app names the b3270 backend.</summary>
public static class SessionFactory
{
    public static string OverrideOrigin => B3270Locator.EnvironmentOverride;

    /// <summary>Locates the engine without starting it. Throws <see cref="BackendUnavailableException"/> with the
    /// locator's explanation when it is missing or not executable.</summary>
    public static EngineInfo CheckBackend()
    {
        var location = B3270Locator.Find();
        return new EngineInfo("b3270", null, location.Path, location.Source);
    }

    public static IEmulatorSession Create(SessionProfile profile)
    {
        B3270Location location;
        Func<IB3270Process> processFactory;
        try
        {
            var found = B3270Locator.Find();
            location = found;
            processFactory = () => new B3270ChildProcess(found.Path);
        }
        catch (BackendUnavailableException ex)
        {
            // Nothing to run: let the first connect fail the way the view model already reports, with the
            // locator's own explanation of where it looked.
            location = B3270Location.Unknown;
            processFactory = () => throw ex;
        }
        var wireLog = WireLog.TryFromEnvironment(out var wireLogError);
        return new B3270Session(profile, processFactory, wireLog, wireLogError, location);
    }
}
