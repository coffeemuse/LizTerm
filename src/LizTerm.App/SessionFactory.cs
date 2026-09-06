using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.App;

/// <summary>The only place the app names the b3270 backend.</summary>
public static class SessionFactory
{
    public static string OverrideOrigin => B3270Locator.EnvironmentOverride;

    /// <summary>The one spelling of how the app resolves the engine, shared by <see cref="CheckBackend"/> and
    /// <see cref="Create(SessionProfile)"/> so the startup gate and the session it later spawns can never
    /// disagree about where to look.</summary>
    internal static (string? OverridePath, string BaseDirectory) DefaultLocation =>
        (Environment.GetEnvironmentVariable(B3270Locator.EnvironmentOverride), AppContext.BaseDirectory);

    /// <summary>Locates the engine without starting it. Throws <see cref="BackendUnavailableException"/> with the
    /// locator's explanation when it is missing or not executable.</summary>
    public static EngineInfo CheckBackend()
    {
        var (overridePath, baseDirectory) = DefaultLocation;
        var location = B3270Locator.Find(overridePath, baseDirectory);
        return new EngineInfo("b3270", null, location.Path, location.Source);
    }

    public static IEmulatorSession Create(SessionProfile profile)
    {
        var (overridePath, baseDirectory) = DefaultLocation;
        return Create(profile, overridePath, baseDirectory);
    }

    /// <summary>Test seam: resolves the engine from an explicit override and base directory instead of the process
    /// environment and the app's own directory, so a test can stand in an empty directory whatever the build copied
    /// beside the real app.</summary>
    internal static IEmulatorSession Create(SessionProfile profile, string? overridePath, string baseDirectory)
    {
        B3270Location location;
        Func<IB3270Process> processFactory;
        try
        {
            var found = B3270Locator.Find(overridePath, baseDirectory);
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
        // The backend assumes no trust anchors; the app is where the machine's own store enters, the same way it
        // supplies ICertificateFetcher rather than the backend reaching for one.
        return new B3270Session(profile, processFactory, wireLog, wireLogError, location)
        {
            TrustAnchors = SystemTrustAnchors.Default,
        };
    }
}
