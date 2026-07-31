// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Threading;

namespace PublicApiSharp.Analyzers;

/// <summary>
/// The extension-block half of the parser for the slots whose Roslyn has no
/// <c>ExtensionBlockDeclarationSyntax</c> (roslyn4.8 and roslyn4.14).
/// </summary>
/// <remarks>
/// A compiler that cannot parse an extension block cannot produce one to render either, so there is
/// nothing here to recognise.
/// </remarks>
internal static partial class ApiTextParser
{
    /// <summary>Recognises an extension block, which this slot's Roslyn cannot represent.</summary>
    /// <param name="member">The member.</param>
    /// <param name="container">The dotted name of the enclosing namespace and types.</param>
    /// <param name="builder">The declaration builder.</param>
    /// <param name="text">The text being parsed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Always <see langword="false"/> on this slot.</returns>
    private static bool TryVisitExtensionBlock(
        MemberDeclarationSyntax member,
        string container,
        ImmutableArray<ApiDeclaration>.Builder builder,
        SourceText text,
        CancellationToken cancellationToken)
    {
        // The signature is fixed by the shared caller; this slot has nothing to match against.
        _ = member;
        _ = container;
        _ = builder;
        _ = text;
        _ = cancellationToken;
        return false;
    }
}
