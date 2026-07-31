// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace RoslynCommon.Analyzers;

/// <summary>Reads editorconfig settings, matching the CA-analyzer key convention.</summary>
internal static class AnalyzerOptionReader
{
    /// <summary>Reads a comma-separated list, trimming entries and dropping empty ones.</summary>
    /// <param name="options">The analyzer config options.</param>
    /// <param name="key">The option key.</param>
    /// <returns>The parsed values, or an empty array when the key is not set.</returns>
    internal static string[] ReadCommaSeparatedList(AnalyzerConfigOptions options, string key)
    {
        if (!options.TryGetValue(key, out var value))
        {
            return [];
        }

        var parts = value.Split(',');
        var parsed = new string[parts.Length];
        var count = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var trimmed = parts[i].Trim();
            if (trimmed.Length > 0)
            {
                parsed[count] = trimmed;
                count++;
            }
        }

        if (count == parts.Length)
        {
            return parsed;
        }

        var result = new string[count];
        System.Array.Copy(parsed, result, count);
        return result;
    }
}
