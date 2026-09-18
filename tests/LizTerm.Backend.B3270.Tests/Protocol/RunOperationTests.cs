// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Backend.B3270.Protocol;

namespace LizTerm.Backend.B3270.Tests.Protocol;

public class RunOperationTests
{
    [Fact]
    public void Describe_names_an_action_that_carries_no_arguments()
    {
        Assert.Equal("Enter", RunOperation.Describe([new B3270Action("Enter")]));
    }

    /// <summary>The whole point of the summary: it goes on an error banner and into bug reports, so it may never
    /// carry what the user typed. A String() action at a logon screen holds a password (#139).</summary>
    [Fact]
    public void Describe_gives_the_size_of_arguments_and_never_their_values()
    {
        var summary = RunOperation.Describe([new B3270Action("String", "hunter2secret")]);
        Assert.Equal("String(13 chars)", summary);
        Assert.DoesNotContain("hunter2", summary);
    }

    [Fact]
    public void Describe_counts_every_argument_of_an_action_together()
    {
        Assert.Equal("Set(19 chars)", RunOperation.Describe([new B3270Action("Set", "verifyHostCert", "false")]));
    }

    [Fact]
    public void Describe_joins_several_actions_in_the_order_they_were_sent()
    {
        var summary = RunOperation.Describe([new B3270Action("String", "COBOL"), new B3270Action("Enter")]);
        Assert.Equal("String(5 chars), Enter", summary);
    }

    [Fact]
    public void Describe_handles_a_line_that_carries_no_actions()
    {
        Assert.Equal("no actions", RunOperation.Describe([]));
    }
}
