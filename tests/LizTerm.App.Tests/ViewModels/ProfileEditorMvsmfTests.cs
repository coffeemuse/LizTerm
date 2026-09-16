// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.ViewModels;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Session;

namespace LizTerm.App.Tests.ViewModels;

public class ProfileEditorMvsmfTests
{
    private static readonly CertificatePin Pin = new("AA:BB", "CN=proxy", "pem");

    private static SessionProfile Rest(string? url = "http://mvs:8080/zosmf", string? userid = "MVSCE02", CertificatePin? pin = null) =>
        new() { Name = "MVS/CE", Host = "mvs", Port = 3270, HostFilesUrl = url, HostFilesUserid = userid, HostFilesPinnedCertificate = pin };

    private sealed class Tester
    {
        public List<(string Name, Uri Url, string? Userid, CertificatePin? Pin)> Calls { get; } = [];
        public Exception? Failure { get; set; }
        public TaskCompletionSource? Gate { get; set; }

        public async Task<HostServerInfo> TestAsync(string name, Uri url, string? userid, CertificatePin? pin, CancellationToken token)
        {
            Calls.Add((name, url, userid, pin));
            if (Gate is { } gate) await gate.Task;
            if (Failure is not null) throw Failure;
            return new HostServerInfo("mvsMF", "1.0.0-dev", "MVS 3.8j");
        }
    }

    [Fact]
    public void An_existing_profile_round_trips_its_rest_fields()
    {
        var original = Rest(pin: Pin);
        var vm = new ProfileEditorViewModel(original);

        Assert.Equal("http://mvs:8080/zosmf", vm.MvsmfUrl);
        Assert.Equal("MVSCE02", vm.MvsmfUserid);
        Assert.True(vm.HasMvsmfPin);
        Assert.Equal("Pinned certificate: SHA-256 AA:BB (CN=proxy)", vm.MvsmfPinText);
        Assert.Equal(original, vm.TryBuild());
    }

    [Fact]
    public void A_url_that_normalises_to_the_pinned_one_keeps_the_pin()
    {
        var vm = new ProfileEditorViewModel(Rest(url: "http://h:8080/zosmf", pin: Pin));

        vm.MvsmfUrl = "http://h:8080";
        Assert.True(vm.HasMvsmfPin);

        vm.MvsmfUrl = "http://other:8080";
        Assert.False(vm.HasMvsmfPin);

        vm.MvsmfUrl = "HTTP://H:8080/zosmf/";
        Assert.True(vm.HasMvsmfPin);
    }

    [Fact]
    public void The_url_is_normalised_and_the_userid_upper_cased_on_save()
    {
        var vm = new ProfileEditorViewModel(Rest(url: null, userid: null)) { MvsmfUrl = " http://mvs:8080 ", MvsmfUserid = " ibmuser " };

        var built = vm.TryBuild()!;

        Assert.Equal("http://mvs:8080/zosmf", built.HostFilesUrl);
        Assert.Equal("IBMUSER", built.HostFilesUserid);
    }

    [Fact]
    public void A_blank_url_saves_none_and_no_rest_pin()
    {
        var vm = new ProfileEditorViewModel(Rest(pin: Pin)) { MvsmfUrl = "  ", MvsmfUserid = "" };

        var built = vm.TryBuild()!;

        Assert.Null(built.HostFilesUrl);
        Assert.Null(built.HostFilesUserid);
        Assert.Null(built.HostFilesPinnedCertificate);
    }

    [Theory]
    [InlineData("ftp://mvs", "MVSCE02", "mvsMF URL: Enter an http:// or https:// URL.")]
    [InlineData("http://u:p@mvs", "MVSCE02", "mvsMF URL: Leave the userid and password out of the URL.")]
    [InlineData("http://mvs", "TOOLONGID", "The mvsMF userid must be 1 to 8 letters, digits or # $ @, starting with a letter or # $ @.")]
    [InlineData("http://mvs", "1ABC", "The mvsMF userid must be 1 to 8 letters, digits or # $ @, starting with a letter or # $ @.")]
    [InlineData("http://mvs", "AB-C", "The mvsMF userid must be 1 to 8 letters, digits or # $ @, starting with a letter or # $ @.")]
    public void Bad_rest_fields_are_refused_on_save(string url, string userid, string expected)
    {
        var vm = new ProfileEditorViewModel(Rest()) { MvsmfUrl = url, MvsmfUserid = userid };

        Assert.Null(vm.TryBuild());
        Assert.Equal(expected, vm.ValidationMessage);
    }

    [Fact]
    public void Editing_the_url_hides_the_pin_and_restoring_it_brings_it_back()
    {
        var vm = new ProfileEditorViewModel(Rest(pin: Pin));

        vm.MvsmfUrl = "https://proxy/zosmf";
        Assert.False(vm.HasMvsmfPin);
        Assert.Null(vm.TryBuild()!.HostFilesPinnedCertificate);

        vm.MvsmfUrl = "HTTP://MVS:8080/zosmf/";
        Assert.True(vm.HasMvsmfPin);
    }

    [Fact]
    public void Forget_clears_the_rest_pin_and_says_so()
    {
        var vm = new ProfileEditorViewModel(Rest(pin: Pin));

        vm.ForgetMvsmfPinCommand.Execute(null);

        Assert.False(vm.HasMvsmfPin);
        Assert.True(vm.MvsmfPinCleared);
        Assert.False(vm.PinCleared);
        vm.MvsmfUrl = "http://mvs:8080/zosmf";
        Assert.False(vm.HasMvsmfPin);
    }

    [Fact]
    public async Task Test_reports_what_the_host_is()
    {
        var tester = new Tester();
        var vm = new ProfileEditorViewModel(Rest(url: null, pin: null), tester.TestAsync) { MvsmfUrl = "http://mvs:8080", MvsmfUserid = "mvsce02" };

        await vm.TestMvsmfCommand.ExecuteAsync(null);

        Assert.Equal("✓ Connected: mvsMF 1.0.0-dev on MVS 3.8j", vm.MvsmfTestResult);
        var call = Assert.Single(tester.Calls);
        Assert.Equal(("MVS/CE", "http://mvs:8080/zosmf", "MVSCE02"), (call.Name, call.Url.ToString(), call.Userid));
        Assert.Null(vm.ValidationMessage);
    }

    [Fact]
    public async Task Test_uses_the_rest_pin_the_editor_shows()
    {
        var tester = new Tester();
        var vm = new ProfileEditorViewModel(Rest(pin: Pin), tester.TestAsync);

        await vm.TestMvsmfCommand.ExecuteAsync(null);

        Assert.Equal(Pin, tester.Calls.Single().Pin);
    }

    [Fact]
    public async Task Test_without_a_url_or_with_a_bad_one_says_so_without_asking()
    {
        var tester = new Tester();
        var vm = new ProfileEditorViewModel(Rest(url: null), tester.TestAsync);

        await vm.TestMvsmfCommand.ExecuteAsync(null);
        Assert.Equal("✗ Enter the mvsMF URL first.", vm.MvsmfTestResult);

        vm.MvsmfUrl = "ftp://mvs";
        await vm.TestMvsmfCommand.ExecuteAsync(null);
        Assert.Equal("✗ mvsMF URL: Enter an http:// or https:// URL.", vm.MvsmfTestResult);
        Assert.Empty(tester.Calls);
        Assert.Null(vm.ValidationMessage);
    }

    [Theory]
    [InlineData(HostFileErrorKind.Unauthenticated, "The host rejected the userid or password.", "✗ The host rejected the userid or password.")]
    [InlineData(HostFileErrorKind.Unreachable, "Server information: cannot reach the host (refused).", "✗ Server information: cannot reach the host (refused).")]
    [InlineData(HostFileErrorKind.CertificateRejected, "x", "✗ The host's certificate is not trusted. Open the mvsMF Browser from a session to review it.")]
    public async Task Test_failures_are_reported_in_words(HostFileErrorKind kind, string message, string expected)
    {
        var tester = new Tester { Failure = new HostFileException(kind, message) };
        var vm = new ProfileEditorViewModel(Rest(), tester.TestAsync);

        await vm.TestMvsmfCommand.ExecuteAsync(null);

        Assert.Equal(expected, vm.MvsmfTestResult);
        Assert.False(vm.IsTestingMvsmf);
    }

    [Fact]
    public async Task Test_is_busy_while_it_runs_and_editing_the_url_clears_the_result()
    {
        var tester = new Tester { Gate = new TaskCompletionSource() };
        var vm = new ProfileEditorViewModel(Rest(), tester.TestAsync);

        var testing = vm.TestMvsmfCommand.ExecuteAsync(null);
        Assert.True(vm.IsTestingMvsmf);
        Assert.Equal("⟳ Testing…", vm.MvsmfTestResult);
        Assert.False(vm.TestMvsmfCommand.CanExecute(null));
        tester.Gate.SetResult();
        await testing;
        Assert.True(vm.TestMvsmfCommand.CanExecute(null));

        vm.MvsmfUrl = "http://other";
        Assert.Null(vm.MvsmfTestResult);
    }

    [Fact]
    public async Task A_url_edit_during_a_test_discards_its_result()
    {
        var tester = new Tester { Gate = new TaskCompletionSource() };
        var vm = new ProfileEditorViewModel(Rest(), tester.TestAsync);

        var testing = vm.TestMvsmfCommand.ExecuteAsync(null);
        vm.MvsmfUrl = "http://other";
        tester.Gate.SetResult();
        await testing;

        Assert.Null(vm.MvsmfTestResult);
        Assert.False(vm.IsTestingMvsmf);
    }

    [Fact]
    public async Task A_userid_edit_or_forget_clears_the_result()
    {
        var tester = new Tester();
        var vm = new ProfileEditorViewModel(Rest(pin: Pin), tester.TestAsync);

        await vm.TestMvsmfCommand.ExecuteAsync(null);
        Assert.NotNull(vm.MvsmfTestResult);
        vm.MvsmfUserid = "IBMUSER";
        Assert.Null(vm.MvsmfTestResult);

        await vm.TestMvsmfCommand.ExecuteAsync(null);
        Assert.NotNull(vm.MvsmfTestResult);
        vm.ForgetMvsmfPinCommand.Execute(null);
        Assert.Null(vm.MvsmfTestResult);
    }

    [Fact]
    public void Without_a_tester_there_is_no_test()
    {
        var vm = new ProfileEditorViewModel(Rest());
        Assert.False(vm.TestMvsmfCommand.CanExecute(null));
    }
}
