// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers;

/// <summary>
/// What the roslyn4.8 floor can do. Neither the extension-block symbol API nor the ref-struct
/// constraint API exists here, and the compiler that loads this slot cannot parse either construct,
/// so nothing needing them can reach it.
/// </summary>
internal static class RoslynFeatures
{
    /// <summary>
    /// Gets a value indicating whether an extension block survives a render-and-reparse round trip
    /// on this host, which decides whether one can be written into a baseline at all.
    /// </summary>
    internal static bool SupportsExtensionBlocks => false;

    /// <summary>
    /// Gets a value indicating whether this host understands the <c>allows ref struct</c> constraint,
    /// in syntax and in the symbol model.
    /// </summary>
    internal static bool SupportsRefStructConstraints => false;

    /// <summary>
    /// Gets a value indicating whether this host knows the user-defined compound assignment
    /// operators, whose metadata names it must map back to a token for one to be rendered.
    /// </summary>
    internal static bool SupportsUserDefinedCompoundAssignment => false;

    /// <summary>Determines whether a type is a C# 14 extension container rather than a normal nested type.</summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> when the type is an extension container.</returns>
    internal static bool IsExtensionContainer(INamedTypeSymbol type) => false;

    /// <summary>Determines whether a type parameter permits a ref struct argument.</summary>
    /// <param name="typeParameter">The type parameter.</param>
    /// <returns><see langword="true"/> when the constraint list ends with <c>allows ref struct</c>.</returns>
    internal static bool AllowsRefLikeType(ITypeParameterSymbol typeParameter) => false;

    /// <summary>Gets the receiver an extension container extends.</summary>
    /// <param name="type">The extension container.</param>
    /// <returns>The receiver parameter, or <see langword="null"/> when the host cannot express one.</returns>
    internal static IParameterSymbol? ExtensionReceiver(INamedTypeSymbol type) => null;
}
