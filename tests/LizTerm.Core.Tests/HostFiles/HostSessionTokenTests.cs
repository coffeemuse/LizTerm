// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.Core.Tests.HostFiles;

public class HostSessionTokenTests
{
    [Fact]
    public void ToString_never_shows_the_token()
    {
        var token = new HostSessionToken("LtpaToken2-abc123==");
        Assert.Equal("LtpaToken2-abc123==", token.Value);
        Assert.DoesNotContain("abc123", token.ToString());
    }

    [Fact]
    public void A_request_names_the_rejected_token_without_its_value()
    {
        var rejected = new HostSessionToken("LtpaToken2-abc123==");
        var request = new HostTokenRequest(rejected);
        Assert.Same(rejected, request.Rejected);
        Assert.DoesNotContain("abc123", request.ToString());
        Assert.Null(new HostTokenRequest(null).Rejected);
    }
}
