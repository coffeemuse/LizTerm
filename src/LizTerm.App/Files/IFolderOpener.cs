namespace LizTerm.App.Files;

/// <summary>Opens a directory in the OS file manager. Injected like <see cref="IFilePicker"/>.</summary>
public interface IFolderOpener
{
    /// <returns>False when the platform could not open it; the caller then shows the path instead.</returns>
    Task<bool> OpenAsync(string directory);
}
