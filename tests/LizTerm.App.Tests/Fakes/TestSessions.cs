// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Sessions;
using LizTerm.App.ViewModels;
using LizTerm.Core.Profiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.Fakes;

/// <summary>A SessionEntry over a FakeEmulatorSession and a FakeSessionHost, for everything that reads the
/// session list without a real window.</summary>
public static class TestSessions
{
    public static (SessionEntry Entry, FakeEmulatorSession Session, FakeSessionHost Host) Create(
        string name, string host = "host.local", ConnectionState state = ConnectionState.Connected3270,
        bool isSaved = true, string? note = null, string[]? tags = null)
    {
        var session = new FakeEmulatorSession
        {
            Profile = new SessionProfile { Name = name, Host = host, Port = 3270, Note = note, Tags = TagSet.From(tags) },
            ConnectionState = state,
        };
        var viewModel = new SessionViewModel(session, action => action(), new FakeTextClipboard());
        var sessionHost = new FakeSessionHost();
        var entry = new SessionEntry(viewModel, new ProfileRow(session.Profile, TagRegistry.Empty, isSaved), isSaved, sessionHost);
        return (entry, session, sessionHost);
    }
}
