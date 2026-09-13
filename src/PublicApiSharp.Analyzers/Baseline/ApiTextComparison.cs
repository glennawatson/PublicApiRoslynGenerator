// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;

namespace PublicApiSharp.Analyzers;

/// <summary>Compares baseline lines without materializing the parser's normalized declarations.</summary>
internal static class ApiTextComparison
{
    /// <summary>Compares nonblank lines after trimming their leading and trailing whitespace.</summary>
    /// <param name="baseline">The baseline text, read without allocating its line table or a string.</param>
    /// <param name="rendered">The renderer's text.</param>
    /// <returns>Whether the texts match under the declaration parser's line normalization.</returns>
    /// <remarks>
    /// Like ApiTextParser.NormalizeText and RenderedApiSurface.Normalize, only a line feed splits
    /// lines. Trimming also removes a CRLF's carriage return. Interior whitespace and line order
    /// remain significant here; the declaration comparison handles any other equivalences.
    /// </remarks>
    internal static bool Matches(SourceText baseline, string rendered)
    {
        var baselinePosition = 0;
        var renderedPosition = 0;
        while (true)
        {
            var baselineLine = ReadLine(baseline, ref baselinePosition);
            var renderedLine = ReadLine(rendered.AsSpan(), ref renderedPosition);
            if (baselineLine.Length != renderedLine.Length)
            {
                return false;
            }

            if (baselineLine.IsEmpty)
            {
                return true;
            }

            for (var index = 0; index < baselineLine.Length; index++)
            {
                if (baseline[baselineLine.Start + index] != renderedLine[index])
                {
                    return false;
                }
            }
        }
    }

    /// <summary>Reads the next nonblank baseline line without constructing a substring.</summary>
    /// <param name="text">The baseline.</param>
    /// <param name="position">The cursor, advanced past each consumed line.</param>
    /// <returns>The trimmed line's span, or an empty span at the end.</returns>
    private static TextSpan ReadLine(SourceText text, ref int position)
    {
        while (position < text.Length)
        {
            var start = position;
            while (position < text.Length && text[position] != '\n')
            {
                position++;
            }

            var end = position;
            position++;
            while (start < end && char.IsWhiteSpace(text[start]))
            {
                start++;
            }

            while (end > start && char.IsWhiteSpace(text[end - 1]))
            {
                end--;
            }

            if (start < end)
            {
                return TextSpan.FromBounds(start, end);
            }
        }

        return default;
    }

    /// <summary>Reads the next nonblank rendered line as a view of the existing string.</summary>
    /// <param name="text">The rendered text.</param>
    /// <param name="position">The cursor, advanced past each consumed line.</param>
    /// <returns>The trimmed line, or an empty span at the end.</returns>
    private static ReadOnlySpan<char> ReadLine(ReadOnlySpan<char> text, ref int position)
    {
        while (position < text.Length)
        {
            var remaining = text.Slice(position);
            var end = remaining.IndexOf('\n');
            if (end < 0)
            {
                end = remaining.Length;
            }

            position += end + 1;
            var line = remaining.Slice(0, end).Trim();
            if (!line.IsEmpty)
            {
                return line;
            }
        }

        return default;
    }
}
