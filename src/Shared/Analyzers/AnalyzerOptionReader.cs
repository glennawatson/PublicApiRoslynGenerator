// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;

namespace RoslynCommon.Analyzers;

/// <summary>Reads editorconfig settings, matching the CA-analyzer key convention.</summary>
internal static class AnalyzerOptionReader
{
    /// <summary>Reads a setting from the first source that has it.</summary>
    /// <param name="primary">The options consulted first.</param>
    /// <param name="fallback">The options consulted when the primary does not have the key.</param>
    /// <param name="key">The option key.</param>
    /// <param name="value">The value found.</param>
    /// <returns><see langword="true"/> when either source had the key.</returns>
    internal static bool TryRead(
        AnalyzerConfigOptions primary,
        AnalyzerConfigOptions? fallback,
        string key,
        out string value)
    {
        if (primary.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }

        if (fallback is not null && fallback.TryGetValue(key, out found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>Reads a comma-separated list, trimming entries and dropping empty ones.</summary>
    /// <param name="primary">The options consulted first.</param>
    /// <param name="fallback">The options consulted when the primary does not have the key.</param>
    /// <param name="key">The option key.</param>
    /// <returns>The parsed values, or an empty array when the key is not set.</returns>
    internal static string[] ReadCommaSeparatedList(
        AnalyzerConfigOptions primary,
        AnalyzerConfigOptions? fallback,
        string key)
    {
        if (!TryRead(primary, fallback, key, out var value))
        {
            return [];
        }

        var remaining = value.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            if (!ReadNext(ref remaining).IsEmpty)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return [];
        }

        var result = new string[count];
        remaining = value.AsSpan();
        var index = 0;
        while (!remaining.IsEmpty)
        {
            var entry = ReadNext(ref remaining);
            if (!entry.IsEmpty)
            {
                result[index] = entry.ToString();
                index++;
            }
        }

        return result;
    }

    /// <summary>Consumes one comma-delimited entry and trims its surrounding whitespace.</summary>
    /// <param name="remaining">The unconsumed list text, advanced past this entry.</param>
    /// <returns>The trimmed entry without allocating a string.</returns>
    private static ReadOnlySpan<char> ReadNext(ref ReadOnlySpan<char> remaining)
    {
        var separator = remaining.IndexOf(',');
        var entry = separator < 0 ? remaining : remaining.Slice(0, separator);
        remaining = separator < 0 ? default : remaining.Slice(separator + 1);
        return entry.Trim();
    }
}
