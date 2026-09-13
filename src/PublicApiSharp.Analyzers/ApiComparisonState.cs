// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    /// <summary>Sorted comparison keys used only to test whether a baseline declaration still exists.</summary>
    private readonly string[] _currentIdentities;

    /// <summary>Initializes a new instance of the <see cref="ApiComparisonState"/> class.</summary>
    /// <param name="baselineByIdentity">The baseline's declarations, keyed by identity.</param>
    /// <param name="currentIdentities">The rendered surface's sorted comparison keys.</param>
    /// <param name="declarationsBySymbol">The rendered declaration for each symbol that produced one.</param>
    private ApiComparisonState(
        Dictionary<string, ApiDeclaration> baselineByIdentity,
        string[] currentIdentities,
        Dictionary<ISymbol, ApiDeclaration> declarationsBySymbol)
    {
        BaselineByIdentity = baselineByIdentity;
        _currentIdentities = currentIdentities;
        DeclarationsBySymbol = declarationsBySymbol;
    }

    /// <summary>Gets the baseline's declarations, keyed by identity.</summary>
    internal Dictionary<string, ApiDeclaration> BaselineByIdentity { get; }

    /// <summary>Gets the rendered declaration for each symbol that produced one.</summary>
    internal Dictionary<ISymbol, ApiDeclaration> DeclarationsBySymbol { get; }

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

        var baselineByIdentity = Index(baseline.Declarations);
        var currentIdentities = IndexIdentities(surface.Declarations);
        var declarationsBySymbol = new Dictionary<ISymbol, ApiDeclaration>(surface.Declarations.Length, SymbolEqualityComparer.Default);
        foreach (var declaration in surface.Declarations)
        {
            if (surface.SymbolAtLine(declaration.StartLine) is { } symbol)
            {
                if (declaration.IsExtensionBlock)
                {
                    var key = ComparisonIdentity(declaration);
                    PairExtensionBlock(declaration, key, baselineByIdentity, currentIdentities);
                    declarationsBySymbol[symbol] = declaration with { Identity = key };
                }
                else
                {
                    declarationsBySymbol[symbol] = declaration;
                }
            }
        }

        return new(
            baselineByIdentity,
            currentIdentities,
            declarationsBySymbol);
    }

    /// <summary>Indexes declarations, retaining distinct extension headers within a receiver identity.</summary>
    /// <param name="declarations">The declarations.</param>
    /// <returns>The lookup.</returns>
    internal static Dictionary<string, ApiDeclaration> Index(ImmutableArray<ApiDeclaration> declarations)
    {
        var map = new Dictionary<string, ApiDeclaration>(declarations.Length, StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            var key = ComparisonIdentity(declaration);

            // Duplicate entries in a hand-edited baseline still keep the first declaration.
            if (!map.ContainsKey(key))
            {
                map.Add(key, declaration);
            }
        }

        return map;
    }

    /// <summary>Determines whether a baseline comparison key remains in the rendered surface.</summary>
    /// <param name="identity">The comparison key, including the full header for an extension block.</param>
    /// <returns>Whether the current surface contains the key.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool ContainsCurrentIdentity(string identity) =>
        Array.BinarySearch(_currentIdentities, identity, StringComparer.Ordinal) >= 0;

    /// <summary>Collects membership keys without retaining a second declaration lookup.</summary>
    /// <param name="declarations">The rendered declarations.</param>
    /// <returns>The comparison keys in ordinal order.</returns>
    /// <remarks>Render order groups member kinds, so it is not ordinal identity order.</remarks>
    private static string[] IndexIdentities(ImmutableArray<ApiDeclaration> declarations)
    {
        var identities = new string[declarations.Length];
        for (var index = 0; index < declarations.Length; index++)
        {
            identities[index] = ComparisonIdentity(declarations[index]);
        }

        Array.Sort(identities, StringComparer.Ordinal);
        return identities;
    }

    /// <summary>Distinguishes extension headers without changing the identities of their members.</summary>
    /// <param name="declaration">The declaration to index.</param>
    /// <returns>The comparison key, including the full text for an extension block.</returns>
    private static string ComparisonIdentity(ApiDeclaration declaration)
    {
        if (!declaration.IsExtensionBlock)
        {
            return declaration.Identity;
        }

        var builder = new PooledStringBuilder();
        _ = builder.Append(declaration.Identity).Append('\n').Append(declaration.Text);
        return builder.ToString();
    }

    /// <summary>Pairs a changed header with an unclaimed baseline block after reserving exact matches.</summary>
    /// <param name="declaration">The current extension block.</param>
    /// <param name="key">Its full comparison key.</param>
    /// <param name="baseline">The baseline index, updated to use the current key for a matched block.</param>
    /// <param name="current">All current keys, including exact matches that must remain reserved.</param>
    private static void PairExtensionBlock(
        ApiDeclaration declaration,
        string key,
        Dictionary<string, ApiDeclaration> baseline,
        string[] current)
    {
        if (baseline.ContainsKey(key))
        {
            return;
        }

        foreach (var candidate in baseline)
        {
            if (candidate.Value.IsExtensionBlock
                && string.Equals(candidate.Value.Identity, declaration.Identity, StringComparison.Ordinal)
                && Array.BinarySearch(current, candidate.Key, StringComparer.Ordinal) < 0)
            {
                // Re-keying consumes this candidate and prevents a second changed block reusing it.
                _ = baseline.Remove(candidate.Key);
                baseline.Add(key, candidate.Value);
                return;
            }
        }
    }
}
