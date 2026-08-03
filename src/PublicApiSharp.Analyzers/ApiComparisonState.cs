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
    /// <returns>The comparison state.</returns>
    internal static ApiComparisonState Create(
        Compilation compilation,
        ApiTextParseResult baseline,
        ApiRenderOptions options,
        CancellationToken cancellationToken) =>
        Create(ApiSurfaceRenderer.Render(compilation, options, cancellationToken), baseline, cancellationToken);

    /// <summary>Builds the comparison from a surface that has already been rendered.</summary>
    /// <param name="surface">The rendered surface.</param>
    /// <param name="baseline">The parsed baseline.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The comparison state.</returns>
    /// <remarks>
    /// The surface arrives already stating its declarations, so there is nothing here that can fail.
    /// It used to be parsed back out of its own text, and a surface this package had rendered badly
    /// enough to be unreadable abandoned the comparison rather than blaming the consumer for it.
    /// What guards that now is <c>RenderedSurfaceParsesBackAsync</c>, which holds the renderer to
    /// output C# can read, and the baseline's own parse, which reports PAS0005 if one ever escaped.
    /// </remarks>
    internal static ApiComparisonState Create(
        RenderedApiSurface surface,
        ApiTextParseResult baseline,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var declarationsBySymbol = new Dictionary<ISymbol, ApiDeclaration>(SymbolEqualityComparer.Default);
        foreach (var declaration in surface.Declarations)
        {
            if (surface.SymbolAtLine(declaration.StartLine) is { } symbol)
            {
                declarationsBySymbol[symbol] = declaration;
            }
        }

        return new(
            Index(baseline.Declarations),
            Index(surface.Declarations),
            declarationsBySymbol);
    }

    /// <summary>Indexes declarations by identity.</summary>
    /// <param name="declarations">The declarations.</param>
    /// <returns>The lookup.</returns>
    internal static Dictionary<string, ApiDeclaration> Index(ImmutableArray<ApiDeclaration> declarations)
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
