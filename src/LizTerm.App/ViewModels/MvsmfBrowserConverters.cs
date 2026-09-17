// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Data.Converters;

namespace LizTerm.App.ViewModels;

public static class MvsmfBrowserConverters
{
    /// <summary>A row the preview cannot open is drawn at half strength; its name also says so in words.</summary>
    public static readonly IValueConverter DimUnlessTrue =
        new FuncValueConverter<bool, double>(supported => supported ? 1.0 : 0.5);
}
