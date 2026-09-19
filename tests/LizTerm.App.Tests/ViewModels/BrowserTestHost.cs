// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.HostFiles;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

internal sealed record BrowserTestHost(
    MvsmfBrowserViewModel Vm,
    FakeHostFileService Host,
    FakeFilePicker Picker,
    FakeCertificatePrompt Certificates,
    FakeCredentialPrompt Credentials,
    HostFileAccess Access)
{
    public int GuideOpened { get; set; }

    public static BrowserTestHost Create(string? userid = "MVSCE02", Action<FakeHostFileService>? seed = null, int pageSize = 500)
    {
        var host = new FakeHostFileService();
        (seed ?? Standard)(host);
        var access = new HostFileAccess(
            new SessionProfile { Name = "MVS/CE", Host = "mvs", HostFilesUrl = "http://mvs:8080", HostFilesUserid = userid },
            (_, _, _) => host, savePin: null);
        var credentials = new FakeCredentialPrompt();
        var certificates = new FakeCertificatePrompt();
        var picker = new FakeFilePicker();
        BrowserTestHost? made = null;
        var vm = new MvsmfBrowserViewModel(access, access.Connect(credentials, certificates), picker, action => action(),
            () =>
            {
                made!.GuideOpened++;
                return Task.CompletedTask;
            });
        vm.PageSize = pageSize;
        vm.FilterDelay = TimeSpan.FromMilliseconds(20);
        made = new BrowserTestHost(vm, host, picker, certificates, credentials, access);
        return made;
    }

    /// <summary>The standard hosts plus five more datasets and a PDS of five members, for paging at a page of two.</summary>
    public static void Large(FakeHostFileService host)
    {
        Standard(host);
        host.AddDataset("MVSCE02.BIG", members: ["ALLOC", "COMPILE", "HELLO", "LINK", "RUN"]);
        for (var i = 1; i <= 4; i++) host.AddDataset($"MVSCE02.D{i}", dsorg: "PS");
    }

    public async Task LoadMoreDatasetsAsync() => await Vm.LoadMoreDatasetsCommand.ExecuteAsync(null);

    public async Task LoadMoreMembersAsync() => await Vm.LoadMoreMembersCommand.ExecuteAsync(null);

    /// <summary>Sets the member filter and waits for the host-side listing it may start.</summary>
    public async Task FilterMembersAsync(string text)
    {
        Vm.MemberFilter = text;
        await Wait.UntilAsync(() => !Vm.IsBusy && !Vm.IsFilterPending, "the filtered member list");
    }

    /// <summary>A PDS of three members, a load library, a sequential dataset and one the preview cannot open.</summary>
    public static void Standard(FakeHostFileService host)
    {
        host.AddDataset("MVSCE02.CNTL", members: ["ALLOC", "COMPILE", "HELLO"]);
        host.AddDataset("MVSCE02.LOAD", recfm: "U", lrecl: 0, blksize: 19069, members: ["PROG"]);
        host.AddDataset("MVSCE02.UFSHOME", dsorg: "PS", recfm: "U", lrecl: 0, blksize: 4096);
        host.AddDataset("MVSCE02.DB", dsorg: "DA", recfm: "F", lrecl: 4096, blksize: 4096);
    }

    public async Task ListAsync() => await Vm.ListCommand.ExecuteAsync(null);

    /// <summary>Lists if nothing is listed yet, loading more pages until <paramref name="dataset"/> shows.</summary>
    public async Task ChooseAsync(string dataset)
    {
        if (Vm.Datasets.Count == 0) await ListAsync();
        while (Vm.Datasets.All(d => d.Name != dataset) && Vm.HasMoreDatasets) await LoadMoreDatasetsAsync();
        Vm.SelectedDataset = Vm.Datasets.Single(d => d.Name == dataset);
        await Wait.UntilAsync(() => !Vm.IsBusy, "the member list");
    }

    public void Select(params string[] members) =>
        Vm.SetSelectedMembers(Vm.Members.Where(m => members.Contains(m.Name)));
}
