// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using CommunityToolkit.Mvvm.Input;
using LizTerm.App.HostFiles;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.ViewModels;

/// <summary>New… and Delete… (USS spec §4.6): a directory is created under the current one from a name asked for
/// in the strip; a directory is deleted with everything in it after one question; files are deleted one by one
/// after one question naming up to five. The stamp memory follows every change.</summary>
public sealed partial class UssBrowserViewModel
{
    private const int NamesInQuestion = 5;

    private bool CanNewDirectory => !IsBusy && Current is not null;
    private bool CanDeleteDirectory => !IsBusy && SelectedDirectory is { Path: not null };
    private bool CanDeleteFiles => !IsBusy && _selectedFiles.Count > 0 && _selectedFiles.All(f => f.IsFile);

    /// <summary>Why <paramref name="name"/> cannot be a new directory under the current one, or null: one segment
    /// (no <c>/</c>, not <c>.</c> or <c>..</c>), not a name already listed, and a path the rules accept. Surrounding
    /// blanks are ignored, as the strip's box is typed into.</summary>
    internal string? NewNameProblem(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return "Enter a name.";
        if (trimmed.Contains('/')) return "A name cannot contain '/'.";
        if (trimmed is "." or "..") return "A name cannot be '.' or '..'.";
        if (Current is not { } current) return "Choose a directory first.";
        if (Directories.Any(d => d.Name == trimmed) || Files.Any(f => f.Name == trimmed)) return $"{trimmed} already exists in {current}.";
        return HostPath.UnixPathError(current.UnixPath == "/" ? "/" + trimmed : current.UnixPath + "/" + trimmed);
    }

    // ---- new directory ----

    [RelayCommand(CanExecute = nameof(CanNewDirectory))]
    private Task NewDirectoryAsync() => Current is { } current ? NewDirectoryAsync(current) : Task.CompletedTask;

    /// <summary>A retry before the host has created asks again; once it has, only the listing is retried.</summary>
    private Task NewDirectoryAsync(HostPath parent) =>
        _ops.RunThenListAsync((retryWith, token) => NewDirectoryCoreAsync(parent, retryWith, token), () => NewDirectoryAsync(parent));

    private async Task NewDirectoryCoreAsync(HostPath parent, Action<Func<Task>> retryWith, CancellationToken token)
    {
        var question = new ConfirmationRequest($"New directory in {parent}:", "Create", input: "", inputRule: NewNameProblem);
        var answer = await _ops.AskAsync(question);
        if (answer.Choice != ConfirmChoice.Primary)
        {
            _ops.StatusText = "– Create cancelled.";
            return;
        }
        // The rule passed, so Child cannot refuse.
        var path = parent.Child(question.Input.Trim());
        _ops.StatusText = $"⟳ Creating {path}…";
        try
        {
            await _connection.RunAsync(service => service.CreateDirectoryAsync(path, token));
        }
        catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.AlreadyExists)
        {
            // The host has it (another user, a job), so the listing on screen is stale: listed again, then the
            // reason, which the listing's own line must not hide.
            await ListCoreAsync(parent.UnixPath!, keepSelection: true, token);
            _ops.StatusText = "✗ " + HostFileMessages.Describe(ex);
            return;
        }
        var what = $"Created {path}";
        retryWith(() => ShowCreatedAsync(parent, path, what));
        await ShowCreatedAsync(parent, path, what, token);
    }

    private Task ShowCreatedAsync(HostPath parent, HostPath created, string what) =>
        _ops.RunExclusiveAsync(token => ShowCreatedAsync(parent, created, what, token), () => ShowCreatedAsync(parent, created, what));

    /// <summary>The second half of a create, and its own retry: the listing, with the new directory selected. A
    /// listing the host refuses leaves its reason after the fact of the create.</summary>
    private async Task ShowCreatedAsync(HostPath parent, HostPath created, string what, CancellationToken token)
    {
        if (!await ListCoreAsync(parent.UnixPath!, keepSelection: false, token))
        {
            _ops.StatusText = $"⚠ {what}. The list was not refreshed: {_ops.StatusText.TrimStart('✗').Trim()}";
            return;
        }
        SelectedDirectory = Directories.FirstOrDefault(d => d.Name == created.Name);
        _ops.StatusText = $"✓ {what}.";
    }

    // ---- delete a directory ----

    [RelayCommand(CanExecute = nameof(CanDeleteDirectory))]
    private Task DeleteDirectoryAsync() => SelectedDirectory is { Path: not null } row ? DeleteDirectoryAsync(row) : Task.CompletedTask;

    private Task DeleteDirectoryAsync(DirectoryRow row) =>
        _ops.RunExclusiveAsync(token => DeleteDirectoryCoreAsync(row, token), () => DeleteDirectoryAsync(row));

    private async Task DeleteDirectoryCoreAsync(DirectoryRow row, CancellationToken token)
    {
        var path = row.Path!;
        var answer = await _ops.AskAsync(new ConfirmationRequest(
            $"Delete directory {row.Name} and everything in it? This cannot be undone.", $"Delete {row.Name}"));
        if (answer.Choice != ConfirmChoice.Primary)
        {
            _ops.StatusText = "– Delete cancelled.";
            return;
        }
        _ops.StatusText = $"⟳ Deleting {path}…";
        try
        {
            await _connection.RunAsync(service => service.DeleteAsync(path, token));
        }
        catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.NotFound)
        {
            // Gone already (another user, a job): the row is stale either way.
            _access.Etags.ForgetUnder(path);
            DropDirectory(row);
            _ops.StatusText = $"✗ {path}: {HostFileMessages.Describe(ex)}";
            return;
        }
        _access.Etags.ForgetUnder(path);
        DropDirectory(row);
        _ops.StatusText = $"✓ Deleted {path}.";
    }

    /// <summary>Takes a row off the list without asking the host, so nothing on screen names what the host no
    /// longer has.</summary>
    private void DropDirectory(DirectoryRow row)
    {
        if (ReferenceEquals(SelectedDirectory, row)) SelectedDirectory = null;
        Directories.Remove(row);
        OnPropertyChanged(nameof(DirectoriesFooter));
    }

    // ---- delete files ----

    [RelayCommand(CanExecute = nameof(CanDeleteFiles))]
    private Task DeleteFilesAsync() => Current is { } current ? DeleteFilesAsync(current, [.. _selectedFiles]) : Task.CompletedTask;

    /// <summary>A retry deletes the files the connection failure left, not the selection: the failure clears it.
    /// Once every file has gone, only the listing is left to retry.</summary>
    private Task DeleteFilesAsync(HostPath directory, IReadOnlyList<FileRow> files)
    {
        var remaining = files;
        return _ops.RunExclusiveAsync(
            token => DeleteFilesCoreAsync(directory, files, left => remaining = left, token),
            () => DeleteFilesAsync(directory, remaining));
    }

    private async Task DeleteFilesCoreAsync(HostPath directory, IReadOnlyList<FileRow> files,
        Action<IReadOnlyList<FileRow>> stoppedAt, CancellationToken token)
    {
        if (files.Count == 0)
        {
            await ListCoreAsync(directory.UnixPath!, keepSelection: false, token);
            return;
        }
        var names = string.Join(", ", files.Take(NamesInQuestion).Select(file => file.Name));
        if (files.Count > NamesInQuestion) names += $" and {files.Count - NamesInQuestion} more";
        var label = $"Delete {Counted(files.Count, "file", "files")}";
        var answer = await _ops.AskAsync(new ConfirmationRequest($"Delete {names} from {directory}? This cannot be undone.", label));
        if (answer.Choice != ConfirmChoice.Primary)
        {
            _ops.StatusText = "– Delete cancelled.";
            return;
        }

        var deleted = 0;
        var failures = new List<string>();
        try
        {
            for (var i = 0; i < files.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var file = files[i];
                var path = file.Path!;
                try
                {
                    await _connection.RunAsync(service => service.DeleteAsync(path, token));
                    _access.Etags.Forget(path);
                    deleted++;
                    DropFile(file);
                }
                catch (HostFileException ex) when (BrowserOperations.IsConnectionFailure(ex))
                {
                    // The rest would fail the same way: let the banner offer Retry for this file and the ones after it.
                    stoppedAt([.. files.Skip(i)]);
                    throw;
                }
                catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.NotFound)
                {
                    // Gone already: the row is stale either way.
                    _access.Etags.Forget(path);
                    DropFile(file);
                    failures.Add($"{file.Name}: {HostFileMessages.Describe(ex)}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add($"{file.Name}: {HostFileMessages.Describe(ex)}");
                }
            }
            stoppedAt([]);
            await ListCoreAsync(directory.UnixPath!, keepSelection: false, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A delete cannot be undone, so the list and the line show what went before the cancel. The list is the
            // truth: a request the cancel interrupted may still have deleted its file on the host.
            await RefreshQuietlyAsync(directory);
            _ops.StatusText = $"– Delete cancelled: deleted {deleted} of {Counted(files.Count, "file", "files")}.";
            return;
        }
        _ops.StatusText = failures.Count == 0
            ? $"✓ Deleted {deleted} of {Counted(files.Count, "file", "files")}."
            : $"⚠ Deleted {deleted} of {Counted(files.Count, "file", "files")}. ✗ {string.Join("; ", failures)}";
    }

    private void DropFile(FileRow row)
    {
        Files.Remove(row);
        SetSelectedFiles(_selectedFiles.Where(file => !ReferenceEquals(file, row)));
    }

    /// <summary>A refresh on the way out of a stopped delete or upload: its own failure would only hide the reason
    /// for the stop, so it is ignored.</summary>
    private async Task RefreshQuietlyAsync(HostPath directory)
    {
        try
        {
            await ListCoreAsync(directory.UnixPath!, keepSelection: false, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }
    }
}
