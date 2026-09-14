// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

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
        var hasParts = false;

        // The primary constraint comes first and there is at most one. 'unmanaged' also sets the
        // value-type flag, so it has to win to avoid writing both.
        if (typeParameter.HasUnmanagedTypeConstraint)
        {
            AppendPart(builder, typeParameter, "unmanaged", ref hasParts);
        }
        else if (typeParameter.HasValueTypeConstraint)
        {
            AppendPart(builder, typeParameter, "struct", ref hasParts);
        }
        else if (typeParameter.HasReferenceTypeConstraint)
        {
            var part = typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated
                ? "class?"
                : "class";
            AppendPart(builder, typeParameter, part, ref hasParts);
        }
        else if (typeParameter.HasNotNullConstraint)
        {
            AppendPart(builder, typeParameter, "notnull", ref hasParts);
        }

        foreach (var constraintType in typeParameter.ConstraintTypes)
        {
            AppendPart(builder, typeParameter, constraintType.ToDisplayString(ApiDisplayFormats.TypeReference), ref hasParts);
        }

        // 'new()' is always last except for the ref-struct permission that follows it.
        if (typeParameter.HasConstructorConstraint)
        {
            AppendPart(builder, typeParameter, "new()", ref hasParts);
        }

        if (!RoslynFeatures.AllowsRefLikeType(typeParameter))
        {
            return;
        }

        AppendPart(builder, typeParameter, "allows ref struct", ref hasParts);
    }

    /// <summary>Starts the clause on its first part and separates subsequent parts.</summary>
    /// <param name="builder">The declaration builder.</param>
    /// <param name="typeParameter">The parameter whose clause is being written.</param>
    /// <param name="part">The next constraint text.</param>
    /// <param name="hasParts">Whether the clause has already started.</param>
    private static void AppendPart(PooledStringBuilder builder, ITypeParameterSymbol typeParameter, string part, ref bool hasParts)
    {
        if (hasParts)
        {
            _ = builder.Append(", ");
        }
        else
        {
            _ = builder.Append(" where ").Append(typeParameter.Name).Append(" : ");
            hasParts = true;
        }

        _ = builder.Append(part);
    }
}
