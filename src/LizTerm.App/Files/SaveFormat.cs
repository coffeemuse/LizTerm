// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Files;

/// <summary>One choice in an OS Save dialog's file-format popup: what to call it, and the extension it writes.
///
/// Deliberately not Avalonia's own <c>FilePickerFileType</c>, even though that is what this becomes: keeping the
/// type here leaves <see cref="IFilePicker"/> free of Avalonia so the fake in the tests stays two properties and
/// a list, the same reason the rest of that interface trades in strings.</summary>
/// <param name="Label">What the dialog's popup shows, e.g. "Plain text".</param>
/// <param name="Extension">The extension without its dot, e.g. "txt". The OS appends it to the chosen name.</param>
public sealed record SaveFormat(string Label, string Extension);
