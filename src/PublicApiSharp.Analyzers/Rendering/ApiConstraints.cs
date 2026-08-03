// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace PublicApiSharp.Analyzers;

/// <summary>Renders the <c>where</c> clauses of a generic type or method.</summary>
/// <remarks>
/// Roslyn's symbol display emits constraints for a named type but not for a method, so a generic
/// method's clauses would silently vanish if this were left to it. Constraints are API: they decide
/// what a caller is allowed to substitute, and tightening one breaks code that used to compile.
/// Composing them here covers both cases with one implementation.
/// </remarks>
internal static class ApiConstraints
{
    /// <summary>Appends the constraint clauses for a set of type parameters.</summary>
    /// <param name="builder">The builder the declaration is being written into.</param>
    /// <param name="typeParameters">The type parameters.</param>
    /// <remarks>Each clause is preceded by a space; nothing is written when none are constrained.</remarks>
    internal static void Append(PooledStringBuilder builder, ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        foreach (var typeParameter in typeParameters)
        {
            AppendClause(builder, typeParameter);
        }
    }

    /// <summary>Appends one type parameter's clause, if it has any constraints.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="typeParameter">The type parameter.</param>
    internal static void AppendClause(PooledStringBuilder builder, ITypeParameterSymbol typeParameter)
    {
        var parts = Parts(typeParameter);
        if (parts.Count == 0)
        {
            return;
        }

        _ = builder.Append(" where ").Append(typeParameter.Name).Append(" : ");
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            _ = builder.Append(parts[i]);
        }
    }

    /// <summary>Collects a type parameter's constraints in the order C# requires them.</summary>
    /// <param name="typeParameter">The type parameter.</param>
    /// <returns>The constraint texts.</returns>
    internal static List<string> Parts(ITypeParameterSymbol typeParameter)
    {
        var parts = new List<string>();

        // The primary constraint comes first and there is at most one. 'unmanaged' also sets the
        // value-type flag, so it has to win to avoid writing both.
        if (typeParameter.HasUnmanagedTypeConstraint)
        {
            parts.Add("unmanaged");
        }
        else if (typeParameter.HasValueTypeConstraint)
        {
            parts.Add("struct");
        }
        else if (typeParameter.HasReferenceTypeConstraint)
        {
            parts.Add(typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                ? "class?"
                : "class");
        }
        else if (typeParameter.HasNotNullConstraint)
        {
            parts.Add("notnull");
        }

        foreach (var constraintType in typeParameter.ConstraintTypes)
        {
            parts.Add(constraintType.ToDisplayString(ApiDisplayFormats.TypeReference));
        }

        // 'new()' is always last except for the ref-struct permission that follows it.
        if (typeParameter.HasConstructorConstraint)
        {
            parts.Add("new()");
        }

        if (RoslynFeatures.AllowsRefLikeType(typeParameter))
        {
            parts.Add("allows ref struct");
        }

        return parts;
    }
}
