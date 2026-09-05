using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace LizTerm.App.Files;

/// <summary>Wraps a top level's storage provider, resolved at each call so the dialog need not be open when the
/// picker is constructed.</summary>
public sealed class AvaloniaFilePicker(TopLevel topLevel) : IFilePicker
{
    public async Task<string?> PickFileToSendAsync()
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "File to send",
            AllowMultiple = false,
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickSaveLocationAsync(string suggestedFileName)
    {
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save received file as",
            SuggestedFileName = suggestedFileName,
        });
        return file?.TryGetLocalPath();
    }
}
