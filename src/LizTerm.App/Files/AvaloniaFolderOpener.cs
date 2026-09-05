using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace LizTerm.App.Files;

public sealed class AvaloniaFolderOpener(TopLevel topLevel) : IFolderOpener
{
    public Task<bool> OpenAsync(string directory) =>
        topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(directory));
}
