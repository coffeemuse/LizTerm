// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Nodes;
using LizTerm.Core.Profiles;
using LizTerm.Core.Settings;

namespace LizTerm.Core.Tests.Settings;

public class KeymapStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "keymap.json");
    private KeymapStore Store => new(FilePath);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void WriteFile(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, content);
    }

    private JsonObject ReadBindings() => JsonNode.Parse(File.ReadAllText(FilePath))!["bindings"]!.AsObject();

    private static KeymapFile Plus(KeymapFile file, string chord, KeymapEntry entry) =>
        new(file.Bindings.Append(KeyValuePair.Create(chord, entry)), file.Unreadable);

    [Fact]
    public void A_missing_file_loads_empty_and_creates_nothing()
    {
        Assert.Empty(Store.Load().Bindings);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Update_writes_and_Load_reads_it_back()
    {
        Store.Update(f => Plus(f, "Ctrl+Home", new KeymapEntry.SendKey("PA1")));

        Assert.Equal(new KeymapEntry.SendKey("PA1"), Store.Load().Bindings["Ctrl+Home"]);
        Assert.Equal("PA1", ReadBindings()["Ctrl+Home"]!.GetValue<string>());
    }

    [Fact]
    public void Update_applies_the_change_to_what_is_on_disk_now()
    {
        WriteFile("""{"bindings": {"Alt+2": null}}""");

        Store.Update(f => Plus(f, "Ctrl+Home", new KeymapEntry.SendKey("PA1")));

        Assert.Equal(["Alt+2", "Ctrl+Home"], ReadBindings().Select(p => p.Key));
    }

    [Fact]
    public void An_unreadable_entry_survives_an_update()
    {
        WriteFile("""{"bindings": {"F1": 7}}""");

        Store.Update(f => Plus(f, "Ctrl+Home", new KeymapEntry.SendKey("PA1")));

        Assert.Equal("7", ReadBindings()["F1"]!.ToJsonString());
    }

    [Fact]
    public void A_file_that_is_not_json_loads_empty_but_refuses_an_update()
    {
        WriteFile("not json");

        Assert.Empty(Store.Load().Bindings);
        var ex = Assert.Throws<InvalidDataException>(() => Store.Update(f => f));
        Assert.Contains("keymap.json", ex.Message);
        Assert.Equal("not json", File.ReadAllText(FilePath));
    }

    [Fact]
    public void The_default_file_is_keymap_json_beside_settings_json()
    {
        Assert.Equal(Path.Combine(Path.GetDirectoryName(AppPaths.SettingsFile())!, "keymap.json"), KeymapStore.DefaultFile());
    }

    [Fact]
    public void The_file_spells_a_chord_and_a_text_as_themselves()
    {
        Store.Update(f => Plus(Plus(f, "Ctrl+Home", new KeymapEntry.SendKey("PA1")), "Ctrl+D6", new KeymapEntry.TypeText("¬")));

        var text = File.ReadAllText(FilePath);

        Assert.Contains("\"Ctrl+Home\": \"PA1\"", text);
        Assert.Contains("\"text\": \"¬\"", text);
        Assert.DoesNotContain("\\u", text);
    }
}
