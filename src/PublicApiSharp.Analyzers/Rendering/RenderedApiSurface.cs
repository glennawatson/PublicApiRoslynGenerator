// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace PublicApiSharp.Analyzers;

/// <summary>A rendered API surface: its text, plus the symbol each declaration came from.</summary>
/// <remarks>
/// The symbol map is keyed by line so that a declaration can be traced to the symbol that produced
/// it. That is what lets a diagnostic about a missing baseline entry be reported on the declaration
/// in the user's own source rather than against the API file.
/// </remarks>
internal sealed class RenderedApiSurface
{
    /// <summary>The two halves considered at each binary-search step.</summary>
    private const int SearchHalves = 2;

    /// <summary>Where each declaration sits in the text, and what it was written from.</summary>
    private readonly List<Written> _written;

    /// <summary>Initializes a new instance of the <see cref="RenderedApiSurface"/> class.</summary>
    /// <param name="text">The rendered surface text.</param>
    /// <param name="written">Where each declaration sits in the text.</param>
    /// <remarks>The surface owns the list; the writer that recorded it hands it over and stops using it.</remarks>
    internal RenderedApiSurface(string text, List<Written> written)
    {
        Text = text;
        _written = written;
    }

    /// <summary>Gets the rendered surface text.</summary>
    internal string Text { get; }

    /// <summary>Gets the declarations this surface states, in the order they were written.</summary>
    /// <remarks>
    /// <para>
    /// Collected while rendering rather than parsed back out of <see cref="Text"/>. Reading the text
    /// back cost more than producing it, and every identity here comes from the symbol that produced
    /// the declaration, which <c>SymbolIdentitiesMatchParsedIdentitiesAsync</c> holds to the same
    /// answer the parser gives for the baseline.
    /// </para>
    /// <para>
    /// Built on demand, because the renderer's other caller — the code fix, which regenerates the
    /// baseline file — wants the text and nothing else. Deriving an identity for every declaration it
    /// will not read would make accepting a change slower for no purpose.
    /// </para>
    /// </remarks>
    internal ImmutableArray<ApiDeclaration> Declarations
    {
        get
        {
            if (field.IsDefault)
            {
                field = Build();
            }

            return field;
        }
    }

    /// <summary>Gets the symbol at a declaration's first attribute or signature line, if any.</summary>
    /// <param name="line">The zero-based line number.</param>
    /// <returns>The symbol, or <see langword="null"/>.</returns>
    internal ISymbol? SymbolAtLine(int line)
    {
        var low = 0;
        var high = _written.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / SearchHalves);
            var written = _written[middle];
            if (line < written.Line)
            {
                high = middle - 1;
            }
            else if (line > written.SignatureLine)
            {
                low = middle + 1;
            }
            else
            {
                return line == written.Line || line == written.SignatureLine ? written.Symbol : null;
            }
        }

        return null;
    }

    /// <summary>Trims each line of a span the way the parser normalizes a declaration.</summary>
    /// <param name="text">The whole document.</param>
    /// <param name="start">Where the declaration starts.</param>
    /// <param name="end">Where it ends.</param>
    /// <returns>The declaration's lines, trimmed, joined by a line feed.</returns>
    private static string Normalize(string text, int start, int end)
    {
        var remaining = text.AsSpan(start, end - start).Trim();
        if (remaining.IndexOf('\n') < 0)
        {
            return remaining.ToString();
        }

        var builder = new PooledStringBuilder(remaining.Length);

        while (!remaining.IsEmpty)
        {
            var lineEnd = remaining.IndexOf('\n');
            if (lineEnd < 0)
            {
                lineEnd = remaining.Length;
            }

            var line = remaining.Slice(0, lineEnd).Trim();
            if (!line.IsEmpty)
            {
                if (builder.Length > 0)
                {
                    _ = builder.Append('\n');
                }

                foreach (var character in line)
                {
                    _ = builder.Append(character);
                }
            }

            remaining = remaining.Slice(Math.Min(lineEnd + 1, remaining.Length));
        }

        return builder.ToString();
    }

    /// <summary>Turns what was written into declarations, identities and all.</summary>
    /// <returns>The declarations.</returns>
    private ImmutableArray<ApiDeclaration> Build()
    {
        var builder = ImmutableArray.CreateBuilder<ApiDeclaration>(_written.Count);

        for (var index = 0; index < _written.Count; index++)
        {
            var written = _written[index];
            var identity = written.Symbol is { } symbol
                ? ApiIdentity.Of(symbol)
                : ApiIdentity.OfAssemblyAttribute(written.AssemblyAttribute!);

            builder.Add(new(
                identity,
                Normalize(Text, written.Start, written.End),
                written.Line,
                new TextSpan(written.Start, written.End - written.Start),
                written.Symbol is INamedTypeSymbol type && RoslynFeatures.IsExtensionContainer(type)));
        }

        return builder.MoveToImmutable();
    }

    /// <summary>One declaration as it was written: where it sits, and what produced it.</summary>
    /// <param name="Symbol">The symbol it came from, or <see langword="null"/> for an assembly attribute.</param>
    /// <param name="AssemblyAttribute">The rendered assembly attribute, when there is no symbol.</param>
    /// <param name="Start">Where the declaration starts in the text.</param>
    /// <param name="End">Where it ends.</param>
    /// <param name="Line">The zero-based line it starts on.</param>
    /// <param name="SignatureLine">The zero-based signature line, after any attributes.</param>
    /// <remarks>
    /// Holding the symbol rather than a finished identity is what lets the work be skipped when
    /// nobody asks for the declarations.
    /// </remarks>
    internal readonly record struct Written(
        ISymbol? Symbol,
        string? AssemblyAttribute,
        int Start,
        int End,
        int Line,
        int SignatureLine);
}
