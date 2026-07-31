// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers;

/// <summary>The <see cref="SymbolDisplayFormat"/> values the renderer prints signatures with.</summary>
/// <remarks>
/// <para>
/// Signatures come from Roslyn's own symbol display rather than from hand-written printing, and
/// that is the point of the whole design: a language feature that Roslyn already knows how to
/// display — <c>required</c>, <c>scoped</c>, params spans, ref-struct constraints, whatever C# adds
/// next — renders correctly the moment the package is loaded by a host compiler new enough to have
/// it, with no change here.
/// </para>
/// <para>
/// Accessibility and modifiers are deliberately <em>not</em> taken from these formats even though
/// the options exist. Roslyn suppresses them for interface members, so a static abstract method
/// would print as a bare signature; see <see cref="ApiModifiers"/>, which composes them uniformly
/// instead.
/// </para>
/// </remarks>
internal static class ApiDisplayFormats
{
    /// <summary>
    /// A reference to a type from inside a signature — fully qualified, with keyword aliases and
    /// nullable annotations. Used for base types, interfaces and attribute argument types.
    /// </summary>
    internal static readonly SymbolDisplayFormat TypeReference = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: Miscellaneous);

    /// <summary>
    /// A member's signature without accessibility or modifiers: return type, name, type parameters
    /// with their constraints, and the parameter list with names and default values.
    /// </summary>
    internal static readonly SymbolDisplayFormat MemberSignature = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters
            | SymbolDisplayGenericsOptions.IncludeTypeConstraints,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeType
            | SymbolDisplayMemberOptions.IncludeExplicitInterface
            | SymbolDisplayMemberOptions.IncludeConstantValue,
        parameterOptions: SymbolDisplayParameterOptions.IncludeExtensionThis
            | SymbolDisplayParameterOptions.IncludeParamsRefOut
            | SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeDefaultValue,
        propertyStyle: SymbolDisplayPropertyStyle.NameOnly,
        miscellaneousOptions: Miscellaneous);

    /// <summary>
    /// A type's own name in its declaration, unqualified, because the enclosing namespace and types
    /// are already written around it. Constraints are excluded because C# writes them after the base
    /// list, not after the name.
    /// </summary>
    internal static readonly SymbolDisplayFormat TypeDeclarationName = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters
            | SymbolDisplayGenericsOptions.IncludeVariance,
        miscellaneousOptions: Miscellaneous);

    /// <summary>
    /// The type declaration name with the constraint clauses appended. The renderer takes the
    /// difference between this and <see cref="TypeDeclarationName"/> to obtain the constraints on
    /// their own, which is exact and avoids searching the text for a keyword.
    /// </summary>
    internal static readonly SymbolDisplayFormat TypeDeclarationNameWithConstraints = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters
            | SymbolDisplayGenericsOptions.IncludeVariance
            | SymbolDisplayGenericsOptions.IncludeTypeConstraints,
        miscellaneousOptions: Miscellaneous);

    /// <summary>A single parameter, with its reference kind, type, name and default value.</summary>
    internal static readonly SymbolDisplayFormat Parameter = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        parameterOptions: SymbolDisplayParameterOptions.IncludeExtensionThis
            | SymbolDisplayParameterOptions.IncludeParamsRefOut
            | SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeDefaultValue,
        miscellaneousOptions: Miscellaneous);

    /// <summary>The fully qualified name of a namespace or type, used for grouping and ordering.</summary>
    internal static readonly SymbolDisplayFormat QualifiedName = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.None);

    /// <summary>
    /// Keyword aliases for the built-in types and nullable annotations on reference types. Both are
    /// how a developer writes the type, so both belong in text meant to read like source.
    /// </summary>
    private const SymbolDisplayMiscellaneousOptions Miscellaneous =
        SymbolDisplayMiscellaneousOptions.UseSpecialTypes
        | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier;
}
