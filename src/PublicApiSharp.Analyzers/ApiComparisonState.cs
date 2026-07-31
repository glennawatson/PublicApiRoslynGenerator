// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading;

namespace PublicApiSharp.Analyzers;

/// <summary>
/// Everything the comparison needs, rendered and parsed once for a compilation and then shared by
/// every per-symbol callback.
/// </summary>
/// <remarks>
/// The surface has to be rendered whole — a declaration's text depends on nothing but its own
/// symbol, but knowing whether the <em>baseline</em> still matches means having read the file. Doing
/// that once behind a <see cref="System.Lazy{T}"/> keeps the per-symbol path to a dictionary lookup,
/// which is what lets the added and changed rules run as symbol actions instead of at compilation
/// end. That distinction is not cosmetic: a diagnostic reported from a compilation action is not
/// local to a document, and Roslyn will not offer a code fix for it.
/// </remarks>
internal sealed class ApiComparisonState
{
    /// <summary>Initializes a new instance of the <see cref="ApiComparisonState"/> class.</summary>
    /// <param name="baselineByIdentity">The baseline's declarations, keyed by identity.</param>
    /// <param name="currentByIdentity">The rendered surface's declarations, keyed by identity.</param>
    /// <param name="declarationsBySymbol">The rendered declaration for each symbol that produced one.</param>
    private ApiComparisonState(
        Dictionary<string, ApiDeclaration> baselineByIdentity,
        Dictionary<string, ApiDeclaration> currentByIdentity,
        Dictionary<ISymbol, ApiDeclaration> declarationsBySymbol)
    {
        BaselineByIdentity = baselineByIdentity;
        CurrentByIdentity = currentByIdentity;
        DeclarationsBySymbol = declarationsBySymbol;
    }

    /// <summary>Gets the baseline's declarations, keyed by identity.</summary>
    internal Dictionary<string, ApiDeclaration> BaselineByIdentity { get; }

    /// <summary>Gets the rendered surface's declarations, keyed by identity.</summary>
    internal Dictionary<string, ApiDeclaration> CurrentByIdentity { get; }

    /// <summary>Gets the rendered declaration for each symbol that produced one.</summary>
    internal Dictionary<ISymbol, ApiDeclaration> DeclarationsBySymbol { get; }

    /// <summary>Renders the compilation and pairs it with the parsed baseline.</summary>
    /// <param name="compilation">The compilation.</param>
    /// <param name="baseline">The parsed baseline.</param>
    /// <param name="options">The render options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The comparison state, or <see langword="null"/> when the rendering cannot be read back.</returns>
    internal static ApiComparisonState? Create(
        Compilation compilation,
        ApiTextParseResult baseline,
        ApiRenderOptions options,
        CancellationToken cancellationToken)
    {
        var surface = ApiSurfaceRenderer.Render(compilation, options, cancellationToken);
        var surfaceParse = ApiTextParser.Parse(SourceText.From(surface.Text), cancellationToken);
        if (!surfaceParse.Success)
        {
            // The renderer produced text it cannot read back. That is a defect in this package
            // rather than anything the user can act on, so the caller stays silent.
            return null;
        }

        var declarationsBySymbol = new Dictionary<ISymbol, ApiDeclaration>(SymbolEqualityComparer.Default);
        foreach (var declaration in surfaceParse.Declarations)
        {
            if (surface.SymbolAtLine(declaration.StartLine) is { } symbol)
            {
                declarationsBySymbol[symbol] = declaration;
            }
        }

        return new(
            Index(baseline.Declarations),
            Index(surfaceParse.Declarations),
            declarationsBySymbol);
    }

    /// <summary>Indexes declarations by identity.</summary>
    /// <param name="declarations">The declarations.</param>
    /// <returns>The lookup.</returns>
    private static Dictionary<string, ApiDeclaration> Index(ImmutableArray<ApiDeclaration> declarations)
    {
        var map = new Dictionary<string, ApiDeclaration>(declarations.Length, System.StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            // A duplicate identity can only come from a hand-edited baseline; the first wins so the
            // comparison stays deterministic.
            if (!map.ContainsKey(declaration.Identity))
            {
                map.Add(declaration.Identity, declaration);
            }
        }

        return map;
    }
}
