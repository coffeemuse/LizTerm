// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;
using System.Text.Json.Serialization;

namespace LizTerm.Core.Session;

/// <summary>A <see cref="TerminalDisplay"/> written by name, read leniently: a name this build does not know, a
/// number, or any other token reads as <see cref="TerminalDisplay.Color"/>. JsonStringEnumConverter would throw
/// on an unknown name and accept any integer, and ProfileStore.Read catches JsonException and SKIPS the whole
/// file, so one mistyped value, or a kind of display a later build added, would make the saved session vanish
/// from the list; here it costs only the display, which falls to the colour default (#123).</summary>
public sealed class TerminalDisplayJsonConverter : JsonConverter<TerminalDisplay>
{
    public override TerminalDisplay Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse<TerminalDisplay>(reader.GetString(), ignoreCase: true, out var display)
            && Enum.IsDefined(display))
            return display;
        // A scalar is already complete and Skip is a no-op; an object or an array is stepped over.
        reader.Skip();
        return TerminalDisplay.Color;
    }

    public override void Write(Utf8JsonWriter writer, TerminalDisplay value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
