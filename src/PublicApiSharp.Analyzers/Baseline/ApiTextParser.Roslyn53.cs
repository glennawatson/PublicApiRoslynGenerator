// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Threading;

namespace PublicApiSharp.Analyzers;

/// <summary>
/// The extension-block half of the parser for Roslyn 5.3 and later, where
/// <c>ExtensionBlockDeclarationSyntax</c> exists and a rendered block can be read back.
/// </summary>
internal static partial class ApiTextParser
{
    /// <summary>Recognises and records an extension block and the members it declares.</summary>
    /// <param name="member">The member.</param>
    /// <param name="container">The dotted name of the enclosing namespace and types.</param>
    /// <param name="builder">The declaration builder.</param>
    /// <param name="text">The text being parsed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns><see langword="true"/> when the member was an extension block.</returns>
    internal static bool TryVisitExtensionBlock(
        MemberDeclarationSyntax member,
        string container,
        ImmutableArray<ApiDeclaration>.Builder builder,
        SourceText text,
        CancellationToken cancellationToken)
    {
        if (member is not ExtensionBlockDeclarationSyntax ext)
        {
            return false;
        }

        // An extension block has no name, so its whole header is what identifies it. One class may
        // hold several blocks over the same receiver that differ only in how they constrain it, and
        // those expose different APIs to different callers: an identity stopping at the receiver
        // would let one stand for all of them, so a block would be matched against another block's
        // baseline entry and report a difference that regenerating the file cannot settle.
        var qualified =
            $"{container}.extension{ArityMarker(Arity(ext.TypeParameterList))}{Parameters(ext.ParameterList)}{Constraints(ext.ConstraintClauses)}";
        Add(builder, text, qualified, HeaderSpan(ext, ext.OpenBraceToken));
        VisitMembers(ext.Members, qualified, builder, text, cancellationToken);
        return true;
    }

    /// <summary>Renders constraint clauses as the part of an identity that tells two blocks apart.</summary>
    /// <param name="clauses">The clauses, in the order they are written.</param>
    /// <returns>The clauses without whitespace, or an empty string when there are none.</returns>
    /// <remarks>
    /// Clauses are ordered by the type parameter they constrain, which the renderer follows, so the
    /// text is stable for a given block rather than dependent on how one was typed.
    /// </remarks>
    internal static string Constraints(SyntaxList<TypeParameterConstraintClauseSyntax> clauses)
    {
        if (clauses.Count == 0)
        {
            return string.Empty;
        }

        var builder = new PooledStringBuilder();
        foreach (var clause in clauses)
        {
            _ = builder.Append(RemoveWhitespace(clause.ToString()));
        }

        return builder.ToString();
    }
}
