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
        Assert.Empty(Store.Load().File.Bindings);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Update_writes_and_Load_reads_it_back()
    {
        Store.Update(f => Plus(f, "Ctrl+Home", new KeymapEntry.SendKey("PA1")));

        Assert.Equal(new KeymapEntry.SendKey("PA1"), Store.Load().File.Bindings["Ctrl+Home"]);
        Assert.Equal("PA1", ReadBindings()["Ctrl+Home"]!.GetValue<string>());
    }

    [Fact]
    public void Update_keeps_the_files_other_top_level_keys()
    {
        WriteFile("""{"version": 2, "bindings": {"F2": "PA1"}}""");

        Store.Update(f => Plus(f, "Ctrl+Home", new KeymapEntry.SendKey("PA2")));

        Assert.Equal(2, JsonNode.Parse(File.ReadAllText(FilePath))!["version"]!.GetValue<int>());
    }

    [Fact]
    public void Update_refuses_a_bindings_that_is_not_an_object()
    {
        WriteFile("""{"bindings": ["F2"]}""");

        var ex = Assert.Throws<InvalidDataException>(() => Store.Update(f => f));

        Assert.Contains("keymap.json", ex.Message);
        Assert.Equal("""{"bindings": ["F2"]}""", File.ReadAllText(FilePath));
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

        Assert.Empty(Store.Load().File.Bindings);
        var ex = Assert.Throws<InvalidDataException>(() => Store.Update(f => f));
        Assert.Contains("keymap.json", ex.Message);
        Assert.Equal("not json", File.ReadAllText(FilePath));
    }

    // ---- why a file would not load (#168) -----------------------------------------------------------------------

    [Fact]
    public void A_missing_file_has_no_problem_to_report()
    {
        Assert.Null(Store.Load().Problem);
    }

    [Fact]
    public void A_file_with_no_bindings_key_is_simply_empty()
    {
        WriteFile("""{"version": 2}""");

        var load = Store.Load();

        Assert.Empty(load.File.Bindings);
        Assert.Null(load.Problem);
    }

    [Fact]
    public void A_file_that_is_not_json_reports_the_line_to_look_at()
    {
        WriteFile("{\n  \"bindings\": {\n    \"Ctrl+Q\": \"PF1\",\n  }\n}");

        Assert.Equal("keymap.json could not be read. It is not valid JSON. Line 4: the JSON object contains a trailing comma at the end.",
                     Store.Load().Problem);
    }

    [Fact]
    public void A_bindings_that_is_not_an_object_is_its_own_problem()
    {
        WriteFile("""{"bindings": ["F2"]}""");

        var load = Store.Load();

        Assert.Empty(load.File.Bindings);
        Assert.Equal("keymap.json could not be read. Its \"bindings\" is not a JSON object.", load.Problem);
    }

    /// <summary>A keymap that is there but will not open loses every binding too, so it is reported rather than
    /// read as "no file" (#168). A directory in the file's place makes the read throw on every platform.</summary>
    [Fact]
    public void A_file_that_will_not_open_at_all_is_reported_too()
    {
        Directory.CreateDirectory(FilePath);

        var load = Store.Load();

        Assert.Empty(load.File.Bindings);
        Assert.NotNull(load.Problem);
        Assert.StartsWith("keymap.json could not be read.", load.Problem);
    }

    [Fact]
    public void A_load_that_failed_leaves_the_file_alone()
    {
        WriteFile("not json");

        Store.Load();

        Assert.Equal("not json", File.ReadAllText(FilePath));
    }

    // ---- moving a broken file aside (#168) --------------------------------------------------------------------

    [Fact]
    public void MoveAside_keeps_the_broken_file_under_a_new_name_and_loads_the_defaults()
    {
        WriteFile("not json");

        var moved = Store.MoveAside();

        Assert.Equal(FilePath + ".bad", moved);
        Assert.Equal("not json", File.ReadAllText(moved!));
        Assert.False(File.Exists(FilePath));
        Assert.Empty(Store.Load().File.Bindings);
        Assert.Null(Store.Load().Problem);
    }

    [Fact]
    public void MoveAside_over_an_earlier_broken_file_keeps_the_newer_one()
    {
        WriteFile("older");
        Store.MoveAside();
        WriteFile("newer");

        Store.MoveAside();

        Assert.Equal("newer", File.ReadAllText(FilePath + ".bad"));
    }

    [Fact]
    public void MoveAside_with_no_file_moves_nothing()
    {
        Assert.Null(Store.MoveAside());
        Assert.False(File.Exists(FilePath + ".bad"));
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
