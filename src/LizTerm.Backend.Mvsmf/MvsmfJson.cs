// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;
using System.Text.Json.Serialization;

namespace LizTerm.Backend.Mvsmf;

internal sealed record MvsmfInfo(
    [property: JsonPropertyName("zosmf_version")] string? ZosmfVersion,
    [property: JsonPropertyName("zosmf_full_version")] string? ZosmfFullVersion,
    [property: JsonPropertyName("zos_version")] string? ZosVersion);

/// <summary>mvsMF sends every attribute as a string ("lrecl":"80"); z/OSMF may send numbers, so attribute fields
/// read either.</summary>
internal sealed record MvsmfDataset(
    [property: JsonPropertyName("dsname")] string? Dsname,
    [property: JsonPropertyName("dsorg"), JsonConverter(typeof(LenientStringConverter))] string? Dsorg,
    [property: JsonPropertyName("recfm"), JsonConverter(typeof(LenientStringConverter))] string? Recfm,
    [property: JsonPropertyName("lrecl"), JsonConverter(typeof(LenientStringConverter))] string? Lrecl,
    [property: JsonPropertyName("blksz"), JsonConverter(typeof(LenientStringConverter))] string? Blksz,
    [property: JsonPropertyName("vol"), JsonConverter(typeof(LenientStringConverter))] string? Vol);

internal sealed record MvsmfDatasetList(
    [property: JsonPropertyName("items")] List<MvsmfDataset>? Items,
    [property: JsonPropertyName("moreRows")] bool? MoreRows);

internal sealed record MvsmfMember([property: JsonPropertyName("member")] string? Member);

internal sealed record MvsmfMemberList(
    [property: JsonPropertyName("items")] List<MvsmfMember>? Items,
    [property: JsonPropertyName("moreRows")] bool? MoreRows);

internal sealed record MvsmfError(
    [property: JsonPropertyName("rc")] int? Rc,
    [property: JsonPropertyName("category")] int? Category,
    [property: JsonPropertyName("reason")] int? Reason,
    [property: JsonPropertyName("message")] string? Message);

internal sealed class LenientStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.Number => reader.TryGetInt64(out var whole) ? whole.ToString(System.Globalization.CultureInfo.InvariantCulture) : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
        JsonTokenType.True => "true",
        JsonTokenType.False => "false",
        _ => SkipAndNull(ref reader),
    };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) => writer.WriteStringValue(value);

    private static string? SkipAndNull(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }
}

[JsonSerializable(typeof(MvsmfInfo))]
[JsonSerializable(typeof(MvsmfDatasetList))]
[JsonSerializable(typeof(MvsmfMemberList))]
[JsonSerializable(typeof(MvsmfError))]
internal partial class MvsmfJsonContext : JsonSerializerContext;
