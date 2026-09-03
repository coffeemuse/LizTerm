using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Profiles;

public class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void LoadAll_on_missing_directory_is_empty()
    {
        var store = new ProfileStore(_dir);
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void Save_then_LoadAll_round_trips_every_field()
    {
        var store = new ProfileStore(_dir);
        var profile = new SessionProfile
        {
            Name = "TK5", Host = "mvs.local", Port = 3270, UseTls = true, VerifyCertificate = false,
            Model = 4, Extended = false, CodePage = "bracket", LuName = "LU01",
        };
        store.Save(profile);
        var loaded = Assert.Single(store.LoadAll());
        Assert.Equal(profile, loaded);
    }

    [Fact]
    public void LoadAll_sorts_by_name_and_skips_invalid_files()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "zeta", Host = "z" });
        store.Save(new SessionProfile { Name = "alpha", Host = "a" });
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json");
        var names = store.LoadAll().Select(p => p.Name).ToArray();
        Assert.Equal(["alpha", "zeta"], names);
    }

    [Fact]
    public void Delete_removes_the_profile()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "gone", Host = "g" });
        store.Delete("gone");
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void FileNameFor_sanitizes_unsafe_characters()
    {
        Assert.Equal("my_host_prod.json", ProfileStore.FileNameFor("my/host:prod"));
    }
}
