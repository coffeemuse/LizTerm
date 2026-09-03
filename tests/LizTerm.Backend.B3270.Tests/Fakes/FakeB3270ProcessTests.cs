using LizTerm.Backend.B3270.Tests.Fakes;

namespace LizTerm.Backend.B3270.Tests.Fakes;

public class FakeB3270ProcessTests
{
    [Fact]
    public async Task Emitted_lines_are_readable_and_exit_ends_the_stream()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        fake.Start(["-json"]);
        fake.Emit("one");
        fake.Emit("two");
        fake.Exit(0);
        Assert.Equal("one", fake.StandardOutput.ReadLine());
        Assert.Equal("two", fake.StandardOutput.ReadLine());
        Assert.Null(fake.StandardOutput.ReadLine());
        Assert.Equal(0, await fake.WaitForExitAsync());
    }

    [Fact]
    public async Task Input_lines_are_captured_and_runs_get_default_results()
    {
        var fake = new FakeB3270Process { AutoInitialize = false };
        fake.Start([]);
        fake.StandardInput.Write("""{"run":{"r-tag":"5","actions":[{"action":"Enter"}]}}""" + "\n");
        var line = await fake.WaitForInputAsync(l => l.Contains("Enter"), TimeSpan.FromSeconds(1));
        Assert.Contains("\"r-tag\":\"5\"", line);
        Assert.Equal("""{"run-result":{"r-tag":"5","success":true,"time":0}}""", fake.StandardOutput.ReadLine());
    }

    [Fact]
    public void AutoInitialize_emits_hello_first()
    {
        var fake = new FakeB3270Process();
        fake.Start([]);
        var first = fake.StandardOutput.ReadLine();
        Assert.NotNull(first);
        Assert.StartsWith("{\"initialize\":[{\"hello\":", first);
    }
}
