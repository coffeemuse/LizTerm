// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.ObjectModel;
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

    /// <summary>Whether the drop-down is on Other. The boxes below are the custom size only while this is on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedModelChoice), nameof(ColumnsDisplay), nameof(RowsDisplay))]
    private bool _isCustomSize;

    /// <summary>What the user typed for the custom size. Kept while the drop-down is on a model, so returning to
    /// Other before closing the editor brings the numbers back; TryBuild ignores them outside Other.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ColumnsDisplay))] private string _columnsText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RowsDisplay))] private string _rowsText = "";

    /// <summary>The colors in force when the editor opened. Chips are drawn against it, and so is the preview of
    /// the color a new tag will get.</summary>
    private readonly TagRegistry _registry;

    /// <summary>The profile's tag names, in order, as typed, WITHOUT the reserved tag — <see cref="IsFavorite"/>
    /// owns that one. <see cref="TagChips"/> draws them.</summary>
    private readonly List<string> _tagNames = [];

    public IReadOnlyList<string> TagNames => _tagNames;

    /// <summary>One chip per name in <see cref="TagNames"/>. A tag the registry does not know yet is drawn in the
    /// color <see cref="TagRegistry.Register"/> would give it, which is what the picker's reconciliation assigns
    /// once the profile is saved.</summary>
    public ObservableCollection<TagChip> TagChips { get; } = [];

    /// <summary>The known tags this profile does not carry yet, for the entry box's drop-down, in the registry's
    /// own (alphabetical) order. The box filters them by what is typed.</summary>
    public ObservableCollection<TagChip> TagSuggestions { get; } = [];

    /// <summary>What is typed in the tag box and not yet a chip. A comma, typed or pasted, turns everything before
    /// it into chips.</summary>
    [ObservableProperty] private string _tagEntry = "";

    /// <summary>Why the last tag could not be added, shown under the box; cleared by the next keystroke.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTagMessage))]
    private string? _tagMessage;

    public bool HasTagMessage => TagMessage is not null;

    /// <summary>Whether the profile carries <c>TagRegistry.FavoriteName</c>. A checkbox rather than a typed tag
    /// so the one name with a fixed meaning cannot be misspelled into an ordinary tag.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddTag), nameof(TagPlaceholder))]
    private bool _isFavorite;

    /// <summary>Whether another tag fits under <see cref="TagSet.MaxTags"/>, FAVORITE counted.</summary>
    public bool CanAddTag => _tagNames.Count + (IsFavorite ? 1 : 0) < TagSet.MaxTags;

    public string TagPlaceholder => CanAddTag ? "Add a tag" : $"{TagSet.MaxTags} tags at most";

    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string? _validationMessage;

    /// <summary>Which field <see cref="ValidationMessage"/> is about; null while there is no message.</summary>
    [ObservableProperty] private ProfileEditorField? _validationField;

    /// <summary>The pin the profile carries, shown read-only. Forget clears it and Save then writes the profile
    /// without it, which is the only way back from a pin to the engine's default trust (spec 5.5). A pin belongs to
    /// the host and port it was taken from: editing either drops it, and restoring them brings it back until Save.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPinnedCertificate), nameof(PinnedSubject), nameof(PinnedFingerprint))]
    private CertificatePin? _pinnedCertificate;
    private CertificatePin? _pinnedFor;
    private readonly string _pinnedHost = "";
    private readonly int _pinnedPort;

    public bool HasPinnedCertificate => PinnedCertificate is not null;
    public string? PinnedSubject => PinnedCertificate?.Subject;

    /// <summary>The SHA-256 on two even lines, broken between bytes: a colon-separated fingerprint is 95
    /// characters with no space in it, and left to wrap on its own it breaks mid-byte.</summary>
    public string? PinnedFingerprint => PinnedCertificate is { } pin ? TwoLines(pin.Sha256) : null;

    private static string TwoLines(string fingerprint)
    {
        var bytes = fingerprint.Split(':');
        if (bytes.Length <= 16) return fingerprint;
        var half = (bytes.Length + 1) / 2;
        return string.Join(':', bytes[..half]) + ":\n" + string.Join(':', bytes[half..]);
    }

    [ObservableProperty] private string _mvsmfUrl = "";
    [ObservableProperty] private string _mvsmfUserid = "";

    /// <summary>The REST pin, which belongs to the REST URL as the 3270 pin belongs to host and port.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMvsmfPin), nameof(MvsmfPinnedSubject), nameof(MvsmfPinnedFingerprint))]
    private CertificatePin? _mvsmfPinnedCertificate;
    private CertificatePin? _mvsmfPinnedFor;
    private readonly string? _mvsmfPinnedUrl;

    public bool HasMvsmfPin => MvsmfPinnedCertificate is not null;
    public string? MvsmfPinnedSubject => MvsmfPinnedCertificate?.Subject;
    public string? MvsmfPinnedFingerprint => MvsmfPinnedCertificate is { } pin ? TwoLines(pin.Sha256) : null;

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
            // Other starts from the size on screen rather than two empty boxes, so the spinners have something to
            // count from. Numbers already typed this session win: returning to Other brings them back.
            if (value.Model is null && !IsCustomSize && ColumnsText.Length == 0 && RowsText.Length == 0)
            {
                ColumnsText = ModelDimension(m => m.Columns);
                RowsText = ModelDimension(m => m.Rows);
            }
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

    /// <param name="tags">The tag colors to draw chips in; null draws every chip in the color a first sighting
    /// would get.</param>
    /// <param name="tester">What the mvsMF Test button runs; null leaves the button disabled.</param>
    public ProfileEditorViewModel(SessionProfile? existing, TagRegistry? tags = null, HostFileTester? tester = null)
    {
        _tester = tester;
        IsNew = existing is null;
        _registry = tags ?? TagRegistry.Empty;

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

        if (existing is null)
        {
            RefreshTags();
            return;
        }
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
        _tagNames.AddRange(existing.Tags.Names.Where(name => !TagRegistry.IsReserved(name)));
        RefreshTags();
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
    private void SetValidation(string? message, ProfileEditorField field, bool fromSize = false)
    {
        ValidationMessage = message;
        ValidationField = message is null ? null : field;
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
        SetValidation(error, ProfileEditorField.ScreenSize, fromSize: true);
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
        if (DimensionError(columnsText, "Columns", out var columns) is { } columnsError) return columnsError;
        if (DimensionError(rowsText, "Rows", out var rows) is { } rowsError) return rowsError;
        if (columns < CustomSizeModel.Columns || rows < CustomSizeModel.Rows)
            return $"A custom size must be at least {CustomSizeModel.Columns} columns and {CustomSizeModel.Rows} rows.";
        OversizeGeometry.TryParse($"{columns}x{rows}", CustomSizeModel, out geometry, out var error);
        return error;
    }

    /// <summary>One box's number, or the reason it is not one. The per-dimension ceiling is checked here too, so
    /// OversizeGeometry's "Oversize columns ..." wording never reaches a window with no Oversize field, and a run of
    /// digits too long for an int is called too large rather than not a whole number.</summary>
    private static string? DimensionError(string text, string name, out int value)
    {
        if (PlainNumber.TryParse(text, 0, OversizeGeometry.MaxCells, out value)) return null;
        return text.All(char.IsAsciiDigit)
            ? $"{name} must be at most {OversizeGeometry.MaxCells.ToString("N0", CultureInfo.InvariantCulture)}."
            : $"{name} must be a whole number.";
    }

    partial void OnHostChanged(string value) => RefreshPin();

    partial void OnPortTextChanged(string value) => RefreshPin();

    private void RefreshPin() =>
        PinnedCertificate = string.Equals(Host.Trim(), _pinnedHost, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(PortText.Trim(), out var port) && port == _pinnedPort
            ? _pinnedFor : null;

    /// <summary>A comma ends a tag, whether typed or pasted: everything before the last one becomes chips, and
    /// what follows stays in the box. A name that cannot be added stops there and stays in the box, with the
    /// reason under it. Any other edit clears that reason.</summary>
    partial void OnTagEntryChanged(string value)
    {
        if (!value.Contains(','))
        {
            TagMessage = null;
            return;
        }
        var parts = value.Split(',');
        string? error = null;
        var stopped = parts.Length - 1;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if ((error = TryAddTag(parts[i])) is null) continue;
            stopped = i;
            break;
        }
        var left = parts[stopped..].Select(part => part.Trim()).Where(part => part.Length > 0);
        TagEntry = error is null ? parts[^1].TrimStart() : string.Join(", ", left);
        TagMessage = error;
    }

    /// <summary>Turns what is in the box into a chip: Enter, and the box losing focus. False, with the reason in
    /// <see cref="TagMessage"/>, when it cannot be one.</summary>
    public bool CommitTagEntry()
    {
        var error = TryAddTag(TagEntry);
        if (error is null) TagEntry = "";
        TagMessage = error;
        return error is null;
    }

    /// <summary>Adds a tag chosen from the suggestions, leaving the box alone.</summary>
    public bool AddTag(string name)
    {
        var error = TryAddTag(name);
        TagMessage = error;
        return error is null;
    }

    /// <summary>Backspace in the empty box.</summary>
    public void RemoveLastTag()
    {
        if (_tagNames.Count == 0) return;
        _tagNames.RemoveAt(_tagNames.Count - 1);
        RefreshTags();
    }

    [RelayCommand]
    private void RemoveTag(TagChip? chip)
    {
        if (chip is null) return;
        if (_tagNames.RemoveAll(name => name.Equals(chip.Text, StringComparison.OrdinalIgnoreCase)) > 0) RefreshTags();
    }

    /// <summary>Adds one typed name, or says why it cannot be added. Blank and repeated names are no error:
    /// there is nothing to fix. The reserved name turns the checkbox on instead of becoming a chip, which the
    /// checkbox visibly moving explains.</summary>
    private string? TryAddTag(string raw)
    {
        var name = TagSet.Normalize(raw);
        if (name.Length == 0) return null;
        if (TagRegistry.IsReserved(name))
        {
            IsFavorite = true;
            return null;
        }
        if (name.Length > TagSet.MaxNameLength) return $"Tag names can be at most {TagSet.MaxNameLength} characters.";
        if (_tagNames.Any(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase))) return null;
        if (!CanAddTag) return $"A profile can carry at most {TagSet.MaxTags} tags.";
        _tagNames.Add(name);
        RefreshTags();
        return null;
    }

    private void RefreshTags()
    {
        // The preview registry, never saved from here: Register gives each unknown name the color the picker's
        // reconciliation will give it when it next reads this profile.
        var (preview, _) = _registry.Register(_tagNames);
        TagChips.Clear();
        foreach (var chip in TagChip.For(TagSet.From(_tagNames), preview)) TagChips.Add(chip);

        TagSuggestions.Clear();
        var carried = TagSet.From(_tagNames);
        foreach (var definition in _registry.Stored.Where(d => !carried.Contains(d.Name)))
            TagSuggestions.Add(TagChip.For(TagSet.From([definition.Name]), _registry)[0]);

        OnPropertyChanged(nameof(TagNames));
        OnPropertyChanged(nameof(CanAddTag));
        OnPropertyChanged(nameof(TagPlaceholder));
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

    /// <summary>Compares the URLs as the profile will store them, so "http://h:8080" and "http://h:8080/zosmf" are
    /// one URL; text that does not normalise is compared as typed. Core's rule does the comparing.</summary>
    private static bool SameRestUrl(string? first, string? second) =>
        PinMerge.SameUrl(NormalizedOrTyped(first), NormalizedOrTyped(second));

    private static string? NormalizedOrTyped(string? text) =>
        HostFileServiceFactory.TryNormalizeUrl(text, out var url, out _) && url is not null ? url.ToString() : text?.Trim();

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
        if (!TryReadMvsmf(out var url, out var userid, out var problem, out _))
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
            result = IsSupportedVersion(info.ProductVersion)
                ? $"✓ Connected: {info.Product} {info.ProductVersion} on {info.SystemVersion}"
                : $"✗ mvsMF {info.ProductVersion} is not supported; LizTerm needs mvsMF 1.1.0 or later.";
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

    /// <summary>Whether a reported version is at least 1.1.0 (spec §4.5). Host-neutral: only the major.minor head,
    /// before any "-dev" or similar suffix, is parsed; anything that does not parse is unsupported.</summary>
    private static bool IsSupportedVersion(string version)
    {
        var head = version.Split('-', 2)[0];
        var parts = head.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            return false;
        return major > 1 || (major == 1 && minor >= 1);
    }

    /// <summary>The REST URL (null when blank) and userid as Save and Test read them; on a refusal, the problem and
    /// the field it is about.</summary>
    private bool TryReadMvsmf(out Uri? url, out string? userid, out string? problem, out ProfileEditorField field)
    {
        url = null;
        userid = null;
        problem = null;
        field = ProfileEditorField.MvsmfUrl;
        if (!string.IsNullOrWhiteSpace(MvsmfUrl) && !HostFileServiceFactory.TryNormalizeUrl(MvsmfUrl, out url, out var error))
        {
            problem = "mvsMF URL: " + error;
            return false;
        }
        var typed = MvsmfUserid.Trim().ToUpperInvariant();
        if (typed.Length > 0 && (typed.Length > 8 || !IsUseridStart(typed[0]) || typed.Any(c => !IsUseridStart(c) && !char.IsAsciiDigit(c))))
        {
            field = ProfileEditorField.MvsmfUserid;
            problem = "The mvsMF userid must be 1 to 8 letters, digits or # $ @, starting with a letter or # $ @.";
            return false;
        }
        userid = typed.Length > 0 ? typed : null;
        return true;
    }

    private static bool IsUseridStart(char c) => char.IsAsciiLetterUpper(c) || c is '#' or '$' or '@';

    public SessionProfile? TryBuild()
    {
        if (string.IsNullOrWhiteSpace(Name)) { SetValidation("Give the profile a name.", ProfileEditorField.Name); return null; }
        if (string.IsNullOrWhiteSpace(Host)) { SetValidation("Enter the host name or address.", ProfileEditorField.Host); return null; }
        if (!int.TryParse(PortText.Trim(), out var port) || port < 1 || port > 65535) { SetValidation("Port must be a number from 1 to 65535.", ProfileEditorField.Port); return null; }
        // The drop-down cannot produce this, but the seeding above can: a hand-edited file whose codePage is
        // null or blank is seeded into the list verbatim and selected, and CodePage.Trim() below would then
        // throw straight out of OnSaveClick, which has no catch. Blank is refused rather than passed through
        // for the same reason #45 replaced the text box: b3270 warns on stderr and starts on a fallback, so a
        // saved "" is a session that connects normally with quietly wrong characters.
        if (string.IsNullOrWhiteSpace(CodePage)) { SetValidation("Choose a code page.", ProfileEditorField.CodePage); return null; }
        // PlainNumber refuses a sign and any embedded space, so "-1", "+60" and "6 0" never reach the engine; the
        // Trim is this call site's own choice, the same forgiveness PortText above gets, so " 60" IS a valid 60.
        // The ceiling is a day: a larger one is a typo, not an intention.
        if (!PlainNumber.TryParse(KeepAliveText.Trim(), 0, 86400, out var keepAlive))
        {
            SetValidation("Keep-alive must be a whole number of seconds, 0 to 86400 (0 turns it off).",
                ProfileEditorField.KeepAlive);
            return null;
        }
        OversizeGeometry? oversize = null;
        if (IsCustomSize && CheckCustomSize(out oversize) is { } sizeError)
        {
            SetValidation(sizeError, ProfileEditorField.ScreenSize, fromSize: true);
            return null;
        }
        // A name still in the box is one the user meant to add; saving without it would drop it in silence.
        if (TagEntry.Trim().Length > 0 && !CommitTagEntry())
        {
            SetValidation(TagMessage, ProfileEditorField.Tags);
            return null;
        }
        // Counted BEFORE TagSet.From runs: From enforces the caps by discarding what does not fit, so a check
        // afterwards could never fire and a ninth tag would vanish in silence. The chips alone cannot pass the cap,
        // but turning FAVORITE on beside eight of them can.
        // Explicitly typed, not var: a collection expression in a conditional needs a target type.
        IReadOnlyList<string> wanted = IsFavorite ? [TagRegistry.FavoriteName, .. _tagNames] : _tagNames;
        if (wanted.Count > TagSet.MaxTags)
        {
            SetValidation($"A profile can carry at most {TagSet.MaxTags} tags.", ProfileEditorField.Tags);
            return null;
        }
        if (!TryReadMvsmf(out var restUrl, out var restUserid, out var restProblem, out var restField))
        {
            SetValidation(restProblem, restField);
            return null;
        }
        SetValidation(null, ProfileEditorField.Name);
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
