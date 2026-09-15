// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;

namespace LizTerm.App.Sessions;

/// <summary>One open session. Summary is the Sessions list's row for its profile, built once when the session
/// opens from the same tag colours the window got, so the switcher draws a row exactly as the list does.</summary>
public sealed record SessionEntry(SessionViewModel Session, ProfileRow Summary, bool IsSaved, ISessionHost Host);
