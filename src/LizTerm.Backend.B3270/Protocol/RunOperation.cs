// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace LizTerm.Backend.B3270.Protocol;

public static class RunOperation
{
    /// <summary>What a line carried, in a form that is safe to put on an error banner or paste into a bug report:
    /// each action's name, and the <em>size</em> of its arguments, never their values. A <c>String</c> action holds
    /// whatever the user typed — a password, at a logon screen — so no argument is ever quoted here, and no
    /// action is exempted on the grounds that its arguments look harmless today. The exact bytes live in the wire
    /// log, which the user guide already warns records everything typed (#139).</summary>
    public static string Describe(IReadOnlyList<B3270Action> actions)
    {
        if (actions.Count == 0) return "no actions";
        return string.Join(", ", actions.Select(a =>
            a.Args.Length == 0 ? a.Name : $"{a.Name}({a.Args.Sum(arg => arg.Length)} chars)"));
    }

    public static string Serialize(string tag, IReadOnlyList<B3270Action> actions)
    {
        var buffer = new ArrayBufferWriter<byte>();
        // Relaxed escaping keeps quotes as \" and leaves UTF-8 text alone (b3270 runs with -utf8).
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("run");
            writer.WriteString("r-tag", tag);
            writer.WriteStartArray("actions");
            foreach (var action in actions)
            {
                writer.WriteStartObject();
                writer.WriteString("action", action.Name);
                if (action.Args.Length > 0)
                {
                    writer.WriteStartArray("args");
                    foreach (var arg in action.Args) writer.WriteStringValue(arg);
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
