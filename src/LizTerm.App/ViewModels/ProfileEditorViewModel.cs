// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
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
    [ObservableProperty] private string _keepAliveText = "60";
    [ObservableProperty] private bool _autoReconnect;
    [ObservableProperty] private string _oversize = "";
    [ObservableProperty] private string? _validationMessage;

    /// <summary>The pin the profile carries, shown read-only. Forget clears it and Save then writes the profile
    /// without it, which is the only way back from a pin to the engine's default trust (spec 5.5). A pin belongs to
    /// the host and port it was taken from: editing either drops it, and restoring them brings it back until Save.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPinnedCertificate), nameof(PinnedCertificateText))]
    private CertificatePin? _pinnedCertificate;
    private CertificatePin? _pinnedFor;
    private readonly string _pinnedHost = "";
    private readonly int _pinnedPort;

    public bool HasPinnedCertificate => PinnedCertificate is not null;
    public string? PinnedCertificateText =>
        PinnedCertificate is { } pin ? $"Pinned certificate: SHA-256 {pin.Sha256} ({pin.Subject})" : null;

    /// <summary>The catalogue, seeded with this profile's own value when it falls outside it — a hand-edited
    /// file, or a model a newer engine adds. A ComboBox bound SelectedItem has nothing to select otherwise, and
    /// merely opening the editor would drop a working profile's setting. Model gets no validation in TryBuild,
    /// so nothing else would catch it either.</summary>
    public IReadOnlyList<TerminalModel> TerminalModels { get; }

    /// <summary>Same seeding rule as <see cref="TerminalModels"/>.</summary>
    public IReadOnlyList<CodePage> CodePages { get; }

    /// <summary>A view over <see cref="Model"/>, which stays the property TryBuild reads.</summary>
    public TerminalModel SelectedModel
    {
        get => TerminalModels.FirstOrDefault(m => m.Number == Model) ?? TerminalModels[0];
        set { if (value is not null) Model = value.Number; OnPropertyChanged(); }
    }

    /// <summary>A view over <see cref="CodePage"/>, which stays the property TryBuild reads.</summary>
    public CodePage SelectedCodePage
    {
        get => CodePages.FirstOrDefault(p => string.Equals(p.Name, CodePage, StringComparison.OrdinalIgnoreCase)) ?? CodePages[0];
        set { if (value is not null) CodePage = value.Name; OnPropertyChanged(); }
    }

    public bool IsNew { get; }

    public ProfileEditorViewModel(SessionProfile? existing)
    {
        IsNew = existing is null;

        var models = TerminalModel.All.ToList();
        if (existing is not null && TerminalModel.Find(existing.Model) is null)
            models.Add(new TerminalModel(existing.Model, 0, 0));
        TerminalModels = models;

        // Qualified with the namespace: the CodePage *property* below (a string) shares its name with the
        // CodePage *type*, and unqualified "CodePage.All"/"CodePage.Find" bind to the property (per C#'s
        // simple-name rules, an instance member always wins over a type of the same name) rather than the type.
        var pages = LizTerm.Core.Session.CodePage.All.ToList();
        if (existing is not null && LizTerm.Core.Session.CodePage.Find(existing.CodePage) is null)
            pages.Add(new CodePage(existing.CodePage, "not in this engine's list"));
        CodePages = pages;

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
        _keepAliveText = existing.KeepAliveSeconds.ToString(CultureInfo.InvariantCulture);
        _autoReconnect = existing.AutoReconnect;
        _oversize = existing.Oversize ?? "";
        _pinnedCertificate = existing.PinnedCertificate;
        _pinnedFor = existing.PinnedCertificate;
        _pinnedHost = existing.Host;
        _pinnedPort = existing.Port;
    }

    partial void OnUseTlsChanged(bool value)
    {
        if (value && PortText == "23") PortText = "992";
        else if (!value && PortText == "992") PortText = "23";
    }

    /// <summary>An oversize legal under one model can be below another's floor — 100x30 clears model 2 and is
    /// short of model 5's floor — so a model change has to re-run the check rather than leave a stale verdict
    /// beside the box. Scoped to a non-blank box on purpose: a blank one says nothing about the geometry, and
    /// clearing an unrelated validation message here would be a second, invisible behaviour.</summary>
    partial void OnModelChanged(int value)
    {
        if (string.IsNullOrWhiteSpace(Oversize)) return;
        ValidationMessage = OversizeGeometry.TryParse(Oversize, SelectedModel, out _, out var error) ? null : error;
    }

    partial void OnHostChanged(string value) => RefreshPin();

    partial void OnPortTextChanged(string value) => RefreshPin();

    private void RefreshPin() =>
        PinnedCertificate = string.Equals(Host.Trim(), _pinnedHost, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(PortText.Trim(), out var port) && port == _pinnedPort
            ? _pinnedFor : null;

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
        // The drop-down cannot produce this, but the seeding above can: a hand-edited file whose codePage is
        // null or blank is seeded into the list verbatim and selected, and CodePage.Trim() below would then
        // throw straight out of OnSaveClick, which has no catch. Blank is refused rather than passed through
        // for the same reason #45 replaced the text box: b3270 warns on stderr and starts on a fallback, so a
        // saved "" is a session that connects normally with quietly wrong characters.
        if (string.IsNullOrWhiteSpace(CodePage)) { ValidationMessage = "Choose a code page."; return null; }
        // NumberStyles.None rejects a sign and surrounding space, so "-1", "+60" and " 60" are refused rather
        // than reaching the engine. The ceiling is a day: a larger one is a typo, not an intention.
        if (!int.TryParse(KeepAliveText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var keepAlive) || keepAlive > 86400)
        {
            ValidationMessage = "Keep-alive must be a whole number of seconds, 0 to 86400 (0 turns it off).";
            return null;
        }
        if (!OversizeGeometry.TryParse(Oversize, SelectedModel, out var oversize, out var oversizeError))
        {
            ValidationMessage = oversizeError;
            return null;
        }
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
            KeepAliveSeconds = keepAlive,
            AutoReconnect = AutoReconnect,
            Oversize = oversize?.ToString(),
        };
    }
}
