// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers;

/// <summary>Matches a fully qualified name against a configured pattern containing <c>*</c> wildcards.</summary>
/// <remarks>
/// <para>
/// Listing attributes one by one does not scale — a project usually wants to drop a whole family
/// (<c>System.Diagnostics.CodeAnalysis.*</c>) or a naming convention (<c>*.InternalUseAttribute</c>).
/// A <c>*</c> stands for any run of characters, including none, and may appear anywhere.
/// </para>
/// <para>
/// Matching is done directly rather than through a regular expression: this runs per attribute per
/// symbol, and a regex would cost a compile and an allocation for what is a two-pointer scan.
/// </para>
/// </remarks>
internal static class NamePattern
{
    /// <summary>Determines whether a name matches a pattern.</summary>
    /// <param name="pattern">The pattern, which may contain <c>*</c> wildcards.</param>
    /// <param name="value">The fully qualified name to test.</param>
    /// <returns><see langword="true"/> when the pattern matches the whole name.</returns>
    internal static bool Matches(string pattern, string value)
    {
        var patternIndex = 0;
        var valueIndex = 0;
        var lastWildcard = -1;
        var resumeAt = 0;

        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                // Remember where to resume if the rest of the pattern fails to match from here.
                lastWildcard = patternIndex;
                patternIndex++;
                resumeAt = valueIndex;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == value[valueIndex])
            {
                patternIndex++;
                valueIndex++;
                continue;
            }

            if (lastWildcard < 0)
            {
                return false;
            }

            // Backtrack: let the last wildcard swallow one more character.
            resumeAt++;
            patternIndex = lastWildcard + 1;
            valueIndex = resumeAt;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    /// <summary>Determines whether any of the patterns matches a name.</summary>
    /// <param name="patterns">The patterns.</param>
    /// <param name="value">The fully qualified name to test.</param>
    /// <returns><see langword="true"/> when at least one pattern matches.</returns>
    internal static bool MatchesAny(string[] patterns, string value)
    {
        for (var i = 0; i < patterns.Length; i++)
        {
            if (Matches(patterns[i], value))
            {
                return true;
            }
        }

        return false;
    }
}
