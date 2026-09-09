// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia;
using Avalonia.Headless;
using LizTerm.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace LizTerm.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<LizTerm.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
