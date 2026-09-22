// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

/// <summary>A <see cref="UssBrowserViewModel"/> over the fake host, on its own runner, seeded by
/// <see cref="Standard"/>: the home directory <c>/u/ibmuser</c> holding <c>notes</c> (a subdirectory
/// <c>drafts</c>, two text files and a binary one) and <c>old</c>, beside <c>/tmp</c> and <c>/u/mvsce02</c>.</summary>
internal sealed record UssTestHost(
    UssBrowserViewModel Vm,
    BrowserOperations Ops,
    FakeHostFileService Host,
    FakeFilePicker Picker,
    HostFileAccess Access)
{
    public static UssTestHost Create(string? userid = "IBMUSER", Action<FakeHostFileService>? seed = null)
    {
        var host = new FakeHostFileService();
        (seed ?? Standard)(host);
        var access = new HostFileAccess(
            new SessionProfile { Name = "MVS/CE", Host = "mvs", HostFilesUrl = "http://mvs:8080", HostFilesUserid = userid },
            (_, _, _) => host, savePin: null);
        var ops = new BrowserOperations();
        var picker = new FakeFilePicker();
        var vm = new UssBrowserViewModel(ops, access, access.Connect(new FakeCredentialPrompt(), new FakeCertificatePrompt()), picker, action => action());
        return new UssTestHost(vm, ops, host, picker, access);
    }

    public static void Standard(FakeHostFileService host)
    {
        host.AddDirectory("/tmp");
        host.AddDirectory("/u/mvsce02");
        host.AddDirectory("/u/ibmuser/old");
        host.AddDirectory("/u/ibmuser/notes/drafts");
        host.AddFile("/u/ibmuser/notes/README.txt", "hello", "world");
        host.AddFile("/u/ibmuser/notes/todo.md", "- x");
        host.AddBinaryFile("/u/ibmuser/notes/data.bin", [1, 2, 3]);
    }

    /// <summary>Types <paramref name="path"/> into the path row and presses Go.</summary>
    public async Task ListAsync(string path)
    {
        Vm.Path = path;
        await Vm.GoCommand.ExecuteAsync(null);
    }

    /// <summary>Sets the file selection the window would push.</summary>
    public void Select(params string[] files) => Vm.SetSelectedFiles(Vm.Files.Where(f => files.Contains(f.Name)));

    /// <summary>Waits for the running operation to put its question up, or to end without one.</summary>
    public async Task<UssTestHost> AskedAsync(Task running)
    {
        await Wait.UntilAsync(() => Ops.Confirmation is not null || running.IsCompleted, "the question");
        return this;
    }
}
