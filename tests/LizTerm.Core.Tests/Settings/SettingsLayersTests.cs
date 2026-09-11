// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Nodes;
using LizTerm.Core.Settings;

namespace LizTerm.Core.Tests.Settings;

/// <summary>The two rules of the settings file, with no disk: how layers merge and which keys a save writes.</summary>
public class SettingsLayersTests
{
    private static JsonObject Doc(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void A_later_layer_wins_per_key_and_an_absent_key_is_the_default()
    {
        var merged = SettingsLayers.Merge([Doc("""{"crosshair":"Both","blink":false}"""), Doc("""{"crosshair":"Vertical"}""")]);

        Assert.Equal(new AppSettings(CrosshairMode.Vertical, Blink: false), SettingsLayers.Read(merged));
    }

    [Fact]
    public void No_layers_at_all_read_as_every_default()
    {
        Assert.Equal(new AppSettings(), SettingsLayers.Read(SettingsLayers.Merge([])));
    }

    /// <summary>One hand-edited typo costs that key alone, not the whole file: the other keys keep their values.</summary>
    [Fact]
    public void A_key_whose_value_will_not_read_is_dropped_and_the_rest_are_kept()
    {
        Assert.Equal(new AppSettings(Blink: false), SettingsLayers.Read(Doc("""{"crosshair":"Diagonal","blink":false}""")));
        Assert.Equal(new AppSettings(CrosshairMode.Both), SettingsLayers.Read(Doc("""{"crosshair":"Both","blink":"yes"}""")));
    }

    [Fact]
    public void An_unknown_key_is_ignored_on_read()
    {
        Assert.Equal(new AppSettings(Blink: false), SettingsLayers.Read(Doc("""{"blink":false,"fontSize":14}""")));
    }

    [Fact]
    public void The_user_document_holds_a_changed_key_and_omits_an_untouched_one()
    {
        var doc = SettingsLayers.UserDocument(new AppSettings(Blink: false), new AppSettings(), existing: null);

        Assert.Equal("""{"blink":false}""", doc.ToJsonString());
    }

    /// <summary>Set once, it stays written, even back to the base value: that was a choice, and a later change of
    /// built-in default must not move it.</summary>
    [Fact]
    public void A_key_already_in_the_user_document_stays_pinned_when_set_back_to_the_base_value()
    {
        var doc = SettingsLayers.UserDocument(new AppSettings(), new AppSettings(), Doc("""{"crosshair":"Both"}"""));

        Assert.Equal("""{"crosshair":"None"}""", doc.ToJsonString());
    }

    [Fact]
    public void A_known_key_with_a_bad_value_is_rewritten_from_the_record()
    {
        var doc = SettingsLayers.UserDocument(new AppSettings(Blink: false), new AppSettings(), Doc("""{"crosshair":"Diagonal"}"""));

        Assert.Equal("""{"crosshair":"None","blink":false}""", doc.ToJsonString());
    }

    [Fact]
    public void An_unknown_key_is_copied_through_verbatim()
    {
        var doc = SettingsLayers.UserDocument(new AppSettings(), new AppSettings(), Doc("""{"fontSize":14,"theme":{"name":"green"}}"""));

        Assert.Equal("""{"fontSize":14,"theme":{"name":"green"}}""", doc.ToJsonString());
    }

    /// <summary>The base beneath the user file (a future system layer) says Both; the user chose None, which is
    /// also the built-in default. It differs from the base, so it is written, or the choice would be lost.</summary>
    [Fact]
    public void A_difference_from_the_base_is_written_even_when_it_is_the_built_in_default()
    {
        var doc = SettingsLayers.UserDocument(new AppSettings(), new AppSettings(CrosshairMode.Both), existing: null);

        Assert.Equal("""{"crosshair":"None"}""", doc.ToJsonString());
    }

    [Fact]
    public void Merge_does_not_share_nodes_with_its_inputs()
    {
        var layer = Doc("""{"blink":false}""");
        var merged = SettingsLayers.Merge([layer]);
        merged["blink"] = true;

        Assert.False(layer["blink"]!.GetValue<bool>());
    }
}
