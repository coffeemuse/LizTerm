// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Nodes;
using Avalonia.Input;
using LizTerm.App.Keyboard;
using LizTerm.Core.Session;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Keyboard;

/// <summary>The user's differences from the default (editable keymap spec §3.1–3.3): parsed, sparse, composed.</summary>
public class KeymapOverlayTests
{
    private static readonly KeyChord CtrlHome = new(Key.Home, KeyModifiers.Control);
    private static readonly KeyChord CtrlOpenBracket = new(Key.OemOpenBrackets, KeyModifiers.Control);
    private static readonly KeyChord Backspace = new(Key.Back);
    private static readonly KeymapAction Pa1 = new KeymapAction.SendKey(TerminalKey.PA1);
    private static readonly KeymapAction Pa2 = new KeymapAction.SendKey(TerminalKey.PA2);

    private static KeymapFile File(string bindings) =>
        KeymapFile.FromJson(JsonNode.Parse("{\"bindings\": " + bindings + "}")!.AsObject());

    [Fact]
    public void Parse_keeps_what_it_can_read_and_carries_the_rest()
    {
        var overlay = KeymapOverlay.Parse(File("""{"ctrl+home": "PA1", "Cmd+K": "PA1", "F1": "PF99", "F2": 7}"""));

        Assert.Equal(Pa1, overlay.Entries[CtrlHome]);
        Assert.Single(overlay.Entries);
        Assert.Equal(["Cmd+K", "F1"], overlay.Ignored.Bindings.Keys.Order());
        Assert.Equal(["F2"], overlay.Ignored.Unreadable.Keys);
        Assert.Equal(3, overlay.IgnoredCount);
    }

    [Fact]
    public void Parse_says_which_entry_it_skipped_and_why()
    {
        var overlay = KeymapOverlay.Parse(File("""{"Cmd+K": "PA1", "F1": "PF99", "F2": 7, "F3": {"text": ""}}"""));

        Assert.Equal(
            [
                new KeymapSkip("Cmd+K", KeymapSkipReason.UnknownChord),
                new KeymapSkip("F1", KeymapSkipReason.UnknownKey, "PF99"),
                new KeymapSkip("F2", KeymapSkipReason.WrongShape),
                new KeymapSkip("F3", KeymapSkipReason.EmptyText),
            ],
            overlay.Skipped);
    }

    [Fact]
    public void An_unreadable_chord_is_named_before_its_action_is_judged()
    {
        // Both halves are wrong; the chord is what the user reads first, so it is the reason given.
        var overlay = KeymapOverlay.Parse(File("""{"Cmd+K": "PF99"}"""));

        Assert.Equal([new KeymapSkip("Cmd+K", KeymapSkipReason.UnknownChord)], overlay.Skipped);
    }

    [Fact]
    public void Nothing_is_skipped_when_every_entry_reads()
    {
        Assert.Empty(KeymapOverlay.Parse(File("""{"ctrl+home": "PA1"}""")).Skipped);
    }

    [Fact]
    public void ToFile_writes_its_entries_in_the_canonical_spelling_and_the_ignored_ones_as_they_were()
    {
        var overlay = KeymapOverlay.Parse(File("""{"ctrl+home": "PA1", "F1": "PF99", "F2": 7}"""));

        var file = overlay.ToFile();

        Assert.Equal(new KeymapEntry.SendKey("PA1"), file.Bindings["Ctrl+Home"]);
        Assert.Equal(new KeymapEntry.SendKey("PF99"), file.Bindings["F1"]);
        Assert.Equal("7", file.Unreadable["F2"]!.ToJsonString());
        Assert.Equal(2, file.Bindings.Count);
    }

    [Fact]
    public void Binding_a_chord_to_its_default_leaves_no_entry()
    {
        var overlay = KeymapOverlay.Empty.Bind(CtrlHome, Pa1).Bind(CtrlHome, Pa2);

        Assert.Empty(overlay.Entries);
    }

    [Fact]
    public void Unbinding_a_default_is_kept_and_unbinding_a_chord_the_default_lacks_is_not()
    {
        var overlay = KeymapOverlay.Empty.Unbind(CtrlHome).Unbind(new KeyChord(Key.X, KeyModifiers.Control));

        Assert.Same(KeymapAction.Unbound.Instance, Assert.Single(overlay.Entries).Value);
        Assert.Equal(CtrlHome, overlay.Entries.Keys.Single());
    }

    [Fact]
    public void Backspace_is_kept_even_at_the_erasing_default()
    {
        var overlay = KeymapOverlay.Empty.Bind(Backspace, new KeymapAction.SendKey(TerminalKey.Erase));

        Assert.Equal(new KeymapAction.SendKey(TerminalKey.Erase), overlay.Entries[Backspace]);
        Assert.Equal(TerminalKey.Erase, Send(overlay.Compose(destructiveBackspace: false), Backspace));
    }

    [Fact]
    public void Without_a_Backspace_entry_the_profile_decides()
    {
        Assert.Equal(TerminalKey.Backspace, Send(KeymapOverlay.Empty.Compose(destructiveBackspace: false), Backspace));
        Assert.Equal(TerminalKey.Erase, Send(KeymapOverlay.Empty.Compose(destructiveBackspace: true), Backspace));
    }

    [Fact]
    public void Compose_applies_a_binding_an_unbinding_and_a_move_from_text_to_key()
    {
        var overlay = KeymapOverlay.Empty
            .Bind(CtrlHome, Pa1)
            .Unbind(new KeyChord(Key.D2, KeyModifiers.Alt))
            .Bind(CtrlOpenBracket, Pa1);

        var map = overlay.Compose(destructiveBackspace: true);

        Assert.Equal(TerminalKey.PA1, Send(map, CtrlHome));
        Assert.False(map.TryMap(new KeyChord(Key.D2, KeyModifiers.Alt), out _));
        Assert.Equal(TerminalKey.PA1, Send(map, CtrlOpenBracket));
        Assert.False(map.TryText(CtrlOpenBracket, out _));
    }

    [Fact]
    public void A_text_binding_replaces_a_key_and_a_text_default_stays_sparse()
    {
        var overlay = KeymapOverlay.Empty
            .Bind(CtrlHome, new KeymapAction.TypeText("€"))
            .Bind(CtrlOpenBracket, new KeymapAction.TypeText("¬"));

        Assert.Single(overlay.Entries);
        var map = overlay.Compose(destructiveBackspace: true);
        Assert.False(map.TryMap(CtrlHome, out _));
        Assert.True(map.TryText(CtrlHome, out var text));
        Assert.Equal("€", text);
    }

    [Fact]
    public void Cleared_drops_everything_ignored_entries_included()
    {
        var overlay = KeymapOverlay.Parse(File("""{"Ctrl+Home": "PA1", "F2": 7}""")).Cleared();

        Assert.Empty(overlay.Entries);
        Assert.Equal(0, overlay.IgnoredCount);
        Assert.Equal("""{"bindings":{}}""", overlay.ToFile().ToJson().ToJsonString());
    }

    [Fact]
    public void ToFile_drops_an_ignored_entry_for_a_chord_the_user_has_bound()
    {
        var overlay = KeymapOverlay.Parse(File("""{"F1": "PF99", "f1": "PF1"}"""));

        var file = overlay.ToFile();

        Assert.Equal(new KeymapEntry.SendKey("PF1"), file.Bindings["F1"]);
        Assert.Single(file.Bindings);
    }

    [Fact]
    public void Binding_a_chord_whose_file_entry_was_unreadable_replaces_it_on_save()
    {
        var overlay = KeymapOverlay.Parse(File("""{"F1": "PF99"}"""));
        Assert.Equal(1, overlay.IgnoredCount);

        var bound = overlay.Bind(new KeyChord(Key.F1), new KeymapAction.SendKey(TerminalKey.PF2));
        var file = bound.ToFile();

        Assert.Equal(new KeymapEntry.SendKey("PF2"), file.Bindings["F1"]);
        Assert.Single(file.Bindings);
    }

    [Fact]
    public void A_chord_whose_spelling_never_parses_back_is_written_once()
    {
        var cmdK = new KeyChord(Key.K, KeyModifiers.Meta);
        var saved = KeymapOverlay.Empty.Bind(cmdK, Pa1).ToFile();
        var reloaded = KeymapOverlay.Parse(saved);
        Assert.Equal(1, reloaded.IgnoredCount);

        var file = reloaded.Bind(cmdK, Pa1).ToFile();

        Assert.Equal(new KeymapEntry.SendKey("PA1"), file.Bindings["Cmd+K"]);
        Assert.Single(file.Bindings);
    }

    [Fact]
    public void Two_spellings_of_one_chord_are_one_entry_and_the_later_wins()
    {
        var overlay = KeymapOverlay.Parse(File("""{"ctrl+home": "PA1", "Ctrl+Home": "PA3"}"""));

        Assert.Single(overlay.Entries);
        Assert.Equal(new KeymapAction.SendKey(TerminalKey.PA3), overlay.Entries[CtrlHome]);
        Assert.Equal(0, overlay.IgnoredCount);
    }

    private static TerminalKey Send(Keymap map, KeyChord chord)
    {
        Assert.True(map.TryMap(chord, out var key));
        return key;
    }
}
