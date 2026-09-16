// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace LizTerm.Backend.Mvsmf.Tests;

/// <summary>Loads a recorded exchange (see Fixtures/README.md) as the response a handler returns.</summary>
internal static class Fixture
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transfer-Encoding", "Content-Length", "Connection",
    };

    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".http");

    public static byte[] Body(string name) => Split(File.ReadAllBytes(Path(name))).Body;

    public static HttpResponseMessage Load(string name)
    {
        var (head, body) = Split(File.ReadAllBytes(Path(name)));
        var lines = Encoding.ASCII.GetString(head).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var status = int.Parse(lines[0].Split(' ')[1]);
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(body) };
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            var header = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (Ignored.Contains(header)) continue;
            if (header.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);
            else
                response.Headers.TryAddWithoutValidation(header, value);
        }
        return response;
    }

    private static (byte[] Head, byte[] Body) Split(byte[] file)
    {
        var separator = file.AsSpan().IndexOf("\n\n"u8);
        if (separator < 0) throw new InvalidDataException("A fixture needs a blank line after its headers.");
        return (file[..separator], file[(separator + 2)..]);
    }
}
