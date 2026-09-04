using CommunityToolkit.Mvvm.ComponentModel;
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
    [ObservableProperty] private bool _destructiveBackspace;
    [ObservableProperty] private string? _validationMessage;

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
    }

    partial void OnUseTlsChanged(bool value)
    {
        if (value && PortText == "23") PortText = "992";
        else if (!value && PortText == "992") PortText = "23";
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
            Model = Model,
            Extended = Extended,
            CodePage = CodePage.Trim(),
            LuName = string.IsNullOrWhiteSpace(LuName) ? null : LuName.Trim(),
            DestructiveBackspace = DestructiveBackspace,
        };
    }
}
