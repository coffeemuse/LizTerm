// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfOptionsTests
{
    [Theory]
    [InlineData("http://10.42.37.209:8080", "http://10.42.37.209:8080/zosmf")]
    [InlineData(" http://10.42.37.209:8080/ ", "http://10.42.37.209:8080/zosmf")]
    [InlineData("http://h:8080/zosmf/", "http://h:8080/zosmf")]
    [InlineData("https://proxy.example/mvs/zosmf", "https://proxy.example/mvs/zosmf")]
    public void Normalizes_the_base_url(string text, string expected)
    {
        Assert.True(MvsmfOptions.TryNormalizeBaseUrl(text, out var url, out var error), error);
        Assert.Equal(expected, url!.ToString());
    }

    [Theory]
    [InlineData(null, "Enter the mvsMF URL.")]
    [InlineData("  ", "Enter the mvsMF URL.")]
    [InlineData("ftp://h/zosmf", "Enter an http:// or https:// URL.")]
    [InlineData("h:8080", "Enter an http:// or https:// URL.")]
    [InlineData("10.42.37.209:8080", "Enter an http:// or https:// URL.")]
    [InlineData("http://h:8080/zosmf?x=1", "The URL cannot have a query or a fragment.")]
    public void Rejects_what_is_not_an_http_base(string? text, string expected)
    {
        Assert.False(MvsmfOptions.TryNormalizeBaseUrl(text, out var url, out var error));
        Assert.Null(url);
        Assert.Equal(expected, error);
    }
}
