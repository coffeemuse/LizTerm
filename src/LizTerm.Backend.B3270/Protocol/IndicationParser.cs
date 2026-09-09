// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json;

namespace LizTerm.Backend.B3270.Protocol;

public static class IndicationParser
{
    public static bool TryParse(string line, out Indication indication)
    {
        indication = null!;
        if (string.IsNullOrWhiteSpace(line)) return false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            using var props = root.EnumerateObject();
            if (!props.MoveNext()) return false;
            var first = props.Current;
            // Validate body kind: initialize requires Array, all others require Object
            if (first.Name == "initialize")
            {
                if (first.Value.ValueKind != JsonValueKind.Array) return false;
            }
            else
            {
                if (first.Value.ValueKind != JsonValueKind.Object) return false;
            }
            indication = Parse(first.Name, first.Value);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static Indication Parse(string name, JsonElement body) => name switch
    {
        "initialize" => new InitializeIndication(ParseArray(body)),
        "hello" => new HelloIndication(Str(body, "version") ?? "", Str(body, "build") ?? ""),
        "screen-mode" => new ScreenModeIndication(Int(body, "model") ?? 0, Int(body, "rows") ?? 0, Int(body, "columns") ?? 0,
            Bool(body, "color") ?? false, Bool(body, "oversize") ?? false, Bool(body, "extended") ?? false),
        "erase" => new EraseIndication(Int(body, "logical-rows"), Int(body, "logical-columns"), Str(body, "fg"), Str(body, "bg")),
        "screen" => ParseScreen(body),
        "oia" => new OiaIndication(Str(body, "field") ?? "", Scalar(body, "value")),
        "connection" => new ConnectionIndication(Str(body, "state") ?? "", Str(body, "host"), Str(body, "cause")),
        "tls" => new TlsIndication(Bool(body, "secure") ?? false, Bool(body, "verified"), Str(body, "session"), Str(body, "host-cert")),
        "tls-hello" => new TlsHelloIndication(Bool(body, "supported") ?? false, Str(body, "provider"), StringList(body, "options")),
        "run-result" => new RunResultIndication(Str(body, "r-tag"), Bool(body, "success") ?? false,
            StringList(body, "text"), BoolList(body, "text-err"), Bool(body, "abort") ?? false),
        "popup" => new PopupIndication(Str(body, "type") ?? "", Str(body, "text") ?? "", Bool(body, "retrying") ?? false, Bool(body, "error") ?? false),
        "ui-error" => new UiErrorIndication(Bool(body, "fatal") ?? false, Str(body, "text") ?? "", Str(body, "operation"), Str(body, "member")),
        "ft" => new FtIndication(Str(body, "state") ?? "", Bool(body, "success"), Str(body, "text"), Long(body, "bytes"), Str(body, "cause")),
        "setting" => new SettingIndication(Str(body, "name") ?? "", Scalar(body, "value"), Str(body, "cause")),
        _ => new UnknownIndication(name),
    };

    private static IReadOnlyList<Indication> ParseArray(JsonElement array)
    {
        var items = new List<Indication>();
        if (array.ValueKind != JsonValueKind.Array) return items;
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            using var props = element.EnumerateObject();
            if (!props.MoveNext()) continue;
            // Skip if the indication body is not an object (except initialize which expects Array)
            var propName = props.Current.Name;
            var propValue = props.Current.Value;
            if (propName == "initialize")
            {
                if (propValue.ValueKind != JsonValueKind.Array) continue;
            }
            else
            {
                if (propValue.ValueKind != JsonValueKind.Object) continue;
            }
            items.Add(Parse(propName, propValue));
        }
        return items;
    }

    private static ScreenIndication ParseScreen(JsonElement body)
    {
        CursorIndication? cursor = null;
        if (body.TryGetProperty("cursor", out var c) && c.ValueKind == JsonValueKind.Object)
            cursor = new CursorIndication(Bool(c, "enabled") ?? false, Int(c, "row"), Int(c, "column"));

        List<RowIndication>? rows = null;
        if (body.TryGetProperty("rows", out var rowsElement) && rowsElement.ValueKind == JsonValueKind.Array)
        {
            rows = [];
            foreach (var row in rowsElement.EnumerateArray())
            {
                var changes = new List<ChangeIndication>();
                if (row.TryGetProperty("changes", out var changesElement) && changesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ch in changesElement.EnumerateArray())
                        changes.Add(new ChangeIndication(Int(ch, "column") ?? 1, Str(ch, "text"), Int(ch, "count"),
                            Str(ch, "fg"), Str(ch, "bg"), Str(ch, "gr")));
                }
                rows.Add(new RowIndication(Int(row, "row") ?? 1, changes));
            }
        }
        return new ScreenIndication(cursor, rows);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;

    private static bool? Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean()
            : null;

    /// <summary>String, number, or boolean as text; null when absent or JSON null.</summary>
    private static string? Scalar(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }

    private static IReadOnlyList<string> StringList(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return [];
        return v.EnumerateArray().Select(x => x.ValueKind switch
        {
            JsonValueKind.String => x.GetString() ?? "",
            JsonValueKind.Number => x.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        }).ToList();
    }

    private static IReadOnlyList<bool> BoolList(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return [];
        return v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.True).ToList();
    }
}
