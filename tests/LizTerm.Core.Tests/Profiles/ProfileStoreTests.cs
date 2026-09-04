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
            Model = 4, Extended = false, CodePage = "bracket", LuName = "LU01", DestructiveBackspace = true,
        };
        store.Save(profile);
        var loaded = Assert.Single(store.LoadAll());
        Assert.Equal(profile, loaded);
    }

    [Fact]
    public void LoadAll_reads_older_files_with_destructive_backspace_off()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "old.json"), """{"name":"old","host":"h","port":23}""");
        var loaded = Assert.Single(new ProfileStore(_dir).LoadAll());
        Assert.False(loaded.DestructiveBackspace);
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

    [Fact]
    public void LoadAll_skips_files_it_cannot_read()
    {
        var store = new ProfileStore(_dir);

        // Save one valid profile
        store.Save(new SessionProfile { Name = "readable", Host = "r" });

        // Create an unreadable file
        var unreadablePath = Path.Combine(_dir, "unreadable.json");
        File.WriteAllText(unreadablePath, "{ \"name\": \"unreadable\", \"host\": \"u\" }");

        // Make it unreadable on non-Windows platforms
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(unreadablePath, UnixFileMode.None);
        }

        try
        {
            // LoadAll should return only the readable profile
            var profiles = store.LoadAll();
            var readableProfile = Assert.Single(profiles);
            Assert.Equal("readable", readableProfile.Name);
        }
        finally
        {
            // Restore file mode so Dispose can clean up the directory
            if (!OperatingSystem.IsWindows() && File.Exists(unreadablePath))
            {
                File.SetUnixFileMode(unreadablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
    }
}
