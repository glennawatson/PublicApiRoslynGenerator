// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers;

/// <summary>
/// What Roslyn 4.14 can do. The symbol model describes extension containers and ref-struct
/// constraints, but the parser has no <c>ExtensionBlockDeclarationSyntax</c>, so an extension block
/// rendered here could not be read back and is left out of the baseline instead.
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
    internal static bool SupportsRefStructConstraints => true;

    /// <summary>Determines whether a type is a C# 14 extension container rather than a normal nested type.</summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> when the type is an extension container.</returns>
    internal static bool IsExtensionContainer(INamedTypeSymbol type) => type.IsExtension;

    /// <summary>Determines whether a type parameter permits a ref struct argument.</summary>
    /// <param name="typeParameter">The type parameter.</param>
    /// <returns><see langword="true"/> when the constraint list ends with <c>allows ref struct</c>.</returns>
    internal static bool AllowsRefLikeType(ITypeParameterSymbol typeParameter) => typeParameter.AllowsRefLikeType;

    /// <summary>Gets the receiver an extension container extends.</summary>
    /// <param name="type">The extension container.</param>
    /// <returns>The receiver parameter, or <see langword="null"/> when the host cannot express one.</returns>
    internal static IParameterSymbol? ExtensionReceiver(INamedTypeSymbol type) => type.ExtensionParameter;
}
