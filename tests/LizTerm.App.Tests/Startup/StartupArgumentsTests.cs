using LizTerm.App.Startup;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Startup;

public class StartupArgumentsTests
{
    [Fact]
    public void No_arguments_means_picker() => Assert.Equal(new StartupArguments(null, null, null), StartupArguments.Parse([]));

    [Fact]
    public void Bare_word_is_a_profile_name() => Assert.Equal(new StartupArguments("TK5", null, null), StartupArguments.Parse(["TK5"]));

    [Theory]
    [InlineData("mvs.local", "mvs.local", null)]
    [InlineData("mvs.local:3270", "mvs.local", 3270)]
    [InlineData("localhost:3270", "localhost", 3270)]
    [InlineData("localhost", "localhost", null)]
    [InlineData("[::1]:23", "::1", 23)]
    [InlineData("[fe80::1]", "fe80::1", null)]
    public void Host_forms_are_recognized(string arg, string host, int? port)
    {
        var parsed = StartupArguments.Parse([arg]);
        Assert.Null(parsed.ProfileName);
        Assert.Equal(host, parsed.Host);
        Assert.Equal(port, parsed.Port);
    }

    [Fact]
    public void Resolve_finds_profile_case_insensitively()
    {
        var profiles = new[] { new SessionProfile { Name = "TK5", Host = "mvs.local" } };
        Assert.Same(profiles[0], StartupArguments.Parse(["tk5"]).Resolve(profiles));
        Assert.Null(StartupArguments.Parse(["missing"]).Resolve(profiles));
    }

    [Fact]
    public void Resolve_builds_an_ad_hoc_profile_for_hosts()
    {
        var profile = StartupArguments.Parse(["mvs.local:3270"]).Resolve([]);
        Assert.NotNull(profile);
        Assert.Equal("mvs.local:3270", profile!.Name);
        Assert.Equal("mvs.local", profile.Host);
        Assert.Equal(3270, profile.Port);
        Assert.Equal(23, StartupArguments.Parse(["mvs.local"]).Resolve([])!.Port);
    }
}
