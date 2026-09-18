// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.HostFiles;

public class HostFileConnectionTests
{
    private static readonly PresentedCertificate Presented = new("11:22", "CN=proxy", "-----BEGIN CERTIFICATE-----", true, null);
    private static readonly CertificatePin Accepted = new("11:22", "CN=proxy", "-----BEGIN CERTIFICATE-----");

    private sealed class Host
    {
        public List<(CertificatePin? Pin, FakeHostFileService Service)> Created { get; } = [];
        public List<CertificatePin> Saved { get; } = [];
        public HostTokenProvider? Provider { get; private set; }
        public TaskCompletionSource? FirstGate { get; set; }
        /// <summary>Services made with a pin refuse too, as for a certificate the pin check can never accept.</summary>
        public bool AlwaysRefuse { get; init; }

        /// <summary>A service made without a pin refuses the certificate; one made with a pin answers.</summary>
        public IHostFileService Create(Uri url, CertificatePin? pin, HostTokenProvider provider)
        {
            Provider = provider;
            var service = new FakeHostFileService();
            if (pin is null || AlwaysRefuse)
            {
                service.Failures["info"] = new HostFileException(HostFileErrorKind.CertificateRejected, "Server information: the host's certificate is not trusted.", certificate: Presented);
                service.Gate = FirstGate;
            }
            lock (Created) Created.Add((pin, service));
            return service;
        }

        public HostFileAccess Access(bool saved = true, CertificatePin? profilePin = null) => new(
            new SessionProfile { Name = "MVS", Host = "proxy", HostFilesUrl = "https://proxy/zosmf", HostFilesPinnedCertificate = profilePin },
            Create, saved ? Saved.Add : null);
    }

    private static Task<HostServerInfo> Info(HostFileConnection connection) => connection.RunAsync(s => s.GetServerInfoAsync());

    [Fact]
    public async Task Connect_anyway_retries_with_the_presented_certificate_for_this_session()
    {
        var host = new Host();
        var access = host.Access();
        var prompt = new FakeCertificatePrompt { Decision = new CertificateDecision(true, false) };
        using var connection = access.Connect(new FakeCredentialPrompt(), prompt);

        var info = await Info(connection);

        Assert.Equal("1.1.0", info.ProductVersion);
        Assert.Equal(new CertificatePin?[] { null, Accepted }, host.Created.Select(c => c.Pin));
        Assert.Equal(Accepted, access.Pin);
        Assert.Empty(host.Saved);
        var request = prompt.LastRequest!;
        Assert.Equal("proxy", request.Host);
        Assert.Equal(new[] { "The mvsMF host's certificate is not trusted." }, request.Reason);
        Assert.Same(Presented, request.Presented);
        Assert.True(request.CanPin);
        Assert.Null(request.CannotPinReason);
    }

    [Fact]
    public async Task Remember_stores_the_pin_in_a_saved_profile()
    {
        var host = new Host();
        using var connection = host.Access().Connect(new FakeCredentialPrompt(), new FakeCertificatePrompt { Decision = new CertificateDecision(true, true) });

        await Info(connection);

        Assert.Equal(new[] { Accepted }, host.Saved);
    }

    [Fact]
    public async Task An_unsaved_profile_is_not_offered_the_pin()
    {
        var host = new Host();
        var prompt = new FakeCertificatePrompt { Decision = new CertificateDecision(true, true) };
        using var connection = host.Access(saved: false).Connect(new FakeCredentialPrompt(), prompt);

        await Info(connection);

        Assert.False(prompt.LastRequest!.CanPin);
        Assert.Null(prompt.LastRequest.CannotPinReason);
        Assert.Empty(host.Saved);
    }

    [Fact]
    public async Task A_certificate_that_cannot_be_pinned_says_why()
    {
        var host = new Host();
        var prompt = new FakeCertificatePrompt();
        var unpinnable = Presented with { Pinnable = false, NotPinnableReason = "The chain is missing its root" };
        var access = new HostFileAccess(new SessionProfile { Name = "MVS", Host = "p", HostFilesUrl = "https://proxy/zosmf" },
            (url, pin, provider) =>
            {
                var service = new FakeHostFileService();
                service.Failures["info"] = new HostFileException(HostFileErrorKind.CertificateRejected, "x", certificate: unpinnable);
                return service;
            }, host.Saved.Add);
        using var connection = access.Connect(new FakeCredentialPrompt(), prompt);

        await Assert.ThrowsAsync<HostFileException>(() => Info(connection));

        Assert.False(prompt.LastRequest!.CanPin);
        Assert.Equal("This certificate cannot be pinned: The chain is missing its root. Connect Anyway applies to this session only.",
            prompt.LastRequest.CannotPinReason);
    }

    [Fact]
    public async Task A_pin_that_cannot_be_saved_still_holds_for_the_session_and_is_reported()
    {
        var host = new Host();
        var access = new HostFileAccess(
            new SessionProfile { Name = "MVS", Host = "proxy", HostFilesUrl = "https://proxy/zosmf" },
            host.Create, _ => throw new IOException("disk full"));
        var reported = new List<string>();
        access.PinSaveFailed += (_, message) => reported.Add(message);
        var prompt = new FakeCertificatePrompt { Decision = new CertificateDecision(true, true) };
        using var connection = access.Connect(new FakeCredentialPrompt(), prompt);

        await Info(connection);

        Assert.Equal(Accepted, access.Pin);
        Assert.Equal(new[] { "Could not save the certificate to the profile: disk full" }, reported);
    }

    [Fact]
    public async Task Declining_keeps_the_refusal()
    {
        var host = new Host();
        using var connection = host.Access().Connect(new FakeCredentialPrompt(), new FakeCertificatePrompt());

        var ex = await Assert.ThrowsAsync<HostFileException>(() => Info(connection));

        Assert.Equal(HostFileErrorKind.CertificateRejected, ex.Kind);
        Assert.All(host.Created, c => Assert.Null(c.Pin));
        Assert.Single(host.Created.SelectMany(c => c.Service.CallsSnapshot()));
    }

    [Fact]
    public async Task Without_a_certificate_prompt_the_refusal_stands()
    {
        var host = new Host();
        using var connection = host.Access().Connect(new FakeCredentialPrompt(), certificates: null);
        await Assert.ThrowsAsync<HostFileException>(() => Info(connection));
    }

    [Fact]
    public async Task The_profile_pin_is_used_from_the_start()
    {
        var host = new Host();
        var old = new CertificatePin("99:99", "CN=old", "pem");
        using var connection = host.Access(profilePin: old).Connect(new FakeCredentialPrompt(), new FakeCertificatePrompt());

        await Info(connection);

        Assert.Equal(new CertificatePin?[] { old }, host.Created.Select(c => c.Pin));
    }

    [Fact]
    public async Task A_pin_accepted_in_another_browser_window_is_used_without_asking()
    {
        var host = new Host();
        var access = host.Access();
        var prompt = new FakeCertificatePrompt { Decision = new CertificateDecision(true, false) };
        using var first = access.Connect(new FakeCredentialPrompt(), prompt);
        using var second = access.Connect(new FakeCredentialPrompt(), prompt);

        await Info(first);
        await Info(second);

        Assert.Single(prompt.Calls);
        Assert.Equal(Accepted, host.Created[^1].Pin);
    }

    [Fact]
    public async Task Refusals_that_arrive_together_ask_once()
    {
        var host = new Host { FirstGate = new TaskCompletionSource() };
        var prompt = new FakeCertificatePrompt { Decision = new CertificateDecision(true, false) };
        using var connection = host.Access().Connect(new FakeCredentialPrompt(), prompt);

        var a = Info(connection);
        var b = Info(connection);
        host.FirstGate.SetResult();
        await Task.WhenAll(a, b);

        Assert.Single(prompt.Calls);
    }

    [Fact]
    public async Task A_decline_answers_the_refusals_that_arrived_with_it()
    {
        var host = new Host { FirstGate = new TaskCompletionSource() };
        var prompt = new FakeCertificatePrompt();
        using var connection = host.Access().Connect(new FakeCredentialPrompt(), prompt);

        var a = Info(connection);
        var b = Info(connection);
        host.FirstGate.SetResult();

        Assert.Equal(HostFileErrorKind.CertificateRejected, (await Assert.ThrowsAsync<HostFileException>(() => a)).Kind);
        Assert.Equal(HostFileErrorKind.CertificateRejected, (await Assert.ThrowsAsync<HostFileException>(() => b)).Kind);
        Assert.Single(prompt.Calls);
    }

    [Fact]
    public async Task After_a_decline_a_later_operation_asks_again()
    {
        var host = new Host();
        var prompt = new FakeCertificatePrompt();
        using var connection = host.Access().Connect(new FakeCredentialPrompt(), prompt);

        await Assert.ThrowsAsync<HostFileException>(() => Info(connection));
        await Assert.ThrowsAsync<HostFileException>(() => Info(connection));

        Assert.Equal(2, prompt.Calls.Count);
        Assert.Equal(3, host.Created.Count);
        connection.Dispose();
        Assert.All(host.Created, c => Assert.True(c.Service.Disposed));
    }

    [Fact]
    public async Task After_a_decline_a_new_certificate_is_asked_about()
    {
        var renewed = Presented with { Sha256 = "33:44" };
        var made = 0;
        var prompt = new FakeCertificatePrompt();
        var access = new HostFileAccess(new SessionProfile { Name = "MVS", Host = "proxy", HostFilesUrl = "https://proxy/zosmf" },
            (url, pin, provider) =>
            {
                var service = new FakeHostFileService();
                var presented = Interlocked.Increment(ref made) == 1 ? Presented : renewed;
                service.Failures["info"] = new HostFileException(HostFileErrorKind.CertificateRejected, "x", certificate: presented);
                return service;
            }, savePin: null);
        using var connection = access.Connect(new FakeCredentialPrompt(), prompt);

        await Assert.ThrowsAsync<HostFileException>(() => Info(connection));
        await Assert.ThrowsAsync<HostFileException>(() => Info(connection));

        Assert.Equal(2, prompt.Calls.Count);
        Assert.Same(renewed, prompt.LastRequest!.Presented);
    }

    [Fact]
    public async Task A_refusal_of_the_pinned_certificate_itself_does_not_ask()
    {
        var host = new Host { AlwaysRefuse = true };
        var prompt = new FakeCertificatePrompt { Decision = new CertificateDecision(true, false) };
        using var connection = host.Access(profilePin: Accepted).Connect(new FakeCredentialPrompt(), prompt);

        var ex = await Assert.ThrowsAsync<HostFileException>(() => Info(connection));

        Assert.Equal(HostFileErrorKind.CertificateRejected, ex.Kind);
        Assert.Empty(prompt.Calls);
    }

    [Fact]
    public async Task Connect_anyway_is_asked_once_for_a_certificate_the_pin_cannot_accept()
    {
        var host = new Host { AlwaysRefuse = true };
        var prompt = new FakeCertificatePrompt { Decision = new CertificateDecision(true, false) };
        using var connection = host.Access().Connect(new FakeCredentialPrompt(), prompt);

        await Assert.ThrowsAsync<HostFileException>(() => Info(connection));
        Assert.Single(prompt.Calls);

        await Assert.ThrowsAsync<HostFileException>(() => Info(connection));
        Assert.Single(prompt.Calls);
    }

    [Fact]
    public async Task The_service_signs_in_through_the_session_holder()
    {
        var host = new Host();
        var access = host.Access();
        var credentials = new FakeCredentialPrompt();
        using var connection = access.Connect(credentials, null);

        var answer = await host.Provider!(new HostTokenRequest(null),
            (c, _) => Task.FromResult(new HostSessionToken($"tok:{c.Userid}")), CancellationToken.None);

        Assert.Equal("tok:MVSCE02", answer!.Value);
        Assert.True(access.SignIn.IsSignedIn);
        Assert.Equal("MVS", credentials.LastRequest!.ProfileName);
    }

    [Fact]
    public async Task Dispose_disposes_every_service_it_made_and_refuses_further_work()
    {
        var host = new Host();
        var connection = host.Access().Connect(new FakeCredentialPrompt(), new FakeCertificatePrompt { Decision = new CertificateDecision(true, false) });
        await Info(connection);

        connection.Dispose();

        Assert.All(host.Created, c => Assert.True(c.Service.Disposed));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Info(connection));
    }
}
