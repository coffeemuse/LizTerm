namespace LizTerm.Backend.B3270.Tests;

[Collection(EnvironmentCollection.Name)]
public class WireLogTests
{
    [Fact]
    public void TryFromEnvironment_returns_null_and_the_error_for_an_unwritable_path()
    {
        var original = Environment.GetEnvironmentVariable(WireLog.EnvironmentVariable);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "wire.log");
        try
        {
            Environment.SetEnvironmentVariable(WireLog.EnvironmentVariable, path);
            var log = WireLog.TryFromEnvironment(out var error);
            Assert.Null(log);
            Assert.NotNull(error);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WireLog.EnvironmentVariable, original);
        }
    }

    [Fact]
    public void TryFromEnvironment_returns_null_without_error_when_unset()
    {
        var original = Environment.GetEnvironmentVariable(WireLog.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(WireLog.EnvironmentVariable, null);
            Assert.Null(WireLog.TryFromEnvironment(out var error));
            Assert.Null(error);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WireLog.EnvironmentVariable, original);
        }
    }

    [Fact]
    public void Path_constructor_appends_both_directions_with_prefixes()
    {
        var path = Path.Combine(Path.GetTempPath(), "lizterm-wire-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            using (var log = new WireLog(path))
            {
                Assert.Equal(path, log.Path);
                log.Outbound("{\"run\":1}");
                log.Inbound("{\"hello\":1}");
            }
            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.EndsWith(" > {\"run\":1}", lines[0]);
            Assert.EndsWith(" < {\"hello\":1}", lines[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
