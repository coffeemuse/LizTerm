using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class ConnectTypesTests
{
    [Fact]
    public void ConnectOptions_defaults_to_the_profile()
    {
        Assert.Null(new ConnectOptions().VerifyCertificate);
        Assert.False(new ConnectOptions(VerifyCertificate: false).VerifyCertificate);
    }

    [Fact]
    public void ConnectionFailedException_carries_the_certificate_flag()
    {
        var plain = new ConnectionFailedException(["Connection failed:", "refused"]);
        Assert.False(plain.CertificateVerificationFailed);
        Assert.Equal("Connection failed: refused", plain.Message);

        var cert = new ConnectionFailedException(["Connection failed:", "TLS: Host certificate verification failed:", "self-signed certificate (18)"], certificateVerificationFailed: true);
        Assert.True(cert.CertificateVerificationFailed);
        Assert.Equal(3, cert.Lines.Count);
    }

    [Fact]
    public void EngineInfo_is_a_value()
    {
        var a = new EngineInfo("b3270", null, "/x/b3270", EngineSource.Bundled);
        Assert.Equal(a, a with { });
        Assert.Equal("4.5.6", (a with { Version = "4.5.6" }).Version);
    }
}
