// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Files;

/// <summary>Opens a web page or a local file in whatever the platform has associated with it. Injected like
/// <see cref="IFolderOpener"/>, which it sits beside.</summary>
public interface IUriOpener
{
    /// <returns>False when the platform could not open it; the caller then shows the URL instead.</returns>
    Task<bool> OpenAsync(Uri uri);

    /// <summary>A path rather than a file:// URI: the extracted user guide lives under the system temp
    /// directory, which on Windows routinely contains a space, and handing the platform a FileInfo avoids URI
    /// escaping rather than getting it right.</summary>
    Task<bool> OpenFileAsync(string path);
}
