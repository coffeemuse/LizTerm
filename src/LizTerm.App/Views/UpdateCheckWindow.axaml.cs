// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using Avalonia.Controls;
using Avalonia.Interactivity;
using LizTerm.App.Updates;

namespace LizTerm.App.Views;

/// <summary>Reports one release-check outcome — newer available, up to date, or a failure. App owns the one
/// instance at a time (App.axaml.cs's _updateCheck), the _about/_preferences shape.</summary>
public partial class UpdateCheckWindow : Window
{
    private readonly UpdateCheckResult _result;
    private readonly Func<string, Task<bool>>? _onDownload;
    private readonly Action<string>? _onSkip;

    /// <summary>Design-time only.</summary>
    public UpdateCheckWindow() : this(
        new UpdateCheckResult.NewerAvailable("0.6.0", "https://github.com/coffeemuse/LizTerm/releases/tag/v0.6.0"),
        "0.5.2", null, null)
    { }

    public UpdateCheckWindow(UpdateCheckResult result, string currentVersion, Func<string, Task<bool>>? onDownload, Action<string>? onSkip)
    {
        InitializeComponent();
        _result = result;
        _onDownload = onDownload;
        _onSkip = onSkip;

        switch (result)
        {
            case UpdateCheckResult.NewerAvailable newer:
                MessageText.Text = $"LizTerm {newer.Version} is available. You have {currentVersion}.";
                DownloadButton.IsVisible = true;
                RemindButton.IsVisible = true;
                SkipButton.IsVisible = true;
                DownloadButton.IsDefault = true;
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
        var newer = (UpdateCheckResult.NewerAvailable)_result;
        bool opened;
        try
        {
            opened = _onDownload is null || await _onDownload(newer.HtmlUrl);
        }
        catch (Exception)
        {
            // Fall through to naming the URL, the same as _onDownload returning false — OpenLinkAsync's shape.
            opened = false;
        }
        if (opened)
        {
            Close();
            return;
        }
        FallbackText.Text = "Could not open a browser. The release is at " + newer.HtmlUrl;
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
