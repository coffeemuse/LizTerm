// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Backend.B3270;
using LizTerm.Backend.B3270.Process;
using LizTerm.Backend.B3270.Protocol;
using LizTerm.Core.Session;

namespace LizTerm.Integration.Tests;

/// <summary>#202: the status bar once named the terminal from the profile, which is not what b3270 tells the host. A
/// colour model 4 or 5 is a 3278 to b3270 (<c>create_3270_termtype</c> names a 3279 only for models 2 and 3), and any
/// custom size is <c>IBM-DYNAMIC</c>. The session now carries the engine's own <c>terminal-name</c> report, and this
/// checks it against the bundled engine's answer to <c>Query TerminalName</c>. Never connects to anything.</summary>
public class EngineTerminalNameTests
{
    [Theory(Timeout = 60_000)]
    [InlineData(2, TerminalDisplay.Color, null, "IBM-3279-2-E")]
    [InlineData(4, TerminalDisplay.Color, null, "IBM-3278-4-E")]
    [InlineData(5, TerminalDisplay.Color, null, "IBM-3278-5-E")]
    [InlineData(3, TerminalDisplay.Mono, null, "IBM-3278-3-E")]
    [InlineData(2, TerminalDisplay.Color, "132x60", "IBM-DYNAMIC")]
    public async Task The_session_reports_the_name_the_engine_gives_the_host(int model, TerminalDisplay display,
        string? oversize, string terminalName)
    {
        var location = BundledEngine.Require();

        var profile = new SessionProfile(Name: "engine-terminal-name", Host: "engine-terminal-name.invalid",
            Model: model, Oversize: oversize) { Display = display };
        await using var session = new B3270Session(profile, () => new B3270ChildProcess(location.Path), location: location);
        await session.StartProcessAsync(TestContext.Current.CancellationToken);

        var result = await session.RunAsync(new B3270Action("Query", "TerminalName"));
        Assert.Equal([terminalName], result.Text);
        // The run's answer follows the initialize block on the same reader thread, so the terminal-name that block
        // carried has been applied by now.
        Assert.Equal(terminalName, session.TerminalName);
    }
}
