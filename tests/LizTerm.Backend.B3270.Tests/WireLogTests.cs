namespace LizTerm.Backend.B3270.Tests;

public class WireLogTests
{
    [Fact]
    public void FromEnvironment_returns_null_and_records_error_for_unwritable_path()
    {
        var original = Environment.GetEnvironmentVariable(WireLog.EnvironmentVariable);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "wire.log");
        try
        {
            Environment.SetEnvironmentVariable(WireLog.EnvironmentVariable, path);

            var log = WireLog.FromEnvironment();

            Assert.Null(log);
            Assert.NotNull(WireLog.LastOpenError);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WireLog.EnvironmentVariable, original);
        }
    }
}
