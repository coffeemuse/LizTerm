// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class TerminalTypeTests
{
    [Fact]
    public void Extended_profiles_carry_the_E_suffix()
    {
        Assert.Equal("3279-2-E", TerminalType.For(new SessionProfile { Model = 2, Extended = true }));
        Assert.Equal("3279-5-E", TerminalType.For(new SessionProfile { Model = 5, Extended = true }));
    }

    [Fact]
    public void Non_extended_profiles_do_not()
    {
        Assert.Equal("3279-4", TerminalType.For(new SessionProfile { Model = 4, Extended = false }));
    }
}
