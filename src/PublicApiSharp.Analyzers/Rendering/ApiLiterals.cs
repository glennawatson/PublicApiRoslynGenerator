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
    internal static string OperatorToken(string metadataName) => metadataName switch
    {
        "op_Addition" or "op_UnaryPlus" => "+",
        "op_Subtraction" or "op_UnaryNegation" => "-",
        "op_Multiply" => "*",
        "op_Division" => "/",
        "op_Modulus" => "%",
        "op_BitwiseAnd" => "&",
        "op_BitwiseOr" => "|",
        "op_ExclusiveOr" => "^",
        "op_LeftShift" => "<<",
        "op_RightShift" => ">>",
        "op_UnsignedRightShift" => ">>>",
        "op_Equality" => "==",
        "op_Inequality" => "!=",
        "op_LessThan" => "<",
        "op_GreaterThan" => ">",
        "op_LessThanOrEqual" => "<=",
        "op_GreaterThanOrEqual" => ">=",
        "op_LogicalNot" => "!",
        "op_OnesComplement" => "~",
        "op_Increment" => "++",
        "op_Decrement" => "--",
        "op_True" => "true",
        "op_False" => "false",
        _ => metadataName,
    };

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
