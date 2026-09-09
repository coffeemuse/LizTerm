// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Files;

/// <summary>Opens a directory in the OS file manager. Injected like <see cref="IFilePicker"/>.</summary>
public interface IFolderOpener
{
    /// <returns>False when the platform could not open it; the caller then shows the path instead.</returns>
    Task<bool> OpenAsync(string directory);
}
