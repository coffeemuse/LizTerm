// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.HostFiles;

public class HostFileAccessTests
{
    private static readonly CertificatePin Pin = new("AA:BB", "CN=proxy", "pem");

    [Fact]
    public void Reads_the_profile_and_normalises_the_url()
    {
        var access = new HostFileAccess(
            new SessionProfile { Name = "MVS/CE", Host = "mvs", HostFilesUrl = "http://mvs:8080", HostFilesUserid = "MVSCE02", HostFilesPinnedCertificate = Pin },
            (_, _, _) => new FakeHostFileService(), savePin: null);

        Assert.Equal("MVS/CE", access.ProfileName);
        Assert.Equal("MVSCE02", access.Userid);
        Assert.Equal("http://mvs:8080/zosmf", access.Url!.ToString());
        Assert.Null(access.UrlError);
        Assert.Equal(Pin, access.Pin);
        Assert.False(access.CanRememberPin);
    }

    [Fact]
    public void An_unusable_url_is_reported_and_cannot_connect()
    {
        var access = new HostFileAccess(new SessionProfile { Name = "x", Host = "h", HostFilesUrl = "ftp://h" },
            (_, _, _) => new FakeHostFileService(), savePin: null);

        Assert.Null(access.Url);
        Assert.Equal("Enter an http:// or https:// URL.", access.UrlError);
        var ex = Assert.Throws<InvalidOperationException>(() => access.Connect(new FakeCredentialPrompt(), null));
        Assert.Equal("Enter an http:// or https:// URL.", ex.Message);
    }

    [Fact]
    public async Task Forget_drops_the_sign_in()
    {
        var access = new HostFileAccess(new SessionProfile { Name = "x", Host = "h", HostFilesUrl = "http://h" },
            (_, _, _) => new FakeHostFileService(), savePin: null);
        await access.Credentials.ProviderFor(new FakeCredentialPrompt())(new(false), CancellationToken.None);
        Assert.True(access.Credentials.HasCredentials);

        access.Forget();

        Assert.False(access.Credentials.HasCredentials);
    }
}
