// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers;

/// <summary>
/// The one place that knows what the host Roslyn can and cannot do, so every other file stays free
/// of version conditionals.
/// </summary>
/// <remarks>
/// <para>
/// The slots do not gain a feature at the same moment on the symbol side and the syntax side, and
/// this package needs both: it renders the surface from symbols and then reads that text back with
/// the parser. C# 14 extension blocks are the case in point.
/// </para>
/// <list type="table">
/// <item>
/// <term>roslyn4.8</term>
/// <description>
/// Neither <c>ITypeSymbol.IsExtension</c> nor the syntax exists. The floor compiler cannot parse an
/// extension block either, so no compilation reaching this slot can contain one.
/// </description>
/// </item>
/// <item>
/// <term>roslyn4.14</term>
/// <description>
/// <c>ITypeSymbol.IsExtension</c> exists, but <c>ExtensionBlockDeclarationSyntax</c> does not.
/// Extension containers are therefore recognised and skipped: their compiler-generated names are
/// not expressible in C#, so rendering them would produce a baseline this slot could not read back.
/// </description>
/// </item>
/// <item>
/// <term>roslyn5.3</term>
/// <description>Both halves exist, so extension blocks round-trip and are rendered in full.</description>
/// </item>
/// </list>
/// </remarks>
internal static class RoslynFeatures
{
    /// <summary>
    /// Gets a value indicating whether an extension block survives a render-and-reparse round trip
    /// on this host, which is what decides whether one can be written into a baseline at all.
    /// </summary>
    internal static bool SupportsExtensionBlocks =>
#if ROSLYN_5_OR_GREATER
        true;
#else
        false;
#endif

    /// <summary>Determines whether a type is a C# 14 extension container rather than a normal nested type.</summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> when the type is an extension container.</returns>
    internal static bool IsExtensionContainer(INamedTypeSymbol type) =>
#if ROSLYN_4_14_OR_GREATER
        type.IsExtension;
#else
        false;
#endif

    /// <summary>Determines whether a type parameter permits a ref struct argument.</summary>
    /// <param name="typeParameter">The type parameter.</param>
    /// <returns><see langword="true"/> when the constraint list ends with <c>allows ref struct</c>.</returns>
    /// <remarks><c>ITypeParameterSymbol.AllowsRefLikeType</c> arrives in Roslyn 4.14.</remarks>
    internal static bool AllowsRefLikeType(ITypeParameterSymbol typeParameter) =>
#if ROSLYN_4_14_OR_GREATER
        typeParameter.AllowsRefLikeType;
#else
        false;
#endif

    /// <summary>Gets the receiver an extension container extends.</summary>
    /// <param name="type">The extension container.</param>
    /// <returns>The receiver parameter, or <see langword="null"/> when the host cannot express one.</returns>
    internal static IParameterSymbol? ExtensionReceiver(INamedTypeSymbol type) =>
#if ROSLYN_4_14_OR_GREATER
        type.ExtensionParameter;
#else
        null;
#endif
}
