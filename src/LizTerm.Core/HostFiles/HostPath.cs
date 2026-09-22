// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text;
using System.Text.RegularExpressions;

namespace LizTerm.Core.HostFiles;

public enum HostPathKind { Dataset, Member, Unix }

/// <summary>Where a file lives on the host: a dataset, a member of a partitioned one, or a UNIX path. Dataset and
/// member names are trimmed, folded to upper case and checked against the MVS naming rules when the path is made; a
/// UNIX path is kept exactly as given, case and blanks alike, since a blank is a name character on a UNIX file
/// system, and checked against <see cref="UnixPathError"/>. A path that exists is always one the host could accept.
/// Only <see cref="TryParse"/>, which reads typed text, trims.</summary>
public sealed record HostPath
{
    public const int MaxDatasetLength = 44;
    public const int MaxMemberLength = 8;
    /// <summary>One under the length at which the host's path buffer overflows. The host counts bytes, and a UNIX
    /// path holds only characters up to U+00FF, each one byte on the wire, so characters and bytes agree.</summary>
    public const int MaxUnixPathLength = 251;
    private const int MaxQualifierLength = 8;

    private HostPath(HostPathKind kind, string? dataset, string? member, string? unixPath)
    {
        Kind = kind;
        Dataset = dataset;
        Member = member;
        UnixPath = unixPath;
    }

    public HostPathKind Kind { get; }
    /// <summary>The dataset name; null for a UNIX path.</summary>
    public string? Dataset { get; }
    /// <summary>The member name; null for a dataset or a UNIX path.</summary>
    public string? Member { get; }
    /// <summary>The absolute, <c>/</c>-separated path; null for a dataset or member.</summary>
    public string? UnixPath { get; }

    /// <exception cref="ArgumentException">The name breaks a naming rule; the message says which.</exception>
    public static HostPath ForDataset(string dataset) => new(HostPathKind.Dataset, Checked(dataset, DatasetNameError), null, null);

    /// <exception cref="ArgumentException">Either name breaks a naming rule; the message says which.</exception>
    public static HostPath ForMember(string dataset, string member) =>
        new(HostPathKind.Member, Checked(dataset, DatasetNameError), Checked(member, MemberNameError), null);

    /// <exception cref="ArgumentException">The path breaks a rule (<see cref="UnixPathError"/>); the message says which.</exception>
    public static HostPath ForUnix(string path) =>
        UnixPathError(path) is { } error ? throw new ArgumentException(error) : new HostPath(HostPathKind.Unix, null, null, path);

    /// <exception cref="InvalidOperationException">This is a UNIX path.</exception>
    public HostPath WithMember(string member) => Kind == HostPathKind.Unix
        ? throw new InvalidOperationException("A UNIX path has no members.")
        : ForMember(Dataset!, member);

    /// <summary>The directory above a UNIX path; null at the root, and for a dataset or member.</summary>
    public HostPath? Parent
    {
        get
        {
            if (UnixPath is null || UnixPath == "/") return null;
            var cut = UnixPath.LastIndexOf('/');
            return ForUnix(cut == 0 ? "/" : UnixPath[..cut]);
        }
    }

    /// <summary>The last segment of a UNIX path (<c>/</c> at the root), the member name, or the dataset name.</summary>
    public string Name => Kind switch
    {
        HostPathKind.Unix => UnixPath == "/" ? "/" : UnixPath![(UnixPath!.LastIndexOf('/') + 1)..],
        HostPathKind.Member => Member!,
        _ => Dataset!,
    };

    /// <summary>The entry <paramref name="name"/> under this UNIX directory.</summary>
    /// <exception cref="InvalidOperationException">This is a dataset or member.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not one acceptable segment; the message says why.</exception>
    public HostPath Child(string name)
    {
        if (Kind != HostPathKind.Unix) throw new InvalidOperationException("Only a UNIX path has children.");
        if (name.Length == 0) throw new ArgumentException("Enter a name.");
        if (name.Contains('/')) throw new ArgumentException("A name cannot contain '/'.");
        return ForUnix(UnixPath == "/" ? "/" + name : UnixPath + "/" + name);
    }

    /// <summary>Reads <c>DSN</c>, <c>DSN(MEMBER)</c>, or a path starting with <c>/</c>, as typed: surrounding
    /// blanks are dropped, so a UNIX name that begins or ends with one is reached through a listing, not typed.</summary>
    public static bool TryParse(string? text, out HostPath? path, out string? error)
    {
        path = null;
        var trimmed = (text ?? "").Trim();
        try
        {
            var open = trimmed.IndexOf('(');
            if (trimmed.StartsWith('/'))
            {
                path = ForUnix(trimmed);
            }
            else if (open < 0)
            {
                path = ForDataset(trimmed);
            }
            else
            {
                if (!trimmed.EndsWith(')')) throw new ArgumentException("A member name must end with ')'.");
                path = ForMember(trimmed[..open], trimmed[(open + 1)..^1]);
            }
            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Why <paramref name="path"/> is not a UNIX path the host could take, or null when it is one: it must be
    /// absolute, hold no empty, <c>.</c> or <c>..</c> segment, no control character and no character above U+00FF
    /// (the host keeps one byte per character), not end in <c>/</c> (except the root itself), and be at most
    /// <see cref="MaxUnixPathLength"/> characters. It is judged exactly as given: blanks are name characters.</summary>
    public static string? UnixPathError(string path)
    {
        path ??= "";
        if (path.Length == 0) return "Enter a path.";
        if (path[0] != '/') return "A path must start with '/'.";
        if (path.Length > MaxUnixPathLength) return $"A path is at most {MaxUnixPathLength} characters.";
        foreach (var rune in path.EnumerateRunes())
        {
            if (Rune.IsControl(rune)) return "A path cannot contain control characters.";
            if (rune.Value > 0xFF) return $"A path cannot contain “{rune}” (U+{rune.Value:X4}), which the host can't store.";
        }
        if (path == "/") return null;
        if (path.EndsWith('/')) return "A path cannot end with '/'.";
        foreach (var segment in path[1..].Split('/'))
        {
            if (segment.Length == 0) return "A path cannot have an empty segment.";
            if (segment is "." or "..") return "A path cannot contain a '.' or '..' segment.";
        }
        return null;
    }

    /// <summary>Why <paramref name="name"/> is not a dataset name, or null when it is one. Case and surrounding blanks
    /// are ignored.</summary>
    public static string? DatasetNameError(string name)
    {
        var folded = Fold(name);
        if (folded.Length == 0) return "Enter a dataset name.";
        if (folded.Length > MaxDatasetLength) return $"A dataset name is at most {MaxDatasetLength} characters.";
        foreach (var qualifier in folded.Split('.'))
        {
            if (qualifier.Length == 0) return "A dataset name cannot have an empty qualifier.";
            if (qualifier.Length > MaxQualifierLength) return $"Qualifier '{qualifier}' is longer than {MaxQualifierLength} characters.";
            if (!IsNameStart(qualifier[0])) return $"Qualifier '{qualifier}' must start with a letter or # $ @.";
            foreach (var c in qualifier)
            {
                if (!IsNameStart(c) && !char.IsAsciiDigit(c) && c != '-')
                    return $"Qualifier '{qualifier}' contains '{c}', which a dataset name cannot.";
            }
        }
        return null;
    }

    /// <summary>Why <paramref name="name"/> is not a member name, or null when it is one.</summary>
    public static string? MemberNameError(string name)
    {
        var folded = Fold(name);
        if (folded.Length == 0) return "Enter a member name.";
        if (folded.Length > MaxMemberLength) return $"A member name is at most {MaxMemberLength} characters.";
        if (!IsNameStart(folded[0])) return "A member name must start with a letter or # $ @.";
        foreach (var c in folded)
        {
            if (!IsNameStart(c) && !char.IsAsciiDigit(c)) return $"A member name cannot contain '{c}'.";
        }
        return null;
    }

    /// <summary>Why <paramref name="pattern"/> cannot filter a dataset list, or null when it can. <c>*</c>, <c>**</c>
    /// and <c>%</c> are wildcards.</summary>
    public static string? DatasetPatternError(string pattern)
    {
        var folded = Fold(pattern);
        if (folded.Length == 0) return "Enter a dataset filter.";
        if (folded.Length > MaxDatasetLength) return $"A filter is at most {MaxDatasetLength} characters.";
        foreach (var c in folded)
        {
            if (!IsNameStart(c) && !char.IsAsciiDigit(c) && c is not ('.' or '-' or '*' or '%'))
                return $"A filter cannot contain '{c}'.";
        }
        return null;
    }

    /// <summary>Why <paramref name="pattern"/> cannot filter a member list, or null when it can. <c>*</c> matches any
    /// run of characters and <c>%</c> exactly one; a pattern without either names one member. The host refuses a
    /// pattern of 45 characters or more, so the limit is the dataset name's rather than the member name's.</summary>
    public static string? MemberPatternError(string pattern)
    {
        var folded = Fold(pattern);
        if (folded.Length == 0) return "Enter a member filter.";
        if (folded.Length > MaxDatasetLength) return $"A member filter is at most {MaxDatasetLength} characters.";
        foreach (var c in folded)
        {
            if (!IsNameStart(c) && !char.IsAsciiDigit(c) && c is not ('*' or '%'))
                return $"A member filter cannot contain '{c}'.";
        }
        return null;
    }

    /// <summary>Whether <paramref name="name"/> matches <paramref name="pattern"/> the way the host reads a member
    /// pattern (<c>*</c> any run of characters, <c>%</c> exactly one), case and surrounding blanks ignored, so a
    /// filter applied here means the same as one sent to the host.</summary>
    public static bool MemberPatternMatches(string pattern, string name)
    {
        var regex = "^" + Regex.Escape(Fold(pattern)).Replace("\\*", ".*").Replace("%", ".") + "$";
        return Regex.IsMatch(Fold(name), regex);
    }

    public override string ToString() => Kind == HostPathKind.Unix ? UnixPath! : Member is null ? Dataset! : $"{Dataset}({Member})";

    private static string Fold(string name) => (name ?? "").Trim().ToUpperInvariant();

    private static bool IsNameStart(char c) => char.IsAsciiLetterUpper(c) || c is '#' or '$' or '@';

    private static string Checked(string name, Func<string, string?> rule) =>
        rule(name) is { } error ? throw new ArgumentException(error) : Fold(name);
}
