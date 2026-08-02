// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;

namespace PublicApiSharp.Analyzers;

/// <summary>Spells values and operator names the way C# source writes them.</summary>
/// <remarks>
/// These are about the language's own notation rather than the shape of the surface, and neither
/// depends on anything the renderer is holding, so they sit apart from it.
/// </remarks>
internal static class ApiLiterals
{
    /// <summary>The prefix the metadata name of a checked operator carries.</summary>
    private const string CheckedPrefix = "op_Checked";

    /// <summary>Writes an identifier the way source must spell it.</summary>
    /// <param name="name">The symbol's plain name.</param>
    /// <returns>The name, escaped when it collides with a keyword.</returns>
    /// <remarks>
    /// A symbol declared as <c>@class</c> has the plain name <c>class</c>. Writing that into the
    /// surface produces text that is not C#, which cannot be read back, and a surface that cannot be
    /// read back stops the baseline being enforced at all rather than failing loudly.
    /// </remarks>
    internal static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : $"@{name}";

    /// <summary>Maps an operator's metadata name back to the token it overloads.</summary>
    /// <param name="metadataName">The metadata name.</param>
    /// <returns>The operator token.</returns>
    /// <remarks>
    /// <para>
    /// The mapping is the host compiler's own, not a list kept here. That matters twice over: it
    /// already covers every family the compiler can parse — including the checked and compound
    /// assignment forms — and it gains whatever C# adds next at the moment the compiler does.
    /// </para>
    /// <para>
    /// A name it does not recognise falls back to itself, which is not C# and would stop the surface
    /// reading back. Nothing reaches that: an operator can only be in a compilation the host
    /// compiler parsed, and what it parsed is exactly what its own table describes.
    /// </para>
    /// </remarks>
    internal static string OperatorToken(string metadataName)
    {
        var token = SyntaxFacts.GetText(SyntaxFacts.GetOperatorKind(metadataName));
        return token.Length == 0 ? metadataName : token;
    }

    /// <summary>Determines whether an operator's metadata name is that of its checked form.</summary>
    /// <param name="metadataName">The metadata name.</param>
    /// <returns><see langword="true"/> for a checked operator.</returns>
    /// <remarks>
    /// The checked form is a member of its own that a caller reaches from inside a <c>checked</c>
    /// context, and the token it overloads is the same one, so only the keyword tells the two apart.
    /// </remarks>
    internal static bool IsCheckedOperator(string metadataName) =>
        metadataName.StartsWith(CheckedPrefix, StringComparison.Ordinal);

    /// <summary>Formats a constant value as the C# literal a reader would write.</summary>
    /// <param name="value">The constant value.</param>
    /// <returns>The literal.</returns>
    internal static string FormatConstant(object? value) => value switch
    {
        null => "null",
        string text => SymbolDisplay.FormatLiteral(text, quote: true),
        char character => SymbolDisplay.FormatLiteral(character, quote: true),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
