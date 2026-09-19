// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

public enum HostPathKind { Dataset, Member }

/// <summary>Where a file lives on the host: a dataset, or a member of a partitioned one. Names are trimmed, folded to
/// upper case and checked against the MVS naming rules when the path is made, so a path that exists is always one
/// the host could accept. A UNIX file system path will arrive as a further kind and factory on this type.</summary>
public sealed record HostPath
{
    public const int MaxDatasetLength = 44;
    public const int MaxMemberLength = 8;
    private const int MaxQualifierLength = 8;

    private HostPath(HostPathKind kind, string dataset, string? member)
    {
        Kind = kind;
        Dataset = dataset;
        Member = member;
    }

    public HostPathKind Kind { get; }
    public string Dataset { get; }
    public string? Member { get; }

    /// <exception cref="ArgumentException">The name breaks a naming rule; the message says which.</exception>
    public static HostPath ForDataset(string dataset) => new(HostPathKind.Dataset, Checked(dataset, DatasetNameError), null);

    /// <exception cref="ArgumentException">Either name breaks a naming rule; the message says which.</exception>
    public static HostPath ForMember(string dataset, string member) =>
        new(HostPathKind.Member, Checked(dataset, DatasetNameError), Checked(member, MemberNameError));

    public HostPath WithMember(string member) => ForMember(Dataset, member);

    /// <summary>Reads <c>DSN</c> or <c>DSN(MEMBER)</c>.</summary>
    public static bool TryParse(string? text, out HostPath? path, out string? error)
    {
        path = null;
        var trimmed = (text ?? "").Trim();
        try
        {
            var open = trimmed.IndexOf('(');
            if (open < 0)
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

    public override string ToString() => Member is null ? Dataset : $"{Dataset}({Member})";

    private static string Fold(string name) => (name ?? "").Trim().ToUpperInvariant();

    private static bool IsNameStart(char c) => char.IsAsciiLetterUpper(c) || c is '#' or '$' or '@';

    private static string Checked(string name, Func<string, string?> rule) =>
        rule(name) is { } error ? throw new ArgumentException(error) : Fold(name);
}
