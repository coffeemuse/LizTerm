// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;
using System.Text.Json.Serialization;

namespace LizTerm.Core.Session;

/// <summary>A <see cref="TagSet"/> as a JSON array of strings. Lenient on purpose, in both directions:
/// <see cref="TagSet.From"/> repairs duplicates, blanks, '#' prefixes and an over-long list, and a value that
/// is not an array at all yields an empty set rather than a JsonException. The leniency matters because
/// ProfileStore.Read catches JsonException and SKIPS the whole file — so a strict converter would turn one
/// mistyped key into a saved session that has silently vanished from the list.</summary>
public sealed class TagSetJsonConverter : JsonConverter<TagSet>
{
    public override TagSet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return TagSet.From([reader.GetString()!]);
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            // A scalar is already complete and Skip is a no-op; an object or a nested array is stepped over.
            reader.Skip();
            return TagSet.Empty;
        }

        var names = new List<string>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;
            if (reader.TokenType == JsonTokenType.String) names.Add(reader.GetString()!);
            else reader.Skip();
        }
        return TagSet.From(names);
    }

    public override void Write(Utf8JsonWriter writer, TagSet value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var name in value.Names) writer.WriteStringValue(name);
        writer.WriteEndArray();
    }
}
