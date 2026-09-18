// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;

namespace LizTerm.Backend.Mvsmf.Tests;

public class FixtureTests
{
    [Fact]
    public async Task A_fixture_loads_its_status_type_and_body()
    {
        using var response = Fixture.Load("info-200");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("\"zosmf_version\"", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_binary_body_keeps_every_byte()
    {
        var body = Fixture.Body("read-binary-jes2");
        Assert.Equal(0, body.Length % 80);
        Assert.Equal(new byte[] { 0x61, 0x61, 0xD1, 0xC5, 0xE2, 0xF2 }, body[..6]);
    }

    [Fact]
    public void No_fixture_holds_credentials_or_a_session_token()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.http"))
        {
            var text = File.ReadAllText(file);
            // The body is the host's own words, and a failed login says "password"; anything LizTerm sent or was
            // given (a credential, a token) could only be in the header block.
            var head = text[..text.IndexOf("\n\n", StringComparison.Ordinal)];
            Assert.DoesNotContain("Authorization", head, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", head, StringComparison.OrdinalIgnoreCase);
            foreach (var line in head.Split('\n').Where(l => l.StartsWith("Set-Cookie:", StringComparison.OrdinalIgnoreCase)))
                Assert.StartsWith("Set-Cookie: LtpaToken2=<token>;", line, StringComparison.OrdinalIgnoreCase);
        }
    }
}
