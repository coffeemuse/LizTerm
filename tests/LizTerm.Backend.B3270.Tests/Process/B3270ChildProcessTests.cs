// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Backend.B3270.Process;

namespace LizTerm.Backend.B3270.Tests.Process;

/// <summary>The environment the engine is started with (#170). The engine's own behaviour under these variables is
/// proved against the real binary by the integration project's EngineLocaleTests; this holds the rule itself.</summary>
public class B3270ChildProcessTests
{
    [Fact]
    public void The_decimal_point_is_pinned_and_the_rest_of_the_locale_is_left_alone()
    {
        var env = new Dictionary<string, string?> { ["LANG"] = "hr_HR.UTF-8", ["PATH"] = "/usr/bin" };
        B3270ChildProcess.ConfigureLocale(env);
        Assert.Equal("C", env["LC_NUMERIC"]);
        Assert.Equal("hr_HR.UTF-8", env["LANG"]);
        Assert.Equal("/usr/bin", env["PATH"]);
        Assert.False(env.ContainsKey("LC_ALL"));
        Assert.False(env.ContainsKey("LC_CTYPE"));
    }

    [Fact]
    public void A_users_own_numeric_locale_is_overridden()
    {
        var env = new Dictionary<string, string?> { ["LC_NUMERIC"] = "de_DE.UTF-8" };
        B3270ChildProcess.ConfigureLocale(env);
        Assert.Equal("C", env["LC_NUMERIC"]);
    }

    /// <summary>LC_ALL outranks LC_NUMERIC, so it cannot stay. What it stood for is spelled out per category, so the
    /// engine still reads the same codeset it would have, and only the decimal point changes.</summary>
    [Fact]
    public void LC_ALL_is_spelled_out_into_its_categories_and_removed()
    {
        var env = new Dictionary<string, string?> { ["LC_ALL"] = "hr_HR.UTF-8", ["LANG"] = "en_US.UTF-8", ["LC_CTYPE"] = "C" };
        B3270ChildProcess.ConfigureLocale(env);
        Assert.False(env.ContainsKey("LC_ALL"));
        Assert.Equal("C", env["LC_NUMERIC"]);
        Assert.Equal("en_US.UTF-8", env["LANG"]);
        foreach (var category in new[] { "LC_CTYPE", "LC_COLLATE", "LC_MESSAGES", "LC_MONETARY", "LC_TIME" })
            Assert.Equal("hr_HR.UTF-8", env[category]);
    }

    /// <summary>POSIX reads an empty LC_ALL as unset, so it stands for nothing and must not be copied anywhere.</summary>
    [Fact]
    public void An_empty_LC_ALL_is_removed_without_being_copied()
    {
        var env = new Dictionary<string, string?> { ["LC_ALL"] = "", ["LC_CTYPE"] = "hr_HR.UTF-8" };
        B3270ChildProcess.ConfigureLocale(env);
        Assert.False(env.ContainsKey("LC_ALL"));
        Assert.Equal("hr_HR.UTF-8", env["LC_CTYPE"]);
        Assert.False(env.ContainsKey("LC_TIME"));
        Assert.Equal("C", env["LC_NUMERIC"]);
    }

    [Fact]
    public void An_environment_with_no_locale_at_all_gets_only_the_decimal_point()
    {
        var env = new Dictionary<string, string?>();
        B3270ChildProcess.ConfigureLocale(env);
        Assert.Equal(new Dictionary<string, string?> { ["LC_NUMERIC"] = "C" }, env);
    }
}
