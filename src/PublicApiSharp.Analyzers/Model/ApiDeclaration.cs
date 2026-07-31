// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers;

/// <summary>
/// One entry of an API surface: a single declaration, paired with the identity that decides whether
/// two entries describe the <em>same</em> member.
/// </summary>
internal sealed class ApiDeclaration
{
    /// <summary>Initializes a new instance of the <see cref="ApiDeclaration"/> class.</summary>
    /// <param name="identity">The declaration's identity.</param>
    /// <param name="text">The rendered declaration.</param>
    /// <param name="startLine">The zero-based line the declaration starts on.</param>
    /// <param name="span">The declaration's span.</param>
    internal ApiDeclaration(string identity, string text, int startLine, TextSpan span)
    {
        Identity = identity;
        Text = text;
        StartLine = startLine;
        Span = span;
    }

    /// <summary>
    /// Gets what makes the member itself, independent of how it is declared — container, kind, name,
    /// generic arity and parameter types. Two entries sharing an identity but not a <see cref="Text"/>
    /// are the same member declared differently, which is the case worth reporting as a change rather
    /// than as a removal plus an addition.
    /// </summary>
    internal string Identity { get; }

    /// <summary>
    /// Gets the rendered declaration, attributes included, with the leading indentation removed.
    /// This is what a reviewer reads in the diff.
    /// </summary>
    internal string Text { get; }

    /// <summary>
    /// Gets the zero-based line of the declaration's first line — its first attribute, when it has
    /// any — within the text it was parsed from.
    /// </summary>
    internal int StartLine { get; }

    /// <summary>Gets the declaration's span, attributes included, within the text it was parsed from.</summary>
    internal TextSpan Span { get; }
}
