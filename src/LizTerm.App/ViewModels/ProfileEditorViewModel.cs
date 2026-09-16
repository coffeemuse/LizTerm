// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LizTerm.Core;
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

    /// <summary>Whether the drop-down is on Other. The boxes below are the custom size only while this is on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedModelChoice), nameof(ColumnsDisplay), nameof(RowsDisplay))]
    private bool _isCustomSize;

    /// <summary>What the user typed for the custom size. Kept while the drop-down is on a model, so returning to
    /// Other before closing the editor brings the numbers back; TryBuild ignores them outside Other.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ColumnsDisplay))] private string _columnsText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RowsDisplay))] private string _rowsText = "";

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

    /// <summary>The catalogue, seeded with this profile's own value when it falls outside it — a hand-edited
    /// file, or a model a newer engine adds. A ComboBox bound SelectedItem has nothing to select otherwise, and
    /// merely opening the editor would drop a working profile's setting. Model gets no validation in TryBuild,
    /// so nothing else would catch it either.</summary>
    private readonly IReadOnlyList<TerminalModel> _terminalModels;

    /// <summary>The drop-down: the catalogue (seeded as above), then Other.</summary>
    public IReadOnlyList<ModelChoice> ModelChoices { get; }

    /// <summary>Same seeding rule as the models.</summary>
    public IReadOnlyList<CodePage> CodePages { get; }

    /// <summary>The model Other saves. With an oversize, b3270 sends IBM-DYNAMIC and starts on 24x80 whatever the
    /// model is, so the model's only remaining effect is the floor the oversize must clear; model 2's is the
    /// lowest, so it refuses nothing a custom size could legally be.</summary>
    public static TerminalModel CustomSizeModel { get; } = TerminalModel.Find(2)!;

    /// <summary>A view over <see cref="Model"/> and <see cref="IsCustomSize"/>, which stay the properties TryBuild
    /// reads. Choosing Other sets the model to <see cref="CustomSizeModel"/>, so Model is always what Save writes.</summary>
    public ModelChoice SelectedModelChoice
    {
        get => IsCustomSize
            ? ModelChoice.Other
            : ModelChoices.FirstOrDefault(c => c.Model?.Number == Model) ?? ModelChoices[0];
        set
        {
            if (value is null) return;
            Model = value.Model?.Number ?? CustomSizeModel.Number;
            IsCustomSize = value.Model is null;
            OnPropertyChanged();
        }
    }

    /// <summary>What the Columns box shows: the typed number under Other, else the chosen model's own width (blank
    /// for a model outside the catalogue, whose geometry is unknown). Writes land only under Other; the box is
    /// disabled otherwise, so a write then can only be the binding echoing the model's size back.</summary>
    public string ColumnsDisplay
    {
        get => IsCustomSize ? ColumnsText : ModelDimension(m => m.Columns);
        set { if (IsCustomSize) ColumnsText = value ?? ""; }
    }

    /// <summary>Same rule as <see cref="ColumnsDisplay"/>, for the Rows box.</summary>
    public string RowsDisplay
    {
        get => IsCustomSize ? RowsText : ModelDimension(m => m.Rows);
        set { if (IsCustomSize) RowsText = value ?? ""; }
    }

    private string ModelDimension(Func<TerminalModel, int> dimension) =>
        _terminalModels.FirstOrDefault(m => m.Number == Model) is { Rows: > 0, Columns: > 0 } model
            ? dimension(model).ToString(CultureInfo.InvariantCulture)
            : "";

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

        var custom = SplitOversize(existing?.Oversize);
        var models = TerminalModel.All.ToList();
        if (existing is not null && custom is null && TerminalModel.Find(existing.Model) is null)
            models.Add(new TerminalModel(existing.Model, 0, 0));
        _terminalModels = models;
        ModelChoices = [.. models.Select(m => new ModelChoice(m)), ModelChoice.Other];

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
        _model = custom is null ? existing.Model : CustomSizeModel.Number;
        _extended = existing.Extended;
        _codePage = existing.CodePage;
        _luName = existing.LuName ?? "";
        _destructiveBackspace = existing.DestructiveBackspace;
        _keepAliveText = existing.KeepAliveSeconds.ToString(CultureInfo.InvariantCulture);
        _autoReconnect = existing.AutoReconnect;
        if (custom is { } size)
        {
            _isCustomSize = true;
            (_columnsText, _rowsText) = size;
        }
        _isFavorite = existing.Tags.Contains(TagRegistry.FavoriteName);
        _tagsText = string.Join(", ", existing.Tags.Names.Where(name => !TagRegistry.IsReserved(name)));
        _note = existing.Note ?? "";
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

    /// <summary>Splits a saved oversize into the two boxes' text, or null when the profile has none. b3270's own
    /// "0x0" means none too. Text that is not two parts still opens on Other, with both boxes empty, so a
    /// hand-edited value is not silently turned into "no oversize"; Save then refuses the empty boxes.</summary>
    private static (string Columns, string Rows)? SplitOversize(string? oversize)
    {
        if (string.IsNullOrWhiteSpace(oversize)) return null;
        var parts = oversize.Trim().Split('x', 'X');
        if (parts.Length != 2) return ("", "");
        if (PlainNumber.TryParse(parts[0], 0, 0, out _) && PlainNumber.TryParse(parts[1], 0, 0, out _)) return null;
        return (parts[0], parts[1]);
    }

    /// <summary>The size verdict currently in the validation box, or null when what is there came from another
    /// rule (or nothing is). <see cref="RevalidateCustomSize"/> withdraws only its own message; see there.</summary>
    private string? _sizeMessage;

    /// <summary>The one writer of <see cref="ValidationMessage"/>, so the box always knows whether what it holds
    /// is the size rule's verdict.</summary>
    private void SetValidation(string? message, bool fromSize = false)
    {
        ValidationMessage = message;
        _sizeMessage = fromSize ? message : null;
    }

    /// <summary>Leaving Other makes any size verdict moot, and typing in a box can make it stale: a red line under
    /// numbers the user has since corrected — the same reason
    /// <see cref="ProfilePickerViewModel.OnQuickConnectTextChanged"/> clears its own message on the first
    /// keystroke.</summary>
    partial void OnIsCustomSizeChanged(bool value) => RevalidateCustomSize();

    partial void OnColumnsTextChanged(string value) => RevalidateCustomSize();

    partial void OnRowsTextChanged(string value) => RevalidateCustomSize();

    partial void OnModelChanged(int value)
    {
        OnPropertyChanged(nameof(ColumnsDisplay));
        OnPropertyChanged(nameof(RowsDisplay));
    }

    /// <summary>Re-runs the size rule and writes only its own verdict. The gate is the whole point: the box is
    /// this rule's to write only while it is empty or already holding what this rule last put there. A message
    /// another rule owns — "Give the profile a name.", which Save will still refuse on first — stays put. An empty
    /// box mid-edit is not yet a mistake, so it withdraws the verdict; Save still refuses it.</summary>
    private void RevalidateCustomSize()
    {
        if (ValidationMessage != _sizeMessage) return;
        var error = IsCustomSize && !string.IsNullOrWhiteSpace(ColumnsText) && !string.IsNullOrWhiteSpace(RowsText)
            ? CheckCustomSize(out _)
            : null;
        SetValidation(error, fromSize: true);
    }

    /// <summary>The custom size as the boxes hold it, or the reason it cannot be saved. The floor is checked here,
    /// in the editor's own words, because OversizeGeometry's names model 2, which Other does not show; what is
    /// left for OversizeGeometry is the engine's area limit.</summary>
    private string? CheckCustomSize(out OversizeGeometry? geometry)
    {
        geometry = null;
        var columnsText = ColumnsText.Trim();
        var rowsText = RowsText.Trim();
        if (columnsText.Length == 0 || rowsText.Length == 0)
            return "Enter both a column count and a row count for the custom size.";
        if (!PlainNumber.TryParse(columnsText, 0, int.MaxValue, out var columns)) return "Columns must be a whole number.";
        if (!PlainNumber.TryParse(rowsText, 0, int.MaxValue, out var rows)) return "Rows must be a whole number.";
        if (columns < CustomSizeModel.Columns || rows < CustomSizeModel.Rows)
            return $"A custom size must be at least {CustomSizeModel.Columns} columns and {CustomSizeModel.Rows} rows.";
        OversizeGeometry.TryParse($"{columns}x{rows}", CustomSizeModel, out geometry, out var error);
        return error;
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
        OversizeGeometry? oversize = null;
        if (IsCustomSize && CheckCustomSize(out oversize) is { } sizeError)
        {
            SetValidation(sizeError, fromSize: true);
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
        };
    }
}
