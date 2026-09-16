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

        // Arity, receiver type and constraints identify the API exposed by the block and its members.
        // The comparison also uses the header to pair blocks whose receiver parameter names differ.
        var qualified =
            $"{container}.extension{ArityMarker(Arity(ext.TypeParameterList))}{Parameters(ext.ParameterList)}{Constraints(ext.ConstraintClauses)}";
        Add(builder, text, qualified, HeaderSpan(ext, ext.OpenBraceToken), isExtensionBlock: true);
        VisitMembers(ext.Members, qualified, builder, text, cancellationToken);
        return true;
    }
}
