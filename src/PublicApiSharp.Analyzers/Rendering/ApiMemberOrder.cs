// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace PublicApiSharp.Analyzers;

/// <summary>Orders the members of a type for rendering.</summary>
/// <remarks>
/// <para>
/// The order has to be a pure function of the symbols, never of their order in source: moving a
/// method up a file is not an API change and must not show up as one in the diff.
/// </para>
/// <para>
/// Members group by kind first — constructors, then fields, properties, events, methods and nested
/// types — because that is how a reader scans an unfamiliar type. Instance members precede static
/// ones within a group, and ties break on name, then generic arity, then parameter count, then
/// parameter types, so overloads sit together in a predictable sequence.
/// </para>
/// </remarks>
internal sealed class ApiMemberOrder : IComparer<ISymbol>
{
    /// <summary>The rank constructors sort under.</summary>
    private const int ConstructorRank = 0;

    /// <summary>The rank fields sort under.</summary>
    private const int FieldRank = 1;

    /// <summary>The rank properties and indexers sort under.</summary>
    private const int PropertyRank = 2;

    /// <summary>The rank events sort under.</summary>
    private const int EventRank = 3;

    /// <summary>The rank methods and operators sort under.</summary>
    private const int MethodRank = 4;

    /// <summary>The rank C# 14 extension blocks sort under.</summary>
    private const int ExtensionBlockRank = 5;

    /// <summary>The rank nested types sort under.</summary>
    private const int NestedTypeRank = 6;

    /// <summary>The rank anything else sorts under.</summary>
    private const int OtherRank = 7;

    /// <summary>Gets the shared comparer.</summary>
    internal static ApiMemberOrder Instance { get; } = new();

    /// <inheritdoc/>
    public int Compare(ISymbol? x, ISymbol? y)
    {
        if (x is not null && y is not null)
        {
            return CompareNonNull(x, y);
        }

        var firstRank = x is null ? 0 : 1;
        var secondRank = y is null ? 0 : 1;
        return firstRank.CompareTo(secondRank);
    }

    /// <summary>Orders two members that are both present.</summary>
    /// <param name="x">The first member.</param>
    /// <param name="y">The second member.</param>
    /// <returns>The comparison result.</returns>
    private static int CompareNonNull(ISymbol x, ISymbol y)
    {
        var result = GroupRank(x).CompareTo(GroupRank(y));
        if (result != 0)
        {
            return result;
        }

        result = (x.IsStatic ? 1 : 0).CompareTo(y.IsStatic ? 1 : 0);
        if (result != 0)
        {
            return result;
        }

        result = string.CompareOrdinal(SortName(x), SortName(y));
        if (result != 0)
        {
            return result;
        }

        return x is IMethodSymbol xMethod && y is IMethodSymbol yMethod ? CompareOverloads(xMethod, yMethod) : 0;
    }

    /// <summary>Orders two members of the same kind and name by their signatures.</summary>
    /// <param name="x">The first method.</param>
    /// <param name="y">The second method.</param>
    /// <returns>The comparison result.</returns>
    private static int CompareOverloads(IMethodSymbol x, IMethodSymbol y)
    {
        var result = x.Arity.CompareTo(y.Arity);
        if (result != 0)
        {
            return result;
        }

        result = x.Parameters.Length.CompareTo(y.Parameters.Length);
        if (result != 0)
        {
            return result;
        }

        for (var i = 0; i < x.Parameters.Length; i++)
        {
            result = string.CompareOrdinal(
                x.Parameters[i].Type.ToDisplayString(ApiDisplayFormats.TypeReference),
                y.Parameters[i].Type.ToDisplayString(ApiDisplayFormats.TypeReference));
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    /// <summary>Gets the kind group a member sorts into.</summary>
    /// <param name="symbol">The member.</param>
    /// <returns>The rank.</returns>
    private static int GroupRank(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => ConstructorRank,
        IFieldSymbol => FieldRank,
        IPropertySymbol => PropertyRank,
        IEventSymbol => EventRank,
        IMethodSymbol => MethodRank,
        INamedTypeSymbol named => RoslynFeatures.IsExtensionContainer(named) ? ExtensionBlockRank : NestedTypeRank,
        _ => OtherRank,
    };

    /// <summary>Gets the name a member sorts under.</summary>
    /// <param name="symbol">The member.</param>
    /// <returns>The sort name.</returns>
    private static string SortName(ISymbol symbol) =>
        symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor }

            // Every constructor of a type sorts equally, leaving arity and parameters to order them.
            ? string.Empty
            : symbol.Name;
}
