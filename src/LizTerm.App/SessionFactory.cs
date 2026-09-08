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

    /// <summary>The one spelling of an engine that has been located but never run, so every caller below
    /// describes the same binary the same way.</summary>
    private static EngineInfo Describe(B3270Location location) =>
        new("b3270", null, location.Path, location.Source);

    /// <summary>What to call an engine the locator refused. Find throws the same type for "no binary anywhere"
    /// and "a binary is there and unusable", which is what B3270Locator.Candidates exists to tell apart: a file
    /// that is present keeps its own path and source, so About and the status bar name the thing to chmod
    /// instead of claiming nothing was found. Only a genuinely absent engine is Unknown.</summary>
    private static B3270Location Refused(string? overridePath, string baseDirectory) =>
        B3270Locator.Candidates(overridePath, baseDirectory).FirstOrDefault(c => File.Exists(c.Path))
        ?? B3270Location.Unknown;

    /// <summary>Locates the engine without starting it. Throws <see cref="BackendUnavailableException"/> with the
    /// locator's explanation when it is missing or not executable.</summary>
    public static EngineInfo CheckBackend()
    {
        var (overridePath, baseDirectory) = DefaultLocation;
        return CheckBackend(overridePath, baseDirectory);
    }

    /// <summary>Test seam, matching Create's: resolves from an explicit override and base directory instead of
    /// the process environment and the app's own directory.</summary>
    internal static EngineInfo CheckBackend(string? overridePath, string baseDirectory) =>
        Describe(B3270Locator.Find(overridePath, baseDirectory));

    /// <summary>CheckBackend's non-throwing sibling, for About, which must render something whatever the
    /// outcome. It answers with <see cref="Refused"/>, the same value Create hands a session that has nothing to
    /// run, so the two paths cannot disagree about what an unusable engine looks like.</summary>
    public static EngineInfo CheckBackendOrUnknown()
    {
        var (overridePath, baseDirectory) = DefaultLocation;
        return CheckBackendOrUnknown(overridePath, baseDirectory);
    }

    /// <inheritdoc cref="CheckBackendOrUnknown()"/>
    internal static EngineInfo CheckBackendOrUnknown(string? overridePath, string baseDirectory)
    {
        try
        {
            return CheckBackend(overridePath, baseDirectory);
        }
        catch (BackendUnavailableException)
        {
            return Describe(Refused(overridePath, baseDirectory));
        }
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
            // locator's own explanation of where it looked. The status bar still names the binary that is
            // there but unusable, rather than calling it missing.
            location = Refused(overridePath, baseDirectory);
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
