// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Tests.Fakes;

namespace LizTerm.App.Tests.Files;

/// <summary>The fake's contract for the two pickers mvsMF Access adds; the Avalonia side is a thin call into
/// the platform's storage provider and has no headless test.</summary>
public class FakeFilePickerTests
{
    [Fact]
    public async Task Several_files_and_a_folder_come_back_as_set_and_are_logged()
    {
        var picker = new FakeFilePicker { Results = ["/a.jcl", "/b.jcl"], FolderResult = "/out" };

        Assert.Equal(new[] { "/a.jcl", "/b.jcl" }, await picker.PickFilesToSendAsync("Upload to X"));
        Assert.Equal("/out", await picker.PickFolderAsync("Download to"));
        Assert.Equal(new[] { "open-many:Upload to X", "folder:Download to" }, picker.Calls);
    }

    [Fact]
    public async Task The_exception_fails_the_new_pickers_too()
    {
        var picker = new FakeFilePicker { Exception = new IOException("no dialog") };
        await Assert.ThrowsAsync<IOException>(() => picker.PickFilesToSendAsync("t"));
        await Assert.ThrowsAsync<IOException>(() => picker.PickFolderAsync("t"));
    }
}
