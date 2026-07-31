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
    private static bool TryVisitExtensionBlock(
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

        // An extension block has no name; what identifies it is the receiver it extends.
        var qualified = $"{container}.extension{Parameters(ext.ParameterList)}";
        Add(builder, text, qualified, HeaderSpan(ext, ext.OpenBraceToken));
        VisitMembers(ext.Members, qualified, builder, text, cancellationToken);
        return true;
    }
}
