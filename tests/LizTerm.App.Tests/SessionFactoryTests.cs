// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Status;
using LizTerm.Backend.B3270;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests;

public class SessionFactoryTests
{
    /// <summary>With the override pointed at a file that does not exist and the factory pointed at an empty
    /// directory instead of the app's own base directory, the factory must still hand back a session whose first
    /// connect reports the locator's explanation.</summary>
    [Fact]
    public async Task Missing_engine_is_reported_by_the_first_connect_not_by_Create()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "lizterm-missing-" + Guid.NewGuid().ToString("N"), "b3270");
        var emptyDirectory = Directory.CreateTempSubdirectory("lizterm-empty-");
        try
        {
            await using var session = SessionFactory.Create(new SessionProfile { Name = "t", Host = "h" }, bogus, emptyDirectory.FullName);
            var ex = await Assert.ThrowsAsync<BackendUnavailableException>(() => session.ConnectAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Contains("Looked in", ex.Message);
            Assert.Contains(bogus, ex.Message);
        }
        finally
        {
            emptyDirectory.Delete(recursive: true);
        }
    }

    /// <summary>Regression: the not-found sentinel carried EngineSource.Bundled, so Help > About told a user
    /// whose override had broken that the engine ships inside the app, with a blank path underneath.</summary>
    [Fact]
    public async Task A_missing_engine_is_not_reported_as_bundled()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "lizterm-missing-" + Guid.NewGuid().ToString("N"), "b3270");
        var emptyDirectory = Directory.CreateTempSubdirectory("lizterm-empty-");
        try
        {
            await using var session = SessionFactory.Create(new SessionProfile { Name = "t", Host = "h" }, bogus, emptyDirectory.FullName);
            Assert.Equal(EngineSource.Unknown, session.Engine.Source);
            Assert.Equal("b3270, not found", StatusFormatter.Engine(session.Engine, SessionFactory.OverrideOrigin));
        }
        finally
        {
            emptyDirectory.Delete(recursive: true);
        }
    }

    /// <summary>The two tests above drive the seam, so nothing else would notice the public overload drifting off
    /// the default the app actually ships with — passing Environment.CurrentDirectory, or reading the wrong
    /// variable. Both public entry points resolve through DefaultLocation; this pins that they agree.</summary>
    [Fact]
    public async Task The_public_Create_resolves_through_the_same_default_as_CheckBackend()
    {
        var (overridePath, baseDirectory) = SessionFactory.DefaultLocation;
        Assert.Equal(Environment.GetEnvironmentVariable(SessionFactory.OverrideOrigin), overridePath);
        Assert.Equal(AppContext.BaseDirectory, baseDirectory);

        var profile = new SessionProfile { Name = "t", Host = "h" };
        await using var viaDefault = SessionFactory.Create(profile);
        await using var viaSeam = SessionFactory.Create(profile, overridePath, baseDirectory);
        Assert.Equal(viaSeam.Engine, viaDefault.Engine);
    }

    /// <summary>The backend defaults to no anchors on purpose, so the app is the thing that has to supply the real
    /// store. Without this the whole plan is inert in the shipping product while every test still passes.</summary>
    [Fact]
    public async Task The_session_it_builds_verifies_against_the_system_trust_anchors()
    {
        var profile = new SessionProfile(Name: "trust", Host: "h", UseTls: true);

        await using var session = (B3270Session)SessionFactory.Create(profile, overridePath: "/nonexistent/b3270", Path.GetTempPath());

        Assert.Same(SystemTrustAnchors.Default, session.TrustAnchors);
    }

    /// <summary>About must render something for every outcome, so the not-found case is a value rather than an
    /// exception — and it says Unknown rather than borrowing a provenance it does not have, which is what
    /// StatusFormatter renders as "b3270, not found".</summary>
    [Fact]
    public void CheckBackendOrUnknown_reports_a_missing_engine_as_unknown_rather_than_throwing()
    {
        var empty = Directory.CreateTempSubdirectory("lizterm-factory-").FullName;
        try
        {
            var engine = SessionFactory.CheckBackendOrUnknown("/nonexistent/b3270", empty);

            Assert.Equal(EngineSource.Unknown, engine.Source);
            Assert.Equal("b3270", engine.Name);
            Assert.Null(engine.Version);
            Assert.Equal("", engine.Path);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    /// <summary>The other arm of the same exception, which About would otherwise render as "b3270, not found".
    /// A binary that is *there* and only needs chmod is not a missing one: B3270Locator.Find throws the same type
    /// for both, which is what Candidates exists to tell apart, and About is the screen a user opens to find out
    /// which of the two they have. So the source must survive — "bundled" or the override's name, not "not
    /// found". The path survives with it so this answer and a session's cannot differ, though nothing displays it
    /// (#75); the file to chmod is named by the locator's exception, not by About.</summary>
    [Fact]
    public void CheckBackendOrUnknown_names_an_engine_that_is_present_but_not_executable()
    {
        if (OperatingSystem.IsWindows()) return; // The locator has no executable-bit arm on Windows.
        var directory = Directory.CreateTempSubdirectory("lizterm-factory-").FullName;
        var present = Path.Combine(directory, "b3270");
        try
        {
            File.WriteAllText(present, "not really an engine");
            File.SetUnixFileMode(present, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var engine = SessionFactory.CheckBackendOrUnknown(present, directory);

            Assert.Equal(EngineSource.Override, engine.Source);
            Assert.Equal(present, engine.Path);
            Assert.Null(engine.Version);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>And a session gets the same answer, so the status bar and About cannot disagree about the same
    /// binary. The connect still fails: only what the engine is *called* changes here.</summary>
    [Fact]
    public async Task A_session_names_the_same_present_but_unusable_engine()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Directory.CreateTempSubdirectory("lizterm-factory-").FullName;
        var present = Path.Combine(directory, "b3270");
        try
        {
            File.WriteAllText(present, "not really an engine");
            File.SetUnixFileMode(present, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            await using var session = SessionFactory.Create(new SessionProfile { Name = "t", Host = "h" }, present, directory);

            Assert.Equal(present, session.Engine.Path);
            Assert.Equal(EngineSource.Override, session.Engine.Source);
            Assert.Equal(SessionFactory.CheckBackendOrUnknown(present, directory), session.Engine);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
