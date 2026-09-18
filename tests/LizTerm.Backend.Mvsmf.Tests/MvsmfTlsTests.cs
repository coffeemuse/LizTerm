// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Security;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Security;
using LizTerm.Core.Session;
using LizTerm.Core.Tests.Security;

namespace LizTerm.Backend.Mvsmf.Tests;

public class MvsmfTlsTests
{
    private const string Info = """{"zosmf_version":"tls-test","zos_version":"MVS 3.8j"}""";

    private static MvsmfFileService Service(int port, CertificatePin? pin) => new(
        new MvsmfOptions(new Uri($"https://localhost:{port}/zosmf"), pin),
        MvsmfAuthTests.Providing([], new HostCredentials("U", "p")));

    private static CertificatePin PinFor(System.Security.Cryptography.X509Certificates.X509Certificate2 certificate) =>
        new(CertificateReader.Fingerprint(certificate), certificate.Subject, certificate.ExportCertificatePem());

    // A TLS regression should fail the test rather than hang the suite: LoopbackHttpsServer's own handshake never
    // times out on its own, so these three carry an explicit timeout.
    [Fact(Timeout = 30000)]
    public async Task An_untrusted_certificate_is_rejected_and_described()
    {
        using var certificate = TestCertificates.WithUsableKey(TestCertificates.SelfSigned());
        await using var server = new LoopbackHttpsServer(certificate, Info);
        using var service = Service(server.Port, pin: null);

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.CertificateRejected, ex.Kind);
        // The first request a service makes is the sign-in, so that is what the refusal is reported against;
        // HostFileMessages.Describe maps CertificateRejected to a fixed sentence, so the prefix never reaches the user.
        Assert.Equal("Sign-in: the host's certificate is not trusted.", ex.Message);
        Assert.Equal(CertificateReader.Fingerprint(certificate), ex.Certificate!.Sha256);
        Assert.True(ex.Certificate.Pinnable, ex.Certificate.NotPinnableReason);
    }

    [Fact(Timeout = 30000)]
    public async Task A_pinned_certificate_is_trusted()
    {
        using var certificate = TestCertificates.WithUsableKey(TestCertificates.SelfSigned());
        await using var server = new LoopbackHttpsServer(certificate, Info);
        using var service = Service(server.Port, PinFor(certificate));

        var info = await service.GetServerInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("tls-test", info.ProductVersion);
    }

    [Fact(Timeout = 30000)]
    public async Task A_pin_for_another_certificate_is_rejected()
    {
        using var certificate = TestCertificates.WithUsableKey(TestCertificates.SelfSigned());
        using var other = TestCertificates.SelfSigned("CN=someone-else");
        await using var server = new LoopbackHttpsServer(certificate, Info);
        using var service = Service(server.Port, PinFor(other));

        var ex = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HostFileErrorKind.CertificateRejected, ex.Kind);
        Assert.Equal(CertificateReader.Fingerprint(certificate), ex.Certificate!.Sha256);
    }

    [Fact]
    public void No_certificate_is_never_trusted()
    {
        var check = new MvsmfCertificateCheck(null);
        Assert.False(check.Validate(new object(), null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
        Assert.Null(check.TakeRejected());
    }

    [Fact]
    public void A_pin_ignores_name_and_chain_errors_when_the_fingerprint_matches()
    {
        using var certificate = TestCertificates.SelfSigned();
        var check = new MvsmfCertificateCheck(PinFor(certificate));
        Assert.True(check.Validate(new object(), certificate, null,
            SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.Null(check.TakeRejected());
    }

    [Fact]
    public void A_pin_refuses_its_certificate_once_it_has_expired()
    {
        using var certificate = TestCertificates.SelfSigned(
            notBefore: DateTimeOffset.UtcNow.AddDays(-10), notAfter: DateTimeOffset.UtcNow.AddDays(-1));
        var check = new MvsmfCertificateCheck(PinFor(certificate));
        Assert.False(check.Validate(new object(), certificate, null, SslPolicyErrors.None));
        Assert.NotNull(check.TakeRejected());
    }

    [Fact]
    public void A_refusal_is_reported_once()
    {
        var check = new MvsmfCertificateCheck(null);
        using var certificate = TestCertificates.SelfSigned();

        Assert.False(check.Validate(new object(), certificate, null, SslPolicyErrors.RemoteCertificateChainErrors));

        var rejected = check.TakeRejected();
        Assert.NotNull(rejected);
        Assert.Equal(CertificateReader.Fingerprint(certificate), rejected!.Sha256);
        Assert.Null(check.TakeRejected());
    }

    [Fact]
    public async Task A_later_handshake_failure_is_not_blamed_on_an_old_certificate()
    {
        var check = new MvsmfCertificateCheck(null);
        using var certificate = TestCertificates.SelfSigned();
        Assert.False(check.Validate(new object(), certificate, null, SslPolicyErrors.RemoteCertificateChainErrors));

        var handler = new RecordedHandler()
            .Then((_, _) => throw new HttpRequestException("handshake failed", new System.Security.Authentication.AuthenticationException("tls")))
            .Then((_, _) => throw new HttpRequestException("handshake failed", new System.Security.Authentication.AuthenticationException("tls")));
        using var service = new MvsmfFileService(handler, new Uri("https://mvs.test/zosmf"),
            MvsmfAuthTests.Providing([], new HostCredentials("U", "p")), certificates: check);

        var first = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HostFileErrorKind.CertificateRejected, first.Kind);
        Assert.NotNull(first.Certificate);

        var second = await Assert.ThrowsAsync<HostFileException>(() => service.GetServerInfoAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HostFileErrorKind.Unreachable, second.Kind);
    }
}
