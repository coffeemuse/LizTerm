// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Backend.B3270.Protocol;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>#200: the profile's Extended box once only dropped the <c>-E</c> from b3270's <c>-model</c>, which b3270
/// 4.5 ignores (<c>common_model_init</c> reads the model number and colour and nothing else), so an unchecked box
/// still told the host <c>IBM-3279-2-E</c>. This asks the bundled engine what it will tell the host, rather than
/// trusting the command line to mean what it says, and checks that the screen it starts with is the one the session
/// window is sized for (<see cref="ScreenSize.AlternateFor"/>, #198): b3270 drops an oversize without the stream,
/// so a custom size has to keep the stream on. Never connects to anything.</summary>
public class EngineExtendedDataStreamTests
{
    [Theory(Timeout = 60_000)]
    [InlineData(true, null, "IBM-3279-2-E", "true")]
    [InlineData(false, null, "IBM-3279-2", "false")]
    [InlineData(false, "132x60", "IBM-DYNAMIC", "true")]
    public async Task The_engine_tells_the_host_what_the_profile_says(bool extended, string? oversize,
        string terminalName, string stream)
    {
        var location = BundledEngine.Require();

        var profile = new SessionProfile(Name: "engine-extended", Host: "engine-extended.invalid", Model: 2,
            Extended: extended, Oversize: oversize);
        await using var session = new B3270Session(profile, () => new B3270ChildProcess(location.Path), location: location);
        await session.StartProcessAsync(TestContext.Current.CancellationToken);

        var result = await session.RunAsync(
            new B3270Action("Query", "TerminalName"), new B3270Action("Set", "extendedDataStream"));
        Assert.Equal([terminalName, stream], result.Text);

        // The run's answer follows the initialize block on the same reader thread, so the screen-mode that block
        // carried has been applied by now.
        var alternate = ScreenSize.AlternateFor(profile);
        Assert.Equal((alternate.Rows, alternate.Columns), (session.CurrentScreen.Rows, session.CurrentScreen.Columns));
    }
}
