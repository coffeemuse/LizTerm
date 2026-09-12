// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App;

/// <summary>The project's own pages, as the Help menu offers them. Constants rather than literals in XAML so
/// that the two menus bind the same string: the native-menu parity guard compares CommandParameter by
/// equality, and two spellings of one URL would pass it while sending a user somewhere else.</summary>
public static class ProjectLinks
{
    private const string Repo = "https://github.com/coffeemuse/LizTerm";

    public const string Repository = Repo;

    /// <summary>The chooser, not a blank issue: it lands on the bug-report and feature-request forms, which ask
    /// for the operating system, the LizTerm version, the host, TLS and the engine source.</summary>
    public const string NewIssue = Repo + "/issues/new/choose";

    public const string Releases = Repo + "/releases";

    /// <summary>Where the guide lives when the bundled copy cannot be opened. `main`, not the tag: a reader who
    /// could not open the offline copy wants the manual, and the current one is the best answer available.</summary>
    public const string UserGuide = Repo + "/blob/main/docs/user-guide.md";
}
