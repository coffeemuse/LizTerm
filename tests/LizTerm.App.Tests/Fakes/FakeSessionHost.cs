// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Sessions;

namespace LizTerm.App.Tests.Fakes;

/// <summary>A host that records Bring() and lets a test simulate what bringing a real window causes, through
/// OnBring (typically calling SessionList.Activated, as a window's Activated event would).</summary>
public sealed class FakeSessionHost : ISessionHost
{
    private bool _keepOnTop;

    public int BringCount { get; private set; }
    public Action? OnBring { get; set; }
    public bool IsMinimized { get; set; }

    public bool KeepOnTop
    {
        get => _keepOnTop;
        set
        {
            if (_keepOnTop == value) return;
            _keepOnTop = value;
            KeepOnTopChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? KeepOnTopChanged;

    public void Bring()
    {
        BringCount++;
        OnBring?.Invoke();
    }
}
