// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

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

    /// <summary>The Assert.Equal is the point, not the fields: SessionProfile is a record, and a record
    /// compares a COLLECTION member by reference, so a Tags held as a list would make a profile read back from
    /// disk unequal to the one written. TagSet's value equality is what keeps this assertion true.</summary>
    [Fact]
    public void Save_then_LoadAll_round_trips_every_field()
    {
        var store = new ProfileStore(_dir);
        var profile = new SessionProfile
        {
            Name = "TK5", Host = "mvs.local", Port = 3270, UseTls = true, VerifyCertificate = false,
            Model = 4, Extended = false, CodePage = "bracket", LuName = "LU01", DestructiveBackspace = true,
            Tags = TagSet.From(["FAVORITE", "PROD", "MVS"]), Note = "IND$FILE test box, no live data",
        };
        store.Save(profile);
        var loaded = Assert.Single(store.LoadAll());
        Assert.Equal(profile, loaded);
        Assert.Equal(["FAVORITE", "PROD", "MVS"], loaded.Tags.Names);
        Assert.Equal("IND$FILE test box, no live data", loaded.Note);
    }

    [Fact]
    public void Tags_are_written_as_a_json_string_array()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "p", Host = "h", Tags = TagSet.From(["PROD", "MVS"]) });
        var json = File.ReadAllText(Directory.GetFiles(_dir, "*.json").Single());
        Assert.Contains("\"PROD\"", json);
        Assert.Contains("\"MVS\"", json);
        Assert.DoesNotContain("\"color\"", json);
    }

    /// <summary>A profile file written before either field existed. Both must read as their declared defaults,
    /// which is what makes this change need no migration (spec 3.1).</summary>
    [Fact]
    public void A_file_without_tags_or_a_note_reads_as_an_empty_set_and_null()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "old.json"), """
            { "name": "old", "host": "h", "port": 23 }
            """);

        var old = Assert.Single(new ProfileStore(_dir).LoadAll());
        Assert.True(old.Tags.IsEmpty);
        Assert.Null(old.Note);
        Assert.Equal(default, old.Tags);
    }

    /// <summary>A hand-edited file is repaired on load rather than refused, the same choice Read already makes
    /// for a pin with no PEM. Nothing here should cost the profile.</summary>
    [Fact]
    public void A_hand_edited_tags_array_is_repaired_on_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "messy.json"), """
            { "name": "messy", "host": "h", "tags": ["#PROD", " prod ", "", "MVS"] }
            """);

        var messy = Assert.Single(new ProfileStore(_dir).LoadAll());
        Assert.Equal(["PROD", "MVS"], messy.Tags.Names);
    }

    /// <summary>Not an array at all. The whole profile must survive: losing a saved host because one key was
    /// mistyped by hand is the outcome LoadAll's leniency exists to avoid.</summary>
    [Fact]
    public void A_tags_key_that_is_not_an_array_costs_the_tags_not_the_profile()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "single.json"), """{ "name": "single", "host": "h", "tags": "PROD" }""");
        File.WriteAllText(Path.Combine(_dir, "object.json"), """{ "name": "object", "host": "h", "tags": { "a": 1 } }""");

        var loaded = new ProfileStore(_dir).LoadAll();
        Assert.Equal(["PROD"], loaded.Single(p => p.Name == "single").Tags.Names);
        Assert.True(loaded.Single(p => p.Name == "object").Tags.IsEmpty);
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
    /// editor always writes the field, so a saved choice survives the default flip (spec 3.2).
    ///
    /// The three tier-1 fields are here for a stronger reason than coverage. The keep-alive default is
    /// retroactive by design — every profile already on disk gains a 60-second keep-alive the moment 0.4.0 runs,
    /// with no migration and no rewrite — and this assertion IS that decision (tier-1 spec 2.2). Without it the
    /// decision lives only in a constructor signature a later refactor could quietly change.</summary>
    [Fact]
    public void LoadAll_reads_a_file_missing_fields_with_every_declared_default_and_an_explicit_false_as_false()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "old.json"), """{"name":"old","host":"h"}""");
        File.WriteAllText(Path.Combine(_dir, "off.json"), """{"name":"off","host":"h","port":23,"destructiveBackspace":false,"verifyCertificate":false,"keepAliveSeconds":0,"autoReconnect":true,"oversize":"132x43"}""");
        var loaded = new ProfileStore(_dir).LoadAll();
        var old = loaded.Single(p => p.Name == "old");
        Assert.True(old.VerifyCertificate);
        Assert.True(old.DestructiveBackspace);
        Assert.Equal(23, old.Port);
        Assert.Equal(2, old.Model);
        Assert.True(old.Extended);
        Assert.Equal("cp037", old.CodePage);
        Assert.Null(old.PinnedCertificate);
        Assert.Equal(60, old.KeepAliveSeconds);
        Assert.False(old.AutoReconnect);
        Assert.Null(old.Oversize);
        var off = loaded.Single(p => p.Name == "off");
        Assert.False(off.DestructiveBackspace);
        Assert.False(off.VerifyCertificate);
        // A saved 0 must survive rather than reading back as the 60 default, or turning keep-alive off would
        // be impossible.
        Assert.Equal(0, off.KeepAliveSeconds);
        Assert.True(off.AutoReconnect);
        Assert.Equal("132x43", off.Oversize);
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

    /// <summary>Save refuses a whitespace-only name, so Read must too: a hand-edited file that loads but can never
    /// be saved back would throw ArgumentException out of every path that rewrites profiles, TagMaintenance among them.</summary>
    [Fact]
    public void LoadAll_skips_a_profile_whose_name_is_only_whitespace()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "kept", Host = "k" });
        File.WriteAllText(Path.Combine(_dir, "blank.json"), """{"name":"  ","host":"h","tags":["PROD"]}""");

        var names = store.LoadAll().Select(p => p.Name).ToArray();

        Assert.Equal(["kept"], names);
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

        // Create a file the store cannot read. An exclusive handle denies the read on every platform, where a Unix
        // file mode of None leaves the file plainly readable on Windows, which does not honour it.
        var unreadablePath = Path.Combine(_dir, "unreadable.json");
        File.WriteAllText(unreadablePath, "{ \"name\": \"unreadable\", \"host\": \"u\" }");

        using (File.Open(unreadablePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // LoadAll should return only the readable profile
            var profiles = store.LoadAll();
            var readableProfile = Assert.Single(profiles);
            Assert.Equal("readable", readableProfile.Name);
        }
    }

    /// <summary>#50. Read swallows a JsonException and LoadAll skips the file, so a half-written profile is
    /// indistinguishable from a deleted one. A temp-file-and-rename never leaves a reader a partial file.</summary>
    [Fact]
    public void Save_never_leaves_a_partial_file_behind()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "MVS", Host = "first.host" });
        store.Save(new SessionProfile { Name = "MVS", Host = "second.host" });

        var loaded = Assert.Single(store.LoadAll());
        Assert.Equal("second.host", loaded.Host);

        // The rename is within one directory, so nothing else may be left lying around for LoadAll to trip on.
        Assert.Equal(["MVS.json"], Directory.GetFiles(_dir).Select(Path.GetFileName).Order());
    }

    /// <summary>A profile's file is the one that holds its name, whatever that file is called — renamed by hand, or
    /// written on a platform whose FileNameFor spelled the name differently. Addressed by FileNameFor alone, such a
    /// profile listed in the picker while its star and Delete did nothing and every save wrote a second file.</summary>
    [Fact]
    public void A_profile_in_a_file_named_for_something_else_is_loaded_saved_updated_and_deleted_there()
    {
        var store = new ProfileStore(_dir);
        Place("mvs-backup.json", new SessionProfile { Name = "MVS", Host = "first.host" });

        Assert.Equal("first.host", store.Load("MVS")?.Host);

        store.Save(new SessionProfile { Name = "MVS", Host = "second.host" });
        store.Update(new SessionProfile { Name = "MVS", Host = "fallback" }, current => current with { Model = 4 });
        Assert.Equal(["mvs-backup.json"], FileNames());
        var loaded = Assert.Single(store.LoadAll());
        Assert.Equal(("second.host", 4), (loaded.Host, loaded.Model));

        store.Delete("MVS");
        Assert.Empty(FileNames());
    }

    /// <summary>Names match ignoring case, because the picker's Edit treats a case-only rename as the same profile
    /// and deletes nothing. An exact match would leave the old file behind as a second profile.</summary>
    [Fact]
    public void A_case_only_rename_rewrites_the_profile_s_own_file()
    {
        var store = new ProfileStore(_dir);
        Place("tk5-old.json", new SessionProfile { Name = "TK5", Host = "h" });

        store.Save(new SessionProfile { Name = "tk5", Host = "h" });

        Assert.Equal(["tk5-old.json"], FileNames());
        Assert.Equal("tk5", Assert.Single(store.LoadAll()).Name);
    }

    /// <summary>FileNameFor is not one-to-one: a/b and a:b are both a_b.json. The second save must not overwrite the
    /// first profile, and each name must go on addressing its own file.</summary>
    [Fact]
    public void Two_names_that_sanitize_to_one_file_name_are_two_profiles()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "a/b", Host = "slash" });
        store.Save(new SessionProfile { Name = "a:b", Host = "colon" });

        Assert.Equal(["a/b", "a:b"], store.LoadAll().Select(p => p.Name));
        Assert.Equal("slash", store.Load("a/b")?.Host);
        Assert.Equal("colon", store.Load("a:b")?.Host);

        store.Delete("a:b");
        Assert.Equal("slash", Assert.Single(store.LoadAll()).Host);
    }

    /// <summary>FileNameFor's file can hold another profile, renamed inside the file by hand, or not read as a profile
    /// at all, which LoadAll skips and the user can repair. Either way it is not this profile's to overwrite.</summary>
    [Fact]
    public void Save_never_overwrites_a_file_that_is_not_that_profile()
    {
        var store = new ProfileStore(_dir);
        Place("MVS.json", new SessionProfile { Name = "renamed", Host = "r" });
        File.WriteAllText(Path.Combine(_dir, "TK5.json"), "{ not json");

        store.Save(new SessionProfile { Name = "MVS", Host = "m" });
        store.Save(new SessionProfile { Name = "TK5", Host = "t" });

        Assert.Equal(["MVS", "renamed", "TK5"], store.LoadAll().Select(p => p.Name));
        Assert.Equal("r", store.Load("renamed")?.Host);
        Assert.Equal("m", store.Load("MVS")?.Host);
        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(_dir, "TK5.json")));
        Assert.Equal("t", store.Load("TK5")?.Host);
    }

    /// <summary>Two files can hold one name, a copy made in a file manager. Load, Save and Delete pick the same one —
    /// FileNameFor's own file when it is among them, else the first by ordinal file name — so a save never lands in
    /// one file and a later delete in the other.</summary>
    [Fact]
    public void With_two_files_holding_one_name_every_operation_picks_the_same_file()
    {
        var store = new ProfileStore(_dir);
        Place("MVS old.json", new SessionProfile { Name = "MVS", Host = "old" });
        Place("MVS copy.json", new SessionProfile { Name = "MVS", Host = "copy" });

        Assert.Equal("copy", store.Load("MVS")?.Host);
        store.Save(new SessionProfile { Name = "MVS", Host = "copy", Model = 5 });
        Assert.Equal(5, store.Load("MVS")?.Model);
        store.Delete("MVS");
        Assert.Equal(["MVS old.json"], FileNames());

        Place("MVS.json", new SessionProfile { Name = "MVS", Host = "canonical" });
        Assert.Equal("canonical", store.Load("MVS")?.Host);
    }

    /// <summary>A file holding the name exactly is found before one holding it only ignoring case, wherever each sits:
    /// "MVS" and "mvs" in two files are two rows, and the mvs row must never star, edit or delete MVS.</summary>
    [Fact]
    public void A_name_held_exactly_is_found_before_one_that_differs_only_in_case()
    {
        var store = new ProfileStore(_dir);
        Place("MVS.json", new SessionProfile { Name = "MVS", Host = "upper" });
        Place("other.json", new SessionProfile { Name = "mvs", Host = "lower" });

        Assert.Equal("lower", store.Load("mvs")?.Host);
        store.Save(new SessionProfile { Name = "mvs", Host = "lower", Model = 5 });
        Assert.Equal(["MVS.json", "other.json"], FileNames());
        store.Delete("mvs");

        Assert.Equal(["MVS.json"], FileNames());
        Assert.Equal("upper", Assert.Single(store.LoadAll()).Host);
    }

    /// <summary>LoadAll lists a name two files hold once, as the file every call that takes a name picks. A row for the
    /// other would star, edit and delete the first, and Manage Tags, which saves each profile LoadAll lists, would
    /// write both into one file and leave the other carrying the old tag.</summary>
    [Fact]
    public void Two_files_holding_one_name_list_once_as_the_file_every_operation_picks()
    {
        var store = new ProfileStore(_dir);
        Place("MVS old.json", new SessionProfile { Name = "MVS", Host = "old" });
        Place("MVS copy.json", new SessionProfile { Name = "MVS", Host = "copy" });

        Assert.Equal("copy", Assert.Single(store.LoadAll()).Host);
    }

    /// <summary>A file that cannot be opened for the moment (a sync tool's lock, another LizTerm replacing it) may be the
    /// profile's own. Writing "MVS (2).json" instead would leave the edit behind MVS.json once it reads again, since
    /// FileNameFor's file is found first, so Save fails and writes nothing.</summary>
    [Fact]
    public void Save_fails_and_writes_nothing_while_a_file_it_may_own_cannot_be_read()
    {
        var store = new ProfileStore(_dir);
        store.Save(new SessionProfile { Name = "MVS", Host = "old" });

        using (File.Open(Path.Combine(_dir, "MVS.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => store.Save(new SessionProfile { Name = "MVS", Host = "new" }));
        }

        Assert.Equal(["MVS.json"], FileNames());
        Assert.Equal("old", store.Load("MVS")?.Host);
    }

    /// <summary>Puts a profile in this store's directory under <paramref name="file"/>, written by a store of its own
    /// so the JSON is exactly what Save produces. The staging directory is below <c>_dir</c>, which LoadAll does not
    /// descend into and Dispose removes.</summary>
    private void Place(string file, SessionProfile profile)
    {
        var staging = new ProfileStore(Path.Combine(_dir, "staging"));
        staging.Save(profile);
        File.Move(Path.Combine(staging.Directory, ProfileStore.FileNameFor(profile.Name)), Path.Combine(_dir, file));
    }

    [Fact]
    public void The_host_files_fields_round_trip_and_default_to_null()
    {
        var store = new ProfileStore(_dir);
        var pin = new CertificatePin("AA:BB", "CN=proxy", "-----BEGIN CERTIFICATE-----\nAA==\n-----END CERTIFICATE-----\n");
        var full = new SessionProfile { Name = "rest", Host = "mvs", HostFilesUrl = "https://proxy/zosmf", HostFilesUserid = "IBMUSER", HostFilesPinnedCertificate = pin };
        var plain = new SessionProfile { Name = "plain", Host = "mvs" };
        store.Save(full);
        store.Save(plain);

        Assert.Equal(full, store.Load("rest"));
        Assert.Equal(plain, store.Load("plain"));
        var text = File.ReadAllText(Directory.GetFiles(_dir).Single(f => File.ReadAllText(f).Contains("\"plain\"")));
        Assert.Contains("\"hostFilesUrl\": null", text);
        Assert.Contains("\"hostFilesPinnedCertificate\": null", text);
    }

    [Fact]
    public void A_file_without_host_files_fields_reads_them_as_null()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "old.json"), """{"name":"old","host":"h"}""");
        var loaded = new ProfileStore(_dir).Load("old")!;
        Assert.Null(loaded.HostFilesUrl);
        Assert.Null(loaded.HostFilesUserid);
        Assert.Null(loaded.HostFilesPinnedCertificate);
    }

    [Fact]
    public void A_host_files_pin_missing_its_pem_or_fingerprint_is_dropped_on_load()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "bad.json"), """{"name":"bad","host":"h","hostFilesUrl":"http://h/zosmf","hostFilesPinnedCertificate":{"sha256":"AA","subject":"CN=x","pem":""}}""");
        var loaded = new ProfileStore(_dir).Load("bad")!;
        Assert.Null(loaded.HostFilesPinnedCertificate);
        Assert.Equal("http://h/zosmf", loaded.HostFilesUrl);
    }

    private IEnumerable<string?> FileNames() => Directory.GetFiles(_dir).Select(Path.GetFileName).Order();
}
