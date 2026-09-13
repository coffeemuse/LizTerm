// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Nodes;
using LizTerm.Core.Settings;

namespace LizTerm.Core.Tests.Settings;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lizterm-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");
    private SettingsStore Store => new(FilePath);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void WriteFile(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, content);
    }

    private JsonObject ReadFile() => JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();

    [Fact]
    public void A_missing_file_loads_every_default_and_creates_nothing()
    {
        Assert.Equal(new AppSettings(), Store.Load());
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Update_then_Load_round_trips_and_writes_the_enum_by_name()
    {
        Store.Update(s => s with { Crosshair = CrosshairMode.Both, Blink = false });

        Assert.Equal(new AppSettings(CrosshairMode.Both, Blink: false), Store.Load());
        Assert.Equal("Both", ReadFile()["crosshair"]!.GetValue<string>());
    }

    [Fact]
    public void The_bell_fields_round_trip_and_the_sound_is_written_by_name()
    {
        var store = new SettingsStore(FilePath);

        store.Update(s => s with { VisualBell = false, BellSound = BellSound.SystemAlert });

        Assert.Equal(new AppSettings(VisualBell: false, BellSound: BellSound.SystemAlert), store.Load());
        Assert.Contains("\"bellSound\": \"SystemAlert\"", File.ReadAllText(FilePath));
    }

    [Fact]
    public void The_keypad_fields_round_trip_and_the_dock_is_written_by_name()
    {
        Store.Update(s => s with { Keypad = true, KeypadDock = KeypadDock.Right });

        Assert.Equal(new AppSettings(Keypad: true, KeypadDock: KeypadDock.Right), Store.Load());
        Assert.Equal("Right", ReadFile()["keypadDock"]!.GetValue<string>());
    }

    /// <summary>The status-bar tags flag (#93): off by default, so a settings file from before it reads as off.</summary>
    [Fact]
    public void The_status_bar_tags_flag_round_trips_and_defaults_off()
    {
        Assert.False(Store.Load().ShowTagsInStatusBar);

        Store.Update(s => s with { ShowTagsInStatusBar = true });

        Assert.Equal(new AppSettings(ShowTagsInStatusBar: true), Store.Load());
        Assert.True(ReadFile()["showTagsInStatusBar"]!.GetValue<bool>());
    }

    [Fact]
    public void A_first_change_writes_only_that_key()
    {
        Store.Update(s => s with { Blink = false });

        Assert.Equal(["blink"], ReadFile().Select(p => p.Key));
    }

    [Fact]
    public void Update_returns_what_it_wrote()
    {
        var written = Store.Update(s => s with { Crosshair = CrosshairMode.Vertical });

        Assert.Equal(new AppSettings(CrosshairMode.Vertical), written);
    }

    [Fact]
    public void A_file_with_a_bad_value_loads_the_rest_and_the_next_update_repairs_it()
    {
        WriteFile("""{"crosshair":"Diagonal","blink":false}""");
        Assert.Equal(new AppSettings(Blink: false), Store.Load());

        Store.Update(s => s with { Blink = true });

        Assert.Equal(new AppSettings(), Store.Load());
        Assert.Equal("None", ReadFile()["crosshair"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1, 2, 3]")]
    [InlineData("""{"blink":false,"blink":true}""")]
    public void A_file_that_is_not_a_json_object_loads_defaults_and_refuses_to_be_overwritten(string content)
    {
        WriteFile(content);
        Assert.Equal(new AppSettings(), Store.Load());

        var ex = Assert.Throws<InvalidDataException>(() => Store.Update(s => s with { Blink = false }));

        Assert.Contains(FilePath, ex.Message);
        Assert.Contains("fix or delete it", ex.Message);
        Assert.Equal(content, File.ReadAllText(FilePath));
        Assert.Equal(["settings.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    /// <summary>Two LizTerm processes: the second saved a crosshair between the first's load and its save. The
    /// first changes only blink, so the crosshair it never touched survives — ProfileStore.Update's rule.</summary>
    [Fact]
    public void Update_applies_the_change_to_what_is_on_disk_now()
    {
        var first = Store;
        first.Load();
        new SettingsStore(FilePath).Update(s => s with { Crosshair = CrosshairMode.Horizontal });

        first.Update(s => s with { Blink = false });

        Assert.Equal(new AppSettings(CrosshairMode.Horizontal, Blink: false), first.Load());
    }

    [Fact]
    public void An_unknown_key_survives_an_update()
    {
        WriteFile("""{"fontSize":14}""");

        Store.Update(s => s with { Blink = false });

        var doc = ReadFile();
        Assert.Equal(14, doc["fontSize"]!.GetValue<int>());
        Assert.False(doc["blink"]!.GetValue<bool>());
    }

    [Fact]
    public void Update_never_leaves_a_temp_file_behind()
    {
        Store.Update(s => s with { Blink = false });
        Store.Update(s => s with { Blink = true });

        Assert.Equal(["settings.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
        Assert.Equal("""{"blink":true}""", ReadFile().ToJsonString());
    }

    [Fact]
    public void The_file_is_written_indented_for_hand_editing()
    {
        Store.Update(s => s with { Blink = false });

        Assert.Contains("\n", File.ReadAllText(FilePath));
    }

    /// <summary>The catch in Write: a failed rename must not strand settings.json.tmp beside the file. A
    /// directory at the file's own path makes File.Move throw, while File.Exists stays false so the re-read
    /// passes. Which exception depends on the platform — macOS and Linux answer "Is a directory" as an
    /// IOException, Windows maps MoveFileEx's ERROR_ACCESS_DENIED to UnauthorizedAccessException — and both are
    /// the store's documented propagations (SettingsViewModel catches both), so the assertion is on the contract,
    /// not on one platform's choice.</summary>
    [Fact]
    public void A_failed_write_leaves_no_temp_file_behind()
    {
        Directory.CreateDirectory(FilePath);

        var ex = Record.Exception(() => Store.Update(s => s with { Blink = false }));

        Assert.True(ex is IOException or UnauthorizedAccessException, $"expected an IO or access exception, got {ex?.GetType().Name ?? "none"}");

        Assert.DoesNotContain("settings.json.tmp", Directory.GetFiles(_dir).Select(Path.GetFileName));
    }
}
