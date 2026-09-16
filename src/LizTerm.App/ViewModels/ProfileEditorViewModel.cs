// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core;
using LizTerm.Core.HostFiles;
using LizTerm.Core.Profiles;
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

    /// <summary>The tag names as the user edits them, comma-separated, WITHOUT the reserved tag —
    /// <see cref="IsFavorite"/> owns that one.</summary>
    [ObservableProperty] private string _tagsText = "";

    /// <summary>Whether the profile carries <c>TagRegistry.FavoriteName</c>. A checkbox rather than a typed tag
    /// so the one name with a fixed meaning cannot be misspelled into an ordinary tag.</summary>
    [ObservableProperty] private bool _isFavorite;

    [ObservableProperty] private string _note = "";
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

    [ObservableProperty] private string _mvsmfUrl = "";
    [ObservableProperty] private string _mvsmfUserid = "";

    /// <summary>The REST pin, which belongs to the REST URL as the 3270 pin belongs to host and port.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMvsmfPin), nameof(MvsmfPinText))]
    private CertificatePin? _mvsmfPinnedCertificate;
    private CertificatePin? _mvsmfPinnedFor;
    private readonly string? _mvsmfPinnedUrl;

    public bool HasMvsmfPin => MvsmfPinnedCertificate is not null;
    public string? MvsmfPinText =>
        MvsmfPinnedCertificate is { } pin ? $"Pinned certificate: SHA-256 {pin.Sha256} ({pin.Subject})" : null;

    /// <summary>The Test button's own line, kept apart from <see cref="ValidationMessage"/>, which only Save writes.</summary>
    [ObservableProperty] private string? _mvsmfTestResult;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestMvsmfCommand))]
    private bool _isTestingMvsmf;

    private readonly HostFileTester? _tester;

    public bool MvsmfPinCleared { get; private set; }

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

    public ProfileEditorViewModel(SessionProfile? existing, HostFileTester? tester = null)
    {
        _tester = tester;
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
        _isFavorite = existing.Tags.Contains(TagRegistry.FavoriteName);
        _tagsText = string.Join(", ", existing.Tags.Names.Where(name => !TagRegistry.IsReserved(name)));
        _note = existing.Note ?? "";
        _pinnedCertificate = existing.PinnedCertificate;
        _pinnedFor = existing.PinnedCertificate;
        _pinnedHost = existing.Host;
        _pinnedPort = existing.Port;
        _mvsmfUrl = existing.HostFilesUrl ?? "";
        _mvsmfUserid = existing.HostFilesUserid ?? "";
        _mvsmfPinnedCertificate = existing.HostFilesPinnedCertificate;
        _mvsmfPinnedFor = existing.HostFilesPinnedCertificate;
        _mvsmfPinnedUrl = existing.HostFilesUrl;
    }

    partial void OnUseTlsChanged(bool value)
    {
        if (value && PortText == "23") PortText = "992";
        else if (!value && PortText == "992") PortText = "23";
    }

    /// <summary>The oversize verdict currently in the validation box, or null when what is there came from
    /// another rule (or nothing is). <see cref="OnModelChanged"/> withdraws only its own message; see there.</summary>
    private string? _oversizeMessage;

    /// <summary>The one writer of <see cref="ValidationMessage"/>, so the box always knows whether what it holds
    /// is the oversize rule's verdict.</summary>
    private void SetValidation(string? message, bool fromOversize = false)
    {
        ValidationMessage = message;
        _oversizeMessage = fromOversize ? message : null;
    }

    /// <summary>An oversize legal under one model can be below another's floor — 100x30 clears model 2 and is
    /// short of model 5's floor — so a model change has to re-run the check rather than leave a stale verdict
    /// beside the box.</summary>
    partial void OnModelChanged(int value) => RevalidateOversize();

    /// <summary>And the box itself: a verdict the user has since typed their way out of is a red line under text
    /// that no longer says what it complains about — the same reason
    /// <see cref="ProfilePickerViewModel.OnQuickConnectTextChanged"/> clears its own message on the first
    /// keystroke. Withdrawing the message on a change the model did not cause is the other half of the rule
    /// <see cref="OnModelChanged"/> already applies.</summary>
    partial void OnOversizeChanged(string value) => RevalidateOversize();

    /// <summary>Re-runs the oversize rule and writes only its own verdict. The gate is the whole point: the box
    /// is this rule's to write only while it is empty or already holding what this rule last put there. A message
    /// another rule owns — "Give the profile a name.", which Save will still refuse on first — stays put whichever
    /// way the geometry now reads, because neither the model nor the geometry says anything about the name.
    /// A blank oversize needs no special case: <see cref="OversizeGeometry.TryParse"/> accepts it and yields no
    /// error, so emptying the box withdraws the message the same way correcting it does.</summary>
    private void RevalidateOversize()
    {
        if (ValidationMessage != _oversizeMessage) return;
        OversizeGeometry.TryParse(Oversize, SelectedModel, out _, out var error);
        SetValidation(error, fromOversize: true);
    }

    partial void OnHostChanged(string value) => RefreshPin();

    partial void OnPortTextChanged(string value) => RefreshPin();

    private void RefreshPin() =>
        PinnedCertificate = string.Equals(Host.Trim(), _pinnedHost, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(PortText.Trim(), out var port) && port == _pinnedPort
            ? _pinnedFor : null;

    /// <summary>Typing the reserved tag into the box turns the checkbox on and drops it from the text, rather
    /// than raising a validation message: the checkbox visibly moving explains what happened, and there is
    /// nothing for the user to go and fix. Re-entrant by construction — the assignment below re-enters this
    /// handler, whose Split then finds no reserved name and leaves the text alone.</summary>
    partial void OnTagsTextChanged(string value)
    {
        var names = TagSet.Split(value);
        if (!names.Any(TagRegistry.IsReserved)) return;
        IsFavorite = true;
        TagsText = string.Join(", ", names.Where(name => !TagRegistry.IsReserved(name)));
    }

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

    /// <summary>Bumped by every change that makes a Test result stale, so a test still running when the profile
    /// changes under it drops its result instead of reporting on values it never tried.</summary>
    private int _testGeneration;

    private void InvalidateTestResult()
    {
        _testGeneration++;
        MvsmfTestResult = null;
    }

    partial void OnMvsmfUrlChanged(string value)
    {
        MvsmfPinnedCertificate = SameRestUrl(value, _mvsmfPinnedUrl) ? _mvsmfPinnedFor : null;
        InvalidateTestResult();
    }

    partial void OnMvsmfUseridChanged(string value) => InvalidateTestResult();

    private static bool SameRestUrl(string? first, string? second) =>
        string.Equals(first?.Trim().TrimEnd('/'), second?.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void ForgetMvsmfPin()
    {
        _mvsmfPinnedFor = null;
        MvsmfPinnedCertificate = null;
        MvsmfPinCleared = true;
        InvalidateTestResult();
    }

    private bool CanTestMvsmf => _tester is not null && !IsTestingMvsmf;

    [RelayCommand(CanExecute = nameof(CanTestMvsmf))]
    private async Task TestMvsmfAsync()
    {
        if (!TryReadMvsmf(out var url, out var userid, out var problem))
        {
            MvsmfTestResult = "✗ " + problem;
            return;
        }
        if (url is null)
        {
            MvsmfTestResult = "✗ Enter the mvsMF URL first.";
            return;
        }
        IsTestingMvsmf = true;
        MvsmfTestResult = "⟳ Testing…";
        var generation = _testGeneration;
        string result;
        try
        {
            var name = string.IsNullOrWhiteSpace(Name) ? "This profile" : Name.Trim();
            var info = await _tester!(name, url, userid, MvsmfPinnedCertificate, CancellationToken.None);
            result = $"✓ Connected: {info.Product} {info.ProductVersion} on {info.SystemVersion}";
        }
        catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.CertificateRejected)
        {
            result = "✗ The host's certificate is not trusted. Open the mvsMF Browser from a session to review it.";
        }
        catch (Exception ex)
        {
            result = "✗ " + HostFileMessages.Describe(ex);
        }
        finally
        {
            IsTestingMvsmf = false;
        }
        if (generation == _testGeneration) MvsmfTestResult = result;
    }

    /// <summary>The REST URL (null when blank) and userid as Save and Test read them.</summary>
    private bool TryReadMvsmf(out Uri? url, out string? userid, out string? problem)
    {
        url = null;
        userid = null;
        problem = null;
        if (!string.IsNullOrWhiteSpace(MvsmfUrl) && !HostFileServiceFactory.TryNormalizeUrl(MvsmfUrl, out url, out var error))
        {
            problem = "mvsMF URL: " + error;
            return false;
        }
        var typed = MvsmfUserid.Trim().ToUpperInvariant();
        if (typed.Length > 0 && (typed.Length > 8 || !IsUseridStart(typed[0]) || typed.Any(c => !IsUseridStart(c) && !char.IsAsciiDigit(c))))
        {
            problem = "The mvsMF userid must be 1 to 8 letters, digits or # $ @, starting with a letter or # $ @.";
            return false;
        }
        userid = typed.Length > 0 ? typed : null;
        return true;
    }

    private static bool IsUseridStart(char c) => char.IsAsciiLetterUpper(c) || c is '#' or '$' or '@';

    public SessionProfile? TryBuild()
    {
        if (string.IsNullOrWhiteSpace(Name)) { SetValidation("Give the profile a name."); return null; }
        if (string.IsNullOrWhiteSpace(Host)) { SetValidation("Enter the host name or address."); return null; }
        if (!int.TryParse(PortText.Trim(), out var port) || port < 1 || port > 65535) { SetValidation("Port must be a number from 1 to 65535."); return null; }
        // The drop-down cannot produce this, but the seeding above can: a hand-edited file whose codePage is
        // null or blank is seeded into the list verbatim and selected, and CodePage.Trim() below would then
        // throw straight out of OnSaveClick, which has no catch. Blank is refused rather than passed through
        // for the same reason #45 replaced the text box: b3270 warns on stderr and starts on a fallback, so a
        // saved "" is a session that connects normally with quietly wrong characters.
        if (string.IsNullOrWhiteSpace(CodePage)) { SetValidation("Choose a code page."); return null; }
        // PlainNumber refuses a sign and any embedded space, so "-1", "+60" and "6 0" never reach the engine; the
        // Trim is this call site's own choice, the same forgiveness PortText above gets, so " 60" IS a valid 60.
        // The ceiling is a day: a larger one is a typo, not an intention.
        if (!PlainNumber.TryParse(KeepAliveText.Trim(), 0, 86400, out var keepAlive))
        {
            SetValidation("Keep-alive must be a whole number of seconds, 0 to 86400 (0 turns it off).");
            return null;
        }
        if (!OversizeGeometry.TryParse(Oversize, SelectedModel, out var oversize, out var oversizeError))
        {
            SetValidation(oversizeError, fromOversize: true);
            return null;
        }
        // Counted from what the user typed, BEFORE TagSet.From runs: From enforces the caps by discarding what
        // does not fit, so a check afterwards could never fire and a ninth tag would vanish in silence.
        var typed = TagSet.Split(TagsText);
        if (typed.FirstOrDefault(name => name.Length > TagSet.MaxNameLength) is not null)
        {
            SetValidation($"Tag names can be at most {TagSet.MaxNameLength} characters.");
            return null;
        }
        // Explicitly typed, not var: a collection expression in a conditional needs a target type.
        IReadOnlyList<string> wanted = IsFavorite ? [TagRegistry.FavoriteName, .. typed] : typed;
        if (wanted.Distinct(StringComparer.OrdinalIgnoreCase).Count() > TagSet.MaxTags)
        {
            SetValidation($"A profile can carry at most {TagSet.MaxTags} tags.");
            return null;
        }
        if (!TryReadMvsmf(out var restUrl, out var restUserid, out var restProblem))
        {
            SetValidation(restProblem);
            return null;
        }
        SetValidation(null);
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
            Tags = TagSet.From(wanted),
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
            HostFilesUrl = restUrl?.ToString(),
            HostFilesUserid = restUserid,
            HostFilesPinnedCertificate = restUrl is null ? null : MvsmfPinnedCertificate,
        };
    }
}
