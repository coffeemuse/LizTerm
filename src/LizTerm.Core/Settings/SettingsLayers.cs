// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;
using System.Text.Json.Nodes;

namespace LizTerm.Core.Settings;

/// <summary>The two rules of the settings file, kept pure so every ordering and pinning case is a plain unit
/// test: how layers merge into one record, and which keys a save writes back. SettingsStore does the file I/O
/// and calls these.</summary>
public static class SettingsLayers
{
    /// <summary>Shallow overlay of top-level keys, base first, later wins per key. Nested values are replaced
    /// whole, which is why AppSettings stays flat. Nodes are cloned: a JsonNode has one parent, and the inputs
    /// stay usable.</summary>
    public static JsonObject Merge(IEnumerable<JsonObject> layers)
    {
        var merged = new JsonObject();
        foreach (var layer in layers)
            foreach (var (key, value) in layer)
                merged[key] = value?.DeepClone();
        return merged;
    }

    /// <summary>The merged document as a record. A key whose value will not read — a crosshair that is not a
    /// mode name, a blink that is not a boolean — is dropped on its own and falls to its default; every other
    /// key keeps its value, so one hand-edited typo does not cost the whole file (and the next save repairs it,
    /// see UserDocument). Unknown keys are ignored, so an older build opens a newer file.</summary>
    public static AppSettings Read(JsonObject merged)
    {
        var readable = new JsonObject();
        foreach (var (key, value) in merged)
        {
            var probe = new JsonObject { [key] = value?.DeepClone() };
            try
            {
                JsonSerializer.Deserialize(probe, SettingsJsonContext.Default.AppSettings);
                readable[key] = value?.DeepClone();
            }
            catch (JsonException)
            {
                // This key alone is unreadable; leave it out.
            }
        }
        return JsonSerializer.Deserialize(readable, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
    }

    /// <summary>The sparse document a save writes: exactly the keys <paramref name="existing"/> already holds,
    /// plus every key whose value in <paramref name="next"/> differs from <paramref name="beneath"/> — the
    /// record the layers under the user file merge to. A known key takes its value from next, so a bad value
    /// heals on the first save; an unknown key is copied from existing verbatim, so a newer build's setting
    /// survives an older build saving. A key the user never touched stays absent and keeps following the
    /// default; one they set once stays pinned, even set back to the base value, because that was a choice.</summary>
    public static JsonObject UserDocument(AppSettings next, AppSettings beneath, JsonObject? existing)
    {
        var nextDoc = JsonSerializer.SerializeToNode(next, SettingsJsonContext.Default.AppSettings)!.AsObject();
        var beneathDoc = JsonSerializer.SerializeToNode(beneath, SettingsJsonContext.Default.AppSettings)!.AsObject();
        var document = new JsonObject();
        if (existing is not null)
        {
            foreach (var (key, value) in existing)
                document[key] = nextDoc.ContainsKey(key) ? nextDoc[key]?.DeepClone() : value?.DeepClone();
        }
        foreach (var (key, value) in nextDoc)
        {
            if (document.ContainsKey(key)) continue;
            if (!JsonNode.DeepEquals(value, beneathDoc[key])) document[key] = value?.DeepClone();
        }
        return document;
    }
}
