// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Files;
using LizTerm.App.Updates;

namespace LizTerm.App.Views;

/// <summary>Reports one release-check outcome — newer available, up to date, or a failure. App owns the one
/// instance at a time (App.axaml.cs's _updateCheck), the _about/_preferences shape.</summary>
public partial class UpdateCheckWindow : Window
{
    private readonly UpdateCheckResult _result;
    private readonly IUriOpener _uriOpener;
    private readonly Action<string>? _onSkip;

    /// <summary>Design-time only.</summary>
    public UpdateCheckWindow() : this(
        new UpdateCheckResult.NewerAvailable("0.6.0", "https://github.com/coffeemuse/LizTerm/releases/tag/v0.6.0"),
        "0.5.2", null)
    { }

    /// <param name="uriOpener">What Download opens the release page through. Null for the platform's, acting through
    /// this window, which is what App wants; tests pass a fake.</param>
    public UpdateCheckWindow(UpdateCheckResult result, string currentVersion, Action<string>? onSkip, IUriOpener? uriOpener = null)
    {
        InitializeComponent();
        _result = result;
        _onSkip = onSkip;
        _uriOpener = uriOpener ?? new AvaloniaUriOpener(this);

        switch (result)
        {
            case UpdateCheckResult.NewerAvailable newer:
                MessageText.Text = $"LizTerm {newer.Version} is available. You have {currentVersion}.";
                DownloadButton.IsVisible = true;
                RemindButton.IsVisible = true;
                SkipButton.IsVisible = true;
                RemindButton.IsDefault = true;
                RemindButton.IsCancel = true;
                break;
            case UpdateCheckResult.UpToDate:
                MessageText.Text = $"You're up to date ({currentVersion}).";
                OkButton.IsVisible = true;
                OkButton.IsDefault = true;
                OkButton.IsCancel = true;
                break;
            case UpdateCheckResult.Failed failed:
                MessageText.Text = "Couldn't check for updates: " + failed.Reason;
                OkButton.IsVisible = true;
                OkButton.IsDefault = true;
                OkButton.IsCancel = true;
                break;
        }
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        var page = ((UpdateCheckResult.NewerAvailable)_result).HtmlUrl;
        if (await LinkOpening.TryOpenAsync(_uriOpener, page))
        {
            Close();
            return;
        }
        FallbackText.Text = LinkOpening.NotOpened(page);
        FallbackText.IsVisible = true;
    }

    private void OnSkipClick(object? sender, RoutedEventArgs e)
    {
        _onSkip?.Invoke(((UpdateCheckResult.NewerAvailable)_result).Version);
        Close();
    }

    private void OnRemindClick(object? sender, RoutedEventArgs e) => Close();
    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
