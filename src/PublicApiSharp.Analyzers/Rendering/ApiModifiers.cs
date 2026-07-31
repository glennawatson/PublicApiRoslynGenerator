// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers;

/// <summary>Composes the accessibility and modifier keywords that precede a declaration.</summary>
/// <remarks>
/// Roslyn's symbol display can emit modifiers, but it suppresses them for interface members — a
/// static abstract interface method comes back as a bare signature, losing the part that makes it a
/// generic-math constraint target. Composing them here keeps one rule for every member kind and
/// keeps the output honest about what a consumer can actually do. Keywords are written in the order
/// C# itself writes them.
/// </remarks>
internal static class ApiModifiers
{
    /// <summary>Appends the modifier prefix for a member declaration.</summary>
    /// <param name="builder">The builder the declaration is being written into.</param>
    /// <param name="member">The member.</param>
    /// <remarks>The prefix includes a trailing space; nothing is written when there is no prefix.</remarks>
    internal static void AppendMember(PooledStringBuilder builder, ISymbol member)
    {
        // An explicit interface implementation cannot carry accessibility or modifiers at all.
        if (IsExplicitInterfaceImplementation(member))
        {
            return;
        }

        var inInterface = member.ContainingType is { TypeKind: TypeKind.Interface };

        // Interface members are public by default and conventionally written without the keyword.
        if (!inInterface || member.DeclaredAccessibility != Accessibility.Public)
        {
            AppendAccessibility(builder, member.DeclaredAccessibility);
        }

        AppendStorageModifiers(builder, member);
        AppendInheritanceModifiers(builder, member, inInterface);
        AppendStateModifiers(builder, member);
    }

    /// <summary>Appends the modifier prefix and type keyword for a type declaration.</summary>
    /// <param name="builder">The builder the declaration is being written into.</param>
    /// <param name="type">The type.</param>
    /// <remarks>The prefix ends with the type keyword and a trailing space.</remarks>
    internal static void AppendType(PooledStringBuilder builder, INamedTypeSymbol type)
    {
        AppendAccessibility(builder, type.DeclaredAccessibility);

        if (type.TypeKind == TypeKind.Class)
        {
            AppendClassModifiers(builder, type);
        }

        if (type is { TypeKind: TypeKind.Struct, IsReadOnly: true })
        {
            Append(builder, "readonly");
        }

        if (type is { TypeKind: TypeKind.Struct, IsRefLikeType: true })
        {
            Append(builder, "ref");
        }

        if (type.IsRecord)
        {
            Append(builder, "record");
        }

        // 'record class' is legal but redundant; C# is conventionally written with 'record' alone,
        // and the struct form still needs its keyword to say which it is.
        if (type.IsRecord && type.TypeKind == TypeKind.Class)
        {
            return;
        }

        Append(builder, TypeKeyword(type));
    }

    /// <summary>Gets the C# keyword introducing a type declaration.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The keyword.</returns>
    internal static string TypeKeyword(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Interface => "interface",
        TypeKind.Struct => "struct",
        TypeKind.Enum => "enum",
        TypeKind.Delegate => "delegate",
        _ => "class",
    };

    /// <summary>Determines whether a member is an explicit interface implementation.</summary>
    /// <param name="member">The member.</param>
    /// <returns><see langword="true"/> when the member explicitly implements an interface member.</returns>
    internal static bool IsExplicitInterfaceImplementation(ISymbol member) => member switch
    {
        IMethodSymbol method => !method.ExplicitInterfaceImplementations.IsEmpty,
        IPropertySymbol property => !property.ExplicitInterfaceImplementations.IsEmpty,
        IEventSymbol evt => !evt.ExplicitInterfaceImplementations.IsEmpty,
        _ => false,
    };

    /// <summary>Appends the const, static and extern keywords.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="member">The member.</param>
    private static void AppendStorageModifiers(PooledStringBuilder builder, ISymbol member)
    {
        // 'const' implies static; writing both would not compile.
        if (member is IFieldSymbol { IsConst: true })
        {
            Append(builder, "const");
        }
        else if (member.IsStatic)
        {
            Append(builder, "static");
        }

        if (!member.IsExtern)
        {
            return;
        }

        Append(builder, "extern");
    }

    /// <summary>Appends the abstract, virtual, sealed and override keywords.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="member">The member.</param>
    /// <param name="inInterface">Whether the member is declared in an interface.</param>
    private static void AppendInheritanceModifiers(PooledStringBuilder builder, ISymbol member, bool inInterface)
    {
        // A non-static interface member is abstract by default, so the keyword is only meaningful
        // on a static one (or on a class member).
        if (member.IsAbstract && (!inInterface || member.IsStatic))
        {
            Append(builder, "abstract");
        }
        else if (member.IsVirtual && !inInterface)
        {
            Append(builder, "virtual");
        }

        if (!member.IsOverride)
        {
            return;
        }

        if (member.IsSealed)
        {
            Append(builder, "sealed");
        }

        Append(builder, "override");
    }

    /// <summary>Appends the readonly and required keywords.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="member">The member.</param>
    private static void AppendStateModifiers(PooledStringBuilder builder, ISymbol member)
    {
        if (member is IFieldSymbol { IsReadOnly: true, IsConst: false } or IMethodSymbol { IsReadOnly: true })
        {
            Append(builder, "readonly");
        }

        if (member is not (IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
        {
            return;
        }

        Append(builder, "required");
    }

    /// <summary>Appends the modifiers that only apply to a class declaration.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="type">The class.</param>
    private static void AppendClassModifiers(PooledStringBuilder builder, INamedTypeSymbol type)
    {
        // A static class is abstract and sealed in metadata; writing all three would not compile.
        if (type.IsStatic)
        {
            Append(builder, "static");
            return;
        }

        if (type.IsAbstract)
        {
            Append(builder, "abstract");
        }

        if (!type.IsSealed || type.IsRecord)
        {
            return;
        }

        Append(builder, "sealed");
    }

    /// <summary>Appends the keyword for an accessibility that survives outside the assembly.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="accessibility">The accessibility.</param>
    private static void AppendAccessibility(PooledStringBuilder builder, Accessibility accessibility)
    {
        // Only the protected half of 'protected internal' survives outside the assembly, and that
        // is what a consumer can rely on, so that is what the surface records.
        if (accessibility == Accessibility.Public)
        {
            Append(builder, "public");
        }
        else if (accessibility is Accessibility.Protected or Accessibility.ProtectedOrInternal)
        {
            Append(builder, "protected");
        }
    }

    /// <summary>Appends a keyword and the space that separates it from the next one.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="keyword">The keyword.</param>
    private static void Append(PooledStringBuilder builder, string keyword) =>
        builder.Append(keyword).Append(' ');
}
