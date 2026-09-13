// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Profiles;

namespace LizTerm.Core.Tests.Profiles;

public class RecentHostsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-recent-" + Guid.NewGuid().ToString("N"));

    private string File_ => Path.Combine(_dir, "recent-hosts.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void A_missing_file_loads_as_an_empty_list()
    {
        Assert.Empty(new RecentHostsStore(File_).Load().Entries);
    }

    [Fact]
    public void Save_then_Load_round_trips_the_entries_in_order()
    {
        var store = new RecentHostsStore(File_);
        store.Save(RecentHosts.From(["L:mvs.example:992", "tk5:3270"]));

        Assert.Equal(["L:mvs.example:992", "tk5:3270"], store.Load().Entries);
        Assert.False(File.Exists(File_ + ".tmp"));
    }

    [Fact]
    public void A_file_that_is_not_json_loads_as_an_empty_list()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, "not json");

        Assert.Empty(new RecentHostsStore(File_).Load().Entries);
    }

    [Fact]
    public void A_hand_edited_file_is_held_to_the_lists_own_rules()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, """{ "hosts": [ " a:23 ", null, "", "A:23", "b:23" ] }""");

        Assert.Equal(["a:23", "b:23"], new RecentHostsStore(File_).Load().Entries);
    }

    /// <summary>A history that cannot be written is not worth a crash: TrySave reports it and carries on.</summary>
    [Fact]
    public void TrySave_answers_the_failure_rather_than_throwing()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "not-a-directory");
        File.WriteAllText(blocker, "");

        var message = new RecentHostsStore(Path.Combine(blocker, "recent-hosts.json")).TrySave(RecentHosts.From(["a:23"]));

        Assert.NotNull(message);
    }
}
