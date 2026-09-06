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
    public void A_pinned_certificate_round_trips_and_an_unpinned_profile_reads_back_null()
    {
        var store = new ProfileStore(_dir);
        var pin = new CertificatePin("8C:13:6A:01", "O = tn3270proxy quick-start, CN = localhost",
            "-----BEGIN CERTIFICATE-----\nZmFrZQ==\n-----END CERTIFICATE-----\n");
        store.Save(new SessionProfile { Name = "pinned", Host = "gw", UseTls = true, PinnedCertificate = pin });
        store.Save(new SessionProfile { Name = "plain", Host = "h" });

        var loaded = store.LoadAll();
        Assert.Equal(pin, loaded.Single(p => p.Name == "pinned").PinnedCertificate);
        Assert.Null(loaded.Single(p => p.Name == "plain").PinnedCertificate);
        // The source-generated context writes every property, so the absent pin is an explicit null (spec 3.1).
        var plainJson = Directory.GetFiles(_dir, "*.json").Select(File.ReadAllText).Single(j => j.Contains("\"name\": \"plain\""));
        Assert.Contains("\"pinnedCertificate\": null", plainJson);
    }

    /// <summary>A file written before a field existed has no such field and reads as the declared default: erase
    /// for DestructiveBackspace, which is what every x3270-family default keymap does (spec 2), and above all
    /// verification on, which the CLR default would silently turn off. A file that says false keeps false: the
    /// editor always writes the field, so a saved choice survives the default flip (spec 3.2).</summary>
    [Fact]
    public void LoadAll_reads_a_file_missing_fields_with_every_declared_default_and_an_explicit_false_as_false()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "old.json"), """{"name":"old","host":"h"}""");
        File.WriteAllText(Path.Combine(_dir, "off.json"), """{"name":"off","host":"h","port":23,"destructiveBackspace":false,"verifyCertificate":false}""");
        var loaded = new ProfileStore(_dir).LoadAll();
        var old = loaded.Single(p => p.Name == "old");
        Assert.True(old.VerifyCertificate);
        Assert.True(old.DestructiveBackspace);
        Assert.Equal(23, old.Port);
        Assert.Equal(2, old.Model);
        Assert.True(old.Extended);
        Assert.Equal("cp037", old.CodePage);
        Assert.Null(old.PinnedCertificate);
        var off = loaded.Single(p => p.Name == "off");
        Assert.False(off.DestructiveBackspace);
        Assert.False(off.VerifyCertificate);
    }

    /// <summary>A pin without its PEM or fingerprint (a hand-edited or redacted file) is dropped on load rather
    /// than handed to the engine, which would refuse an empty trust file with an error naming a temp file that no
    /// longer exists and never reach the certificate prompt.</summary>
    [Fact]
    public void A_pin_missing_its_pem_or_fingerprint_is_dropped_on_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "empty.json"), """{"name":"empty","host":"h","useTls":true,"pinnedCertificate":{}}""");
        File.WriteAllText(Path.Combine(_dir, "blank.json"), """{"name":"blank","host":"h","useTls":true,"pinnedCertificate":{"sha256":"AA","subject":"CN=x","pem":""}}""");
        File.WriteAllText(Path.Combine(_dir, "nosha.json"), """{"name":"nosha","host":"h","useTls":true,"pinnedCertificate":{"subject":"CN=x","pem":"-----BEGIN CERTIFICATE-----\nAA==\n-----END CERTIFICATE-----\n"}}""");
        var store = new ProfileStore(_dir);
        Assert.All(store.LoadAll(), p => Assert.Null(p.PinnedCertificate));
        Assert.Null(store.Load("empty")!.PinnedCertificate);
    }

    /// <summary>Update writes one change into the profile as it is on disk now, so a session window holding the
    /// profile it was opened with can pin a certificate without discarding edits saved from the picker since.</summary>
    [Fact]
    public void Update_applies_the_change_to_the_stored_profile_or_to_the_fallback_when_it_is_gone()
    {
        var store = new ProfileStore(_dir);
        var opened = new SessionProfile { Name = "gw", Host = "gw", Port = 4270, UseTls = true, Model = 2 };
        store.Save(opened);
        store.Save(opened with { Model = 4, VerifyCertificate = false });
        var pin = new CertificatePin("8C:13", "CN=gw", "pem");

        store.Update(opened with { PinnedCertificate = pin, VerifyCertificate = true },
            current => current with { PinnedCertificate = pin, VerifyCertificate = true });
        var stored = store.Load("gw")!;
        Assert.Equal(4, stored.Model);
        Assert.True(stored.VerifyCertificate);
        Assert.Equal(pin, stored.PinnedCertificate);

        store.Delete("gw");
        store.Update(opened, current => current with { PinnedCertificate = pin });
        Assert.Equal(opened with { PinnedCertificate = pin }, store.Load("gw"));
        Assert.Null(store.Load("missing"));
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
