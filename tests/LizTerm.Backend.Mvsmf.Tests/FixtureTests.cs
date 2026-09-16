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
    public void No_fixture_holds_credentials()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.http"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Authorization", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
