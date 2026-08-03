// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers;

/// <summary>
/// One entry of an API surface: a single declaration, paired with the identity that decides whether
/// two entries describe the <em>same</em> member.
/// </summary>
/// <param name="Identity">
/// What makes the member itself, independent of how it is declared — container, kind, name, generic
/// arity and parameter types. Two entries sharing an identity but not a <paramref name="Text"/> are
/// the same member declared differently, which is the case worth reporting as a change rather than
/// as a removal plus an addition.
/// </param>
/// <param name="Text">
/// The rendered declaration, attributes included, with the leading indentation removed. This is what
/// a reviewer reads in the diff.
/// </param>
/// <param name="StartLine">
/// The zero-based line of the declaration's first line — its first attribute, when it has any —
/// within the surface it belongs to.
/// </param>
/// <param name="Span">The declaration's span, attributes included, within that same surface.</param>
/// <remarks>
/// <para>
/// A record because that is exactly what it is: two entries built from the same four values describe
/// the same declaration, and nothing about it has an identity of its own.
/// </para>
/// <para>
/// A class rather than a struct, which was measured rather than assumed. One declaration is held in
/// four places at once — the surface's own array and three lookups — and a struct is copied into
/// every one of them, so the dictionary entries grow from a reference to the whole value. That cost
/// more than the per-instance allocation it removed: 819 MB against 899 MB over the same run.
/// </para>
/// </remarks>
internal sealed record ApiDeclaration(string Identity, string Text, int StartLine, TextSpan Span);
