// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace RoslynCommon.Analyzers;

/// <summary>
/// Tiny allocation-light factories for the <see cref="ImmutableArray{T}"/> values analyzers and
/// code fixes expose (SupportedDiagnostics, FixableDiagnosticIds).
/// </summary>
/// <remarks>
/// The roslyn4.8 floor ships an <see cref="ImmutableArray{T}"/> that predates
/// collection-expression support (CS9210), so a literal <c>[item]</c> will not compile here.
/// </remarks>
internal static class ImmutableArrays
{
    /// <summary>Creates a single-element immutable array.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="item">The only element.</param>
    /// <returns>An immutable array containing <paramref name="item"/>.</returns>
    internal static ImmutableArray<T> Of<T>(T item) => ImmutableArray.Create(item);

    /// <summary>Creates an immutable array from the supplied elements.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="items">The elements.</param>
    /// <returns>An immutable array containing <paramref name="items"/>.</returns>
    internal static ImmutableArray<T> Of<T>(params T[] items) => ImmutableArray.Create(items);
}
