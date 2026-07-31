// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers;

/// <summary>
/// The outcome of reading API surface text: either the declarations it describes, or the syntax
/// error that stopped it being read.
/// </summary>
/// <remarks>
/// Malformed text is kept distinct from "no declarations" on purpose. A baseline that fails to parse
/// would otherwise look like an empty baseline, and every member in the assembly would be reported
/// as newly added — burying the one-line hand-edit that actually caused it.
/// </remarks>
internal sealed class ApiTextParseResult
{
    /// <summary>Initializes a new instance of the <see cref="ApiTextParseResult"/> class.</summary>
    /// <param name="declarations">The declarations that were read.</param>
    /// <param name="error">The syntax error, or <see langword="null"/>.</param>
    /// <param name="errorSpan">The span the error was reported at.</param>
    private ApiTextParseResult(ImmutableArray<ApiDeclaration> declarations, string? error, TextSpan errorSpan)
    {
        Declarations = declarations;
        Error = error;
        ErrorSpan = errorSpan;
    }

    /// <summary>Gets the declarations, empty when <see cref="Error"/> is set.</summary>
    internal ImmutableArray<ApiDeclaration> Declarations { get; }

    /// <summary>Gets the syntax error that stopped the text being read, or <see langword="null"/>.</summary>
    internal string? Error { get; }

    /// <summary>Gets the span of <see cref="Error"/> within the text.</summary>
    internal TextSpan ErrorSpan { get; }

    /// <summary>Gets a value indicating whether the text was read successfully.</summary>
    internal bool Success => Error is null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="declarations">The declarations that were read.</param>
    /// <returns>The result.</returns>
    internal static ApiTextParseResult Parsed(ImmutableArray<ApiDeclaration> declarations) =>
        new(declarations, null, default);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The syntax error message.</param>
    /// <param name="errorSpan">The span the error was reported at.</param>
    /// <returns>The result.</returns>
    internal static ApiTextParseResult Malformed(string error, TextSpan errorSpan) =>
        new(ImmutableArray<ApiDeclaration>.Empty, error, errorSpan);
}
