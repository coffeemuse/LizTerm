// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Net.Sockets;
using LizTerm.App.Tests.Fakes;
using LizTerm.Backend.Mvsmf;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests;

/// <summary>The one App test that names the mvsMF backend (tests/CLAUDE.md): what the factory builds is the one
/// thing about that backend the App owns.</summary>
public class HostFileServiceFactoryTests
{
    private static ValueTask<HostSessionToken?> Anyone(HostTokenRequest request, HostSignIn signIn, CancellationToken token) =>
        ValueTask.FromResult<HostSessionToken?>(new HostSessionToken("tok"));

    [Fact]
    public void Create_builds_the_mvsmf_service()
    {
        using var service = HostFileServiceFactory.Create(new Uri("http://mvs.test:8080/zosmf"), null, Anyone);
        Assert.IsType<MvsmfFileService>(service);
    }

    [Fact]
    public void A_url_with_credentials_is_refused_both_ways()
    {
        Assert.False(HostFileServiceFactory.TryNormalizeUrl("http://u:p@mvs.test", out var url, out var error));
        Assert.Null(url);
        Assert.Equal("Leave the userid and password out of the URL.", error);
        Assert.Throws<ArgumentException>(() => HostFileServiceFactory.Create(new Uri("http://u:p@mvs.test/zosmf"), null, Anyone));
    }

    [Fact]
    public void The_url_gets_its_zosmf_path()
    {
        Assert.True(HostFileServiceFactory.TryNormalizeUrl("http://mvs.test:8080", out var url, out _));
        Assert.Equal("http://mvs.test:8080/zosmf", url!.ToString());
    }

    [Fact]
    public async Task The_tester_reports_a_host_it_cannot_reach_without_asking_for_a_password()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var prompt = new FakeCredentialPrompt();

        var tester = HostFileServiceFactory.CreateTester(prompt);
        var ex = await Assert.ThrowsAsync<HostFileException>(() =>
            tester("MVS/CE", new Uri($"http://127.0.0.1:{port}/zosmf"), "MVSCE02", null, TestContext.Current.CancellationToken));

        // The probe is the first request, and it fails before any sign-in is needed.
        Assert.Equal(HostFileErrorKind.Unreachable, ex.Kind);
        Assert.Empty(prompt.Calls);
    }
}
