// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App;
using LizTerm.App.Tests.Fakes;
using LizTerm.App.ViewModels;

namespace LizTerm.App.Tests.ViewModels;

public class SessionViewModelLinkTests
{
    private static (SessionViewModel Vm, FakeUriOpener Opener) Build()
    {
        var opener = new FakeUriOpener();
        var vm = new SessionViewModel(new FakeEmulatorSession(), action => action(), new FakeTextClipboard(),
            uriOpener: opener);
        return (vm, opener);
    }

    [Fact]
    public async Task Opening_a_link_hands_the_url_to_the_opener()
    {
        var (vm, opener) = Build();

        await vm.OpenLinkCommand.ExecuteAsync(ProjectLinks.NewIssue);

        Assert.Equal([ProjectLinks.NewIssue], opener.Opened);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task A_platform_that_cannot_open_the_url_names_it_in_the_banner()
    {
        var (vm, opener) = Build();
        opener.Result = false;

        await vm.OpenLinkCommand.ExecuteAsync(ProjectLinks.Repository);

        Assert.Equal($"Could not open a browser. The page is at {ProjectLinks.Repository}.", vm.ErrorMessage);
    }

    [Fact]
    public async Task An_opener_that_throws_is_treated_as_a_failure_to_open()
    {
        var (vm, opener) = Build();
        opener.Exception = new InvalidOperationException("no browser");

        await vm.OpenLinkCommand.ExecuteAsync(ProjectLinks.Releases);

        Assert.Equal($"Could not open a browser. The page is at {ProjectLinks.Releases}.", vm.ErrorMessage);
    }
}
