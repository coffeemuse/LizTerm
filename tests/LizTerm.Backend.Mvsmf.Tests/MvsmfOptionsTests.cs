// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfOptionsTests
{
    [Theory]
    [InlineData("http://192.0.2.10:8080", "http://192.0.2.10:8080/zosmf")]
    [InlineData(" http://192.0.2.10:8080/ ", "http://192.0.2.10:8080/zosmf")]
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
    [InlineData("192.0.2.10:8080", "Enter an http:// or https:// URL.")]
    [InlineData("http://h:8080/zosmf?x=1", "The URL cannot have a query or a fragment.")]
    [InlineData("http://alice:secret@h:8080", "Leave the userid and password out of the URL.")]
    public void Rejects_what_is_not_an_http_base(string? text, string expected)
    {
        Assert.False(MvsmfOptions.TryNormalizeBaseUrl(text, out var url, out var error));
        Assert.Null(url);
        Assert.Equal(expected, error);
    }
    public static TheoryData<Uri, string> BadBaseUrls => new()
    {
        { new Uri("zosmf", UriKind.Relative), "Enter an http:// or https:// URL." },
        { new Uri("ftp://h/zosmf"), "Enter an http:// or https:// URL." },
        { new Uri("http://alice:secret@h:8080/zosmf"), "Leave the userid and password out of the URL." },
        { new Uri("http://h:8080/zosmf?x=1"), "The URL cannot have a query or a fragment." },
        { new Uri("http://h:8080/zosmf#top"), "The URL cannot have a query or a fragment." },
    };

    [Theory]
    [MemberData(nameof(BadBaseUrls))]
    public void The_constructor_refuses_what_is_not_an_http_base(Uri url, string expected)
    {
        var e = Assert.Throws<ArgumentException>(() => new MvsmfOptions(url));
        Assert.StartsWith(expected, e.Message);
        Assert.DoesNotContain("secret", e.Message);
    }

    [Fact]
    public void The_constructor_keeps_a_good_url_as_given()
    {
        var url = new Uri("https://proxy.example/mvs/zosmf/");
        var options = new MvsmfOptions(url);
        Assert.Same(url, options.BaseUrl);
        Assert.Null(options.PinnedCertificate);
        Assert.DoesNotContain("@", options.ToString());
    }
}
