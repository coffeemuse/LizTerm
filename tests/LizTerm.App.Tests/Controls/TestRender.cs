// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace LizTerm.App.Tests.Controls;

/// <summary>The one spelling of "force a frame" for the TerminalScreen tests in this namespace. The headless
/// render loop draws on demand, so a "did it repaint" assertion (RunPlanBuilds, or a paint that must not throw)
/// passes vacuously without this — nothing renders after the first frame otherwise.</summary>
internal static class TestRender
{
    public static void Repaint(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
    }
}
