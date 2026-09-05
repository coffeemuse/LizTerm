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

    [Theory]
    [InlineData("L:mvs.local", true, true, null, "mvs.local", 992)]
    [InlineData("L:mvs.local:4270", true, true, null, "mvs.local", 4270)]
    [InlineData("Y:L:mvs.local:4270", true, false, null, "mvs.local", 4270)]
    [InlineData("L:Y:mvs.local", true, false, null, "mvs.local", 992)]
    [InlineData("Y:mvs.local", false, false, null, "mvs.local", 23)]
    [InlineData("CONS01@mvs.local:3270", false, true, "CONS01", "mvs.local", 3270)]
    [InlineData("LU1,LU2@mvs.local", false, true, "LU1,LU2", "mvs.local", 23)]
    [InlineData("L:CONS01@[fe80::1]:4270", true, true, "CONS01", "fe80::1", 4270)]
    public void Prefixes_and_lu_names_are_parsed(string arg, bool tls, bool verify, string? lu, string host, int port)
    {
        var parsed = StartupArguments.Parse([arg]);
        Assert.Null(parsed.Error);
        Assert.Null(parsed.ProfileName);
        Assert.Equal(tls, parsed.UseTls);
        Assert.Equal(verify, parsed.VerifyCertificate);
        Assert.Equal(lu, parsed.LuName);
        Assert.Equal(host, parsed.Host);
        var profile = parsed.Resolve([])!;
        Assert.Equal(port, profile.Port);
        Assert.Equal(tls, profile.UseTls);
        Assert.Equal(verify, profile.VerifyCertificate);
        Assert.Equal(lu, profile.LuName);
    }

    [Theory]
    [InlineData("X:mvs.local")]
    [InlineData("L:L:mvs.local")]
    [InlineData("@mvs.local")]
    [InlineData("L:@mvs.local:23")]
    public void Bad_syntax_is_an_error_with_usage(string arg)
    {
        var parsed = StartupArguments.Parse([arg]);
        Assert.Equal(StartupArguments.Usage, parsed.Error);
        Assert.Null(parsed.Host);
        Assert.Null(parsed.ProfileName);
        Assert.Null(parsed.Resolve([]));
    }

    [Fact]
    public void Ad_hoc_profile_name_is_the_address_without_prefixes()
    {
        Assert.Equal("mvs.local:4270", StartupArguments.Parse(["L:Y:mvs.local:4270"]).Resolve([])!.Name);
        Assert.Equal("CONS01@mvs.local:3270", StartupArguments.Parse(["CONS01@mvs.local:3270"]).Resolve([])!.Name);
        Assert.Equal("mvs.local:992", StartupArguments.Parse(["L:mvs.local"]).Resolve([])!.Name);
    }
}
