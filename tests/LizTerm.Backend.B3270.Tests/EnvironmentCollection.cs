namespace LizTerm.Backend.B3270.Tests;

/// <summary>Tests that set a process-wide environment variable (LIZTERM_WIRE_LOG here) must not run beside tests that read it (spec 8).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvironmentCollection
{
    public const string Name = "Environment";
}
