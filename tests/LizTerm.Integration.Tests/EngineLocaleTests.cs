// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net;
using System.Net.Sockets;
using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>#170: b3270 (4.5ga6, Common/codepage.c) calls setlocale(LC_ALL, "") on macOS and Linux and then prints
/// every JSON double with "%g", so under a locale whose decimal separator is a comma its run-result for any run that
/// took a millisecond or more reads <c>"time":0,039</c>. That is not JSON, the parser drops the line, the Connect run
/// is never answered, and the session times out after thirty seconds while the screen sits there live. The reporter
/// ran Linux Mint in Croatian; a Mac launched from the Finder has no LANG at all, which is why it never showed there.
/// This test runs the bundled engine the way that Linux desktop does and asks for the one run whose answer must
/// carry a fractional time — a refused connect — and expects the refusal, not a hang.
///
/// The locale has to exist on the machine for setlocale to honour it: macOS ships them all, so the test is decisive
/// there; a Linux runner without hr_HR generated falls back to C and passes on its own. Windows engines never call
/// setlocale, and LANG means nothing there, so the test skips.</summary>
[Collection(EnvironmentCollection.Name)]
public class EngineLocaleTests
{
    [Fact(Timeout = 60_000)]
    public async Task A_comma_decimal_locale_does_not_hide_the_engines_answers()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The Windows engine does not call setlocale and LANG has no meaning there.");
        var location = BundledEngine.Require();

        var originalLang = Environment.GetEnvironmentVariable("LANG");
        var originalLcAll = Environment.GetEnvironmentVariable("LC_ALL");
        var originalLcNumeric = Environment.GetEnvironmentVariable("LC_NUMERIC");
        try
        {
            // The reporter's desktop, as the engine inherits it: a comma-decimal LANG and nothing overriding it.
            Environment.SetEnvironmentVariable("LANG", "hr_HR.UTF-8");
            Environment.SetEnvironmentVariable("LC_ALL", null);
            Environment.SetEnvironmentVariable("LC_NUMERIC", null);

            var profile = new SessionProfile(Name: "engine-locale", Host: "127.0.0.1", Port: RefusedPort());
            await using var session = new B3270Session(profile, () => new B3270ChildProcess(location.Path), location: location);
            // Ten seconds is far more than a loopback refusal needs and far less than the app's own thirty, so a
            // dropped answer shows up here as OperationCanceledException rather than as a slow pass.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            // The exception's type is the proof: it is raised only from a parsed run-result. Its text is not asserted,
            // because glibc translates strerror under LC_MESSAGES, which now follows LANG, so on a machine with hr_HR
            // generated the refusal arrives in Croatian.
            var ex = await Assert.ThrowsAsync<ConnectionFailedException>(() => session.ConnectAsync(cancellationToken: cts.Token));
            Assert.NotEmpty(ex.Lines);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LANG", originalLang);
            Environment.SetEnvironmentVariable("LC_ALL", originalLcAll);
            Environment.SetEnvironmentVariable("LC_NUMERIC", originalLcNumeric);
        }
    }

    /// <summary>A loopback port nothing listens on: bound once to learn a free number, then released.</summary>
    private static int RefusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
