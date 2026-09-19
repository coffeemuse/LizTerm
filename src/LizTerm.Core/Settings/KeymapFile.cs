// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Nodes;

namespace LizTerm.Core.Settings;

/// <summary>keymap.json as a document: <c>{"bindings": {"&lt;chord&gt;": "&lt;key&gt;" | {"text": "…"} | null}}</c>
/// (editable keymap spec §3.1). Chords and key names are strings here; LizTerm.App parses them. A value of any
/// other shape is kept in Unreadable and written back verbatim, so one hand-edited mistake costs one binding and a
/// newer build's value survives an older build saving. Written with the chords in ordinal order, so a file diff is
/// stable. Immutable.</summary>
public sealed class KeymapFile
{
    private const string BindingsKey = "bindings";
    private const string TextKey = "text";

    public static KeymapFile Empty { get; } = new([], []);

    public IReadOnlyDictionary<string, KeymapEntry> Bindings { get; }

    /// <summary>Values this build cannot read, by chord spelling, cloned so the file's document stays untouched.</summary>
    public IReadOnlyDictionary<string, JsonNode?> Unreadable { get; }

    public KeymapFile(IEnumerable<KeyValuePair<string, KeymapEntry>> bindings, IEnumerable<KeyValuePair<string, JsonNode?>> unreadable)
    {
        Bindings = new Dictionary<string, KeymapEntry>(bindings);
        Unreadable = new Dictionary<string, JsonNode?>(unreadable);
    }

    /// <summary>Reads a document. No "bindings" object, or no document at all, is the empty file.</summary>
    public static KeymapFile FromJson(JsonObject? document)
    {
        if (document?[BindingsKey] is not JsonObject entries) return Empty;
        var bindings = new Dictionary<string, KeymapEntry>();
        var unreadable = new Dictionary<string, JsonNode?>();
        foreach (var (chord, value) in entries)
        {
            switch (value)
            {
                case null:
                    bindings[chord] = KeymapEntry.Unbound.Instance;
                    break;
                case JsonValue scalar when scalar.TryGetValue<string>(out var keyName):
                    bindings[chord] = new KeymapEntry.SendKey(keyName);
                    break;
                case JsonObject { Count: 1 } holder when holder[TextKey] is JsonValue textValue && textValue.TryGetValue<string>(out var text):
                    bindings[chord] = new KeymapEntry.TypeText(text);
                    break;
                default:
                    unreadable[chord] = value!.DeepClone();   // the null case is above; flow analysis does not carry it into default
                    break;
            }
        }
        return new(bindings, unreadable);
    }

    public JsonObject ToJson()
    {
        var entries = new JsonObject();
        var spellings = Bindings.Keys.Concat(Unreadable.Keys).Order(StringComparer.Ordinal);
        foreach (var chord in spellings)
        {
            entries[chord] = Bindings.TryGetValue(chord, out var entry)
                ? entry switch
                {
                    KeymapEntry.SendKey send => JsonValue.Create(send.KeyName),
                    KeymapEntry.TypeText type => new JsonObject { [TextKey] = type.Text },
                    _ => null,
                }
                : Unreadable[chord]?.DeepClone();
        }
        return new JsonObject { [BindingsKey] = entries };
    }
}
