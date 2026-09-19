// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Nodes;
using LizTerm.Core.Settings;

namespace LizTerm.Core.Tests.Settings;

/// <summary>keymap.json's shape (editable keymap spec §3.1): three readable value shapes, everything else carried
/// through untouched.</summary>
public class KeymapFileTests
{
    private static JsonObject Doc(string bindings) => JsonNode.Parse("{\"bindings\": " + bindings + "}")!.AsObject();

    [Fact]
    public void Reads_a_key_name_a_text_object_and_null()
    {
        var file = KeymapFile.FromJson(Doc("""{"Ctrl+Home": "PA1", "Ctrl+D6": {"text": "¢"}, "Alt+2": null}"""));

        Assert.Equal(new KeymapEntry.SendKey("PA1"), file.Bindings["Ctrl+Home"]);
        Assert.Equal(new KeymapEntry.TypeText("¢"), file.Bindings["Ctrl+D6"]);
        Assert.Same(KeymapEntry.Unbound.Instance, file.Bindings["Alt+2"]);
        Assert.Empty(file.Unreadable);
    }

    [Fact]
    public void A_value_of_any_other_shape_is_unreadable_and_written_back_as_it_was()
    {
        var file = KeymapFile.FromJson(Doc("""{"F1": 7, "F2": {"key": "PF2"}, "F3": ["PF3"], "F4": {"text": "x", "more": 1}, "F5": "PF5"}"""));

        Assert.Equal(["F5"], file.Bindings.Keys);
        Assert.Equal(["F1", "F2", "F3", "F4"], file.Unreadable.Keys.Order());
        var written = file.ToJson()["bindings"]!.AsObject();
        Assert.Equal("7", written["F1"]!.ToJsonString());
        Assert.Equal("""{"key":"PF2"}""", written["F2"]!.ToJsonString());
        Assert.Equal("""["PF3"]""", written["F3"]!.ToJsonString());
        Assert.Equal("PF5", written["F5"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("""{"bindings": 5}""")]
    [InlineData("""{"bindings": ["F1"]}""")]
    [InlineData("""{"other": {}}""")]
    public void No_bindings_object_is_an_empty_file(string? json)
    {
        var document = json is null ? null : JsonNode.Parse(json)!.AsObject();

        var file = KeymapFile.FromJson(document);

        Assert.Empty(file.Bindings);
        Assert.Empty(file.Unreadable);
    }

    [Fact]
    public void ToJson_then_FromJson_round_trips_and_writes_chords_in_order()
    {
        var file = new KeymapFile(
            [
                KeyValuePair.Create("Tap:LeftCtrl", (KeymapEntry)new KeymapEntry.SendKey("Enter")),
                KeyValuePair.Create("Ctrl+D6", (KeymapEntry)new KeymapEntry.TypeText("¢")),
                KeyValuePair.Create("Alt+2", (KeymapEntry)KeymapEntry.Unbound.Instance),
            ],
            [KeyValuePair.Create("Zzz", (JsonNode?)JsonValue.Create(1))]);

        var json = file.ToJson();
        var back = KeymapFile.FromJson(json);

        Assert.Equal(["Alt+2", "Ctrl+D6", "Tap:LeftCtrl", "Zzz"], json["bindings"]!.AsObject().Select(p => p.Key));
        Assert.Equal(file.Bindings.OrderBy(b => b.Key), back.Bindings.OrderBy(b => b.Key));
        Assert.Equal("1", back.Unreadable["Zzz"]!.ToJsonString());
        Assert.Null(json["bindings"]!["Alt+2"]);
    }

    [Fact]
    public void Empty_writes_an_empty_bindings_object()
    {
        Assert.Equal("""{"bindings":{}}""", KeymapFile.Empty.ToJson().ToJsonString());
    }
}
