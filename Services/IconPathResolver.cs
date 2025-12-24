using SL_Cleaning.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SL_Cleaning.Services;

/// <summary>
/// Generates candidate icon paths for a software entry using assorted metadata.
/// </summary>
public static class IconPathResolver
{
    /// <summary>
    /// Produces a prioritized list of icon file candidates for the supplied entry.
    /// </summary>
    public static IEnumerable<string> GetCandidates(SoftwareEntry entry)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in EnumerateCore(entry))
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var cleaned = CleanPath(value);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                continue;
            }

            if (seen.Add(cleaned))
            {
                yield return cleaned;
            }
        }
    }

    private static IEnumerable<string?> EnumerateCore(SoftwareEntry entry)
    {
        yield return entry.DisplayIcon;
        yield return ExtractPathFromCommand(entry.UninstallString);
        yield return ExtractPathFromCommand(entry.QuietUninstallString);
        yield return ExtractPathFromCommand(entry.ModifyPath);

        if (!string.IsNullOrWhiteSpace(entry.InstallLocation))
        {
            var expanded = Environment.ExpandEnvironmentVariables(entry.InstallLocation.Trim('"'));
            if (Directory.Exists(expanded))
            {
                foreach (var candidate in EnumerateFilesSafely(expanded))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesSafely(string directory)
    {
        static IEnumerable<string> Enumerate(string dir, string filter, int take)
        {
            try
            {
                return Directory.EnumerateFiles(dir, filter).Take(take);
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        foreach (var path in Enumerate(directory, "*.ico", 3))
        {
            yield return path;
        }

        foreach (var path in Enumerate(directory, "*.exe", 3))
        {
            yield return path;
        }

        foreach (var path in Enumerate(directory, "*.dll", 2))
        {
            yield return path;
        }
    }

    private static string? ExtractPathFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();
        if (trimmed.StartsWith("@"))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.StartsWith("\""))
        {
            int endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return trimmed.Substring(1, endQuote - 1);
            }
        }

        if (trimmed.StartsWith("'"))
        {
            int endQuote = trimmed.IndexOf('\'', 1);
            if (endQuote > 1)
            {
                return trimmed.Substring(1, endQuote - 1);
            }
        }

        int whitespaceIndex = trimmed.IndexOf(' ');
        if (whitespaceIndex > 0)
        {
            return trimmed[..whitespaceIndex];
        }

        return trimmed;
    }

    private static string? CleanPath(string path)
    {
        var working = path.Trim();

        if (working.StartsWith("@"))
        {
            working = working[1..];
        }

        working = working.Trim('"');
        working = Environment.ExpandEnvironmentVariables(working);

        return working;
    }
}
