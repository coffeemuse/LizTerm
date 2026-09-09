// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.Core.Session;

namespace LizTerm.App.ViewModels;

public partial class ProfileEditorViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _portText = "23";
    [ObservableProperty] private bool _useTls;
    [ObservableProperty] private bool _verifyCertificate = true;
    [ObservableProperty] private int _model = 2;
    [ObservableProperty] private bool _extended = true;
    [ObservableProperty] private string _codePage = "cp037";
    [ObservableProperty] private string _luName = "";
    [ObservableProperty] private bool _destructiveBackspace = true;
    [ObservableProperty] private string? _validationMessage;

    /// <summary>The pin the profile carries, shown read-only. Forget clears it and Save then writes the profile
    /// without it, which is the only way back from a pin to the engine's default trust (spec 5.5). A pin belongs to
    /// the host and port it was taken from: editing either drops it, and restoring them brings it back until Save.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPinnedCertificate), nameof(PinnedCertificateText))]
    private CertificatePin? _pinnedCertificate;
    private CertificatePin? _pinnedFor;
    private readonly string _pinnedHost = "";
    private readonly string _pinnedPortText = "";

    public bool HasPinnedCertificate => PinnedCertificate is not null;
    public string? PinnedCertificateText =>
        PinnedCertificate is { } pin ? $"Pinned certificate: SHA-256 {pin.Sha256} ({pin.Subject})" : null;

    public int[] Models { get; } = [2, 3, 4, 5];
    public bool IsNew { get; }

    public ProfileEditorViewModel(SessionProfile? existing)
    {
        IsNew = existing is null;
        if (existing is null) return;
        _name = existing.Name;
        _host = existing.Host;
        _portText = existing.Port.ToString();
        _useTls = existing.UseTls;
        _verifyCertificate = existing.VerifyCertificate;
        _model = existing.Model;
        _extended = existing.Extended;
        _codePage = existing.CodePage;
        _luName = existing.LuName ?? "";
        _destructiveBackspace = existing.DestructiveBackspace;
        _pinnedCertificate = existing.PinnedCertificate;
        _pinnedFor = existing.PinnedCertificate;
        _pinnedHost = existing.Host;
        _pinnedPortText = existing.Port.ToString();
    }

    partial void OnUseTlsChanged(bool value)
    {
        if (value && PortText == "23") PortText = "992";
        else if (!value && PortText == "992") PortText = "23";
    }

    partial void OnHostChanged(string value) => RefreshPin();

    partial void OnPortTextChanged(string value) => RefreshPin();

    private void RefreshPin() =>
        PinnedCertificate = string.Equals(Host.Trim(), _pinnedHost, StringComparison.OrdinalIgnoreCase) && PortText.Trim() == _pinnedPortText ? _pinnedFor : null;

    /// <summary>The user pressed Forget. The picker needs this because a null PinnedCertificate here can also
    /// mean the editor's copy of the profile simply predates a pin written from a session window.</summary>
    public bool PinCleared { get; private set; }

    [RelayCommand]
    private void ForgetPin()
    {
        _pinnedFor = null;
        PinnedCertificate = null;
        PinCleared = true;
    }

    public SessionProfile? TryBuild()
    {
        if (string.IsNullOrWhiteSpace(Name)) { ValidationMessage = "Give the profile a name."; return null; }
        if (string.IsNullOrWhiteSpace(Host)) { ValidationMessage = "Enter the host name or address."; return null; }
        if (!int.TryParse(PortText.Trim(), out var port) || port < 1 || port > 65535) { ValidationMessage = "Port must be a number from 1 to 65535."; return null; }
        if (string.IsNullOrWhiteSpace(CodePage)) { ValidationMessage = "Enter a code page, for example cp037."; return null; }
        ValidationMessage = null;
        return new SessionProfile
        {
            Name = Name.Trim(),
            Host = Host.Trim(),
            Port = port,
            UseTls = UseTls,
            VerifyCertificate = VerifyCertificate,
            PinnedCertificate = PinnedCertificate,
            Model = Model,
            Extended = Extended,
            CodePage = CodePage.Trim(),
            LuName = string.IsNullOrWhiteSpace(LuName) ? null : LuName.Trim(),
            DestructiveBackspace = DestructiveBackspace,
        };
    }
}
