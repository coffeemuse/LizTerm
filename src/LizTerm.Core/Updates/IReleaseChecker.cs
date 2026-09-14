// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Updates;

public interface IReleaseChecker
{
    Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken);
}
