// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers;

/// <summary>A rendered API surface: its text, plus the symbol each declaration came from.</summary>
/// <remarks>
/// The symbol map is keyed by line so that a declaration parsed back out of <see cref="Text"/> can
/// be traced to the symbol that produced it. That is what lets a diagnostic about a missing baseline
/// entry be reported on the declaration in the user's own source rather than against the API file.
/// </remarks>
internal sealed class RenderedApiSurface
{
    /// <summary>The symbol whose declaration begins at each line, indexed by line number.</summary>
    private readonly ISymbol?[] _symbolsByLine;

    /// <summary>Initializes a new instance of the <see cref="RenderedApiSurface"/> class.</summary>
    /// <param name="text">The rendered surface text.</param>
    /// <param name="symbolsByLine">The symbol that starts at each line, indexed by zero-based line number.</param>
    internal RenderedApiSurface(string text, ISymbol?[] symbolsByLine)
    {
        Text = text;
        _symbolsByLine = symbolsByLine;
    }

    /// <summary>Gets the rendered surface text.</summary>
    internal string Text { get; }

    /// <summary>Gets the symbol whose declaration begins at a line, if any.</summary>
    /// <param name="line">The zero-based line number.</param>
    /// <returns>The symbol, or <see langword="null"/>.</returns>
    internal ISymbol? SymbolAtLine(int line) =>
        line >= 0 && line < _symbolsByLine.Length ? _symbolsByLine[line] : null;
}
