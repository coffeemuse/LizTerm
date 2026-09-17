// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

/// <summary>What the profile editor hands back. The profile alone is not enough: the editor expresses "no pin"
/// three different ways (never had one, the user pressed Forget, the host or port was repointed) and only it
/// knows which, while only the picker can see the file. <see cref="LizTerm.Core.Profiles.PinMerge"/> resolves
/// the two, for the 3270 pin and the REST pin alike.</summary>
public sealed record ProfileEdit(SessionProfile Profile, bool PinCleared, bool HostFilesPinCleared = false);
