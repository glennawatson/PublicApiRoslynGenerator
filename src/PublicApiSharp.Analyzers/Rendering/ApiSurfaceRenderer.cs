// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;

namespace PublicApiSharp.Analyzers;

/// <summary>Renders a compilation's externally visible surface as C# declaration text.</summary>
/// <remarks>
/// <para>
/// The output is ordinary nested C#: namespaces containing types containing members, indented, with
/// bodies elided. That shape is the point — a reviewer reads a diff of it the same way they read
/// code, and can tell at a glance whether a change is additive.
/// </para>
/// <para>
/// Every signature comes from Roslyn's symbol display, so the renderer inherits the compiler's
/// understanding of the language rather than reimplementing it. What is composed here is only the
/// part Roslyn has no opinion on: the nesting, the ordering, the modifier prefix, and which members
/// belong in the surface at all.
/// </para>
/// </remarks>
internal static class ApiSurfaceRenderer
{
    /// <summary>One level of indentation.</summary>
    private const string Indent = "    ";

    /// <summary>Renders the compilation's public API surface.</summary>
    /// <param name="compilation">The compilation.</param>
    /// <param name="options">The render options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The rendered surface.</returns>
    internal static RenderedApiSurface Render(
        Compilation compilation,
        ApiRenderOptions options,
        CancellationToken cancellationToken)
    {
        var writer = new SurfaceWriter();

        if (options.IncludeAssemblyAttributes)
        {
            ApiAttributeRenderer.Append(
                writer.Builder,
                compilation.Assembly.GetAttributes(),
                string.Empty,
                "assembly: ",
                options,
                writer.AssemblyAttribute);
        }

        var namespaces = new List<INamespaceSymbol>();
        CollectNamespaces(compilation.Assembly.GlobalNamespace, namespaces, options, cancellationToken);
        namespaces.Sort(static (a, b) => string.CompareOrdinal(QualifiedName(a), QualifiedName(b)));

        var fileScoped = UsesFileScopedNamespace(namespaces, options);

        foreach (var namespaceSymbol in namespaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderNamespace(writer, namespaceSymbol, options, fileScoped, cancellationToken);
        }

        return writer.Complete();
    }

    /// <summary>Appends a single member's declaration.</summary>
    /// <param name="builder">The builder the surface is being written into.</param>
    /// <param name="member">The member.</param>
    internal static void AppendMember(PooledStringBuilder builder, ISymbol member)
    {
        ApiModifiers.AppendMember(builder, member);

        switch (member)
        {
            case IFieldSymbol field:
            {
                _ = builder.Append(field.Type.ToDisplayString(ApiDisplayFormats.TypeReference))
                    .Append(' ').Append(ApiLiterals.Identifier(field.Name));
                if (field.IsConst)
                {
                    _ = builder.Append(" = ").Append(ApiLiterals.FormatConstant(field.ConstantValue));
                }

                _ = builder.Append(';');
                break;
            }

            case IPropertySymbol property:
            {
                _ = builder.Append(property.Type.ToDisplayString(ApiDisplayFormats.TypeReference)).Append(' ');
                AppendPropertyName(builder, property);
                AppendAccessors(builder, property);
                break;
            }

            case IEventSymbol evt:
            {
                _ = builder.Append("event ").Append(evt.Type.ToDisplayString(ApiDisplayFormats.TypeReference))
                    .Append(' ').Append(ApiLiterals.Identifier(evt.Name)).Append(';');
                break;
            }

            case IMethodSymbol method:
            {
                AppendMethod(builder, method);
                _ = builder.Append(" { }");
                break;
            }

            default:
            {
                _ = builder.Append(member.ToDisplayString(ApiDisplayFormats.MemberSignature));
                break;
            }
        }
    }

    /// <summary>Appends a delegate as its single declaration line.</summary>
    /// <param name="builder">The builder the surface is being written into.</param>
    /// <param name="type">The delegate type.</param>
    internal static void AppendDelegate(PooledStringBuilder builder, INamedTypeSymbol type)
    {
        // AppendType already ends with the type keyword and a trailing space.
        ApiModifiers.AppendType(builder, type);

        var invoke = type.DelegateInvokeMethod;
        if (invoke is null)
        {
            _ = builder.Append(type.ToDisplayString(ApiDisplayFormats.TypeDeclarationName)).Append(';');
            return;
        }

        _ = builder
            .Append(invoke.ReturnsVoid ? "void" : invoke.ReturnType.ToDisplayString(ApiDisplayFormats.TypeReference))
            .Append(' ')
            .Append(type.ToDisplayString(ApiDisplayFormats.TypeDeclarationName))
            .Append('(');
        AppendParameters(builder, invoke.Parameters);
        _ = builder.Append(')');
        ApiConstraints.Append(builder, type.TypeParameters);
        _ = builder.Append(';');
    }

    /// <summary>Appends a type's declaration header: modifiers, name, base list and constraints.</summary>
    /// <param name="builder">The builder the surface is being written into.</param>
    /// <param name="type">The type.</param>
    internal static void AppendTypeHeader(PooledStringBuilder builder, INamedTypeSymbol type)
    {
        ApiModifiers.AppendType(builder, type);
        _ = builder.Append(type.ToDisplayString(ApiDisplayFormats.TypeDeclarationName));

        if (type.TypeKind == TypeKind.Enum)
        {
            // The underlying type is only written when it is not the default.
            if (type.EnumUnderlyingType is { SpecialType: not SpecialType.System_Int32 } underlying)
            {
                _ = builder.Append(" : ").Append(underlying.ToDisplayString(ApiDisplayFormats.TypeReference));
            }

            return;
        }

        AppendBaseList(builder, type);
        ApiConstraints.Append(builder, type.TypeParameters);
    }

    /// <summary>Renders one namespace and the types it declares.</summary>
    /// <param name="writer">The surface writer.</param>
    /// <param name="namespaceSymbol">The namespace.</param>
    /// <param name="options">The render options.</param>
    /// <param name="fileScoped">Whether the surface uses a file-scoped namespace declaration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    internal static void RenderNamespace(
        SurfaceWriter writer,
        INamespaceSymbol namespaceSymbol,
        ApiRenderOptions options,
        bool fileScoped,
        CancellationToken cancellationToken)
    {
        var types = VisibleTypes(namespaceSymbol, options);
        if (types.Count == 0)
        {
            return;
        }

        if (namespaceSymbol.IsGlobalNamespace)
        {
            RenderTypes(writer, types, string.Empty, options, cancellationToken);
            return;
        }

        if (fileScoped)
        {
            writer.Line(string.Empty, $"namespace {DeclaredName(namespaceSymbol)};", null);
            writer.Line(string.Empty, string.Empty, null);
            RenderTypes(writer, types, string.Empty, options, cancellationToken);
            return;
        }

        writer.Line(string.Empty, $"namespace {DeclaredName(namespaceSymbol)}", null);
        writer.Line(string.Empty, "{", null);
        RenderTypes(writer, types, Indent, options, cancellationToken);
        writer.Line(string.Empty, "}", null);
    }

    /// <summary>Decides whether the surface can use a file-scoped namespace declaration.</summary>
    /// <param name="namespaces">Every namespace the assembly declares, including the global one.</param>
    /// <param name="options">The render options.</param>
    /// <returns><see langword="true"/> when exactly one namespace holds types and none sit at global scope.</returns>
    /// <remarks>
    /// C# permits one file-scoped namespace per file, and it may not be mixed with a block-scoped one
    /// or preceded by a type at global scope. So the modern flatter form is used whenever the
    /// assembly's surface allows it, and the block form is the fallback rather than the default —
    /// an assembly that later grows a second namespace reformats its baseline once, which is a real
    /// API change being recorded, not churn.
    /// </remarks>
    internal static bool UsesFileScopedNamespace(List<INamespaceSymbol> namespaces, ApiRenderOptions options)
    {
        var withTypes = 0;
        foreach (var namespaceSymbol in namespaces)
        {
            if (VisibleTypes(namespaceSymbol, options).Count == 0)
            {
                continue;
            }

            if (namespaceSymbol.IsGlobalNamespace)
            {
                return false;
            }

            withTypes++;
        }

        return withTypes == 1;
    }

    /// <summary>Collects every namespace the assembly declares, skipping excluded ones.</summary>
    /// <param name="namespaceSymbol">The namespace to walk.</param>
    /// <param name="into">The list to add to.</param>
    /// <param name="options">The render options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    internal static void CollectNamespaces(
        INamespaceSymbol namespaceSymbol,
        List<INamespaceSymbol> into,
        ApiRenderOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!namespaceSymbol.IsGlobalNamespace && options.IsNamespaceExcluded(QualifiedName(namespaceSymbol)))
        {
            return;
        }

        into.Add(namespaceSymbol);

        foreach (var member in namespaceSymbol.GetNamespaceMembers())
        {
            CollectNamespaces(member, into, options, cancellationToken);
        }
    }

    /// <summary>Gets the externally visible types a container declares, in a stable order.</summary>
    /// <param name="container">The namespace or type.</param>
    /// <param name="options">The render options.</param>
    /// <returns>The types.</returns>
    internal static List<INamedTypeSymbol> VisibleTypes(INamespaceOrTypeSymbol container, ApiRenderOptions options)
    {
        var declared = container.GetTypeMembers();
        var types = new List<INamedTypeSymbol>(declared.Length);
        foreach (var member in declared)
        {
            if (!ApiSymbolFilter.IsExternallyVisible(member))
            {
                continue;
            }

            if (!options.IncludeGeneratedCode && ApiSymbolFilter.IsGeneratedCode(member))
            {
                continue;
            }

            // An extension container whose syntax this host cannot parse must not reach the
            // baseline: its name is compiler-generated and unspeakable, so the file would not read
            // back. See RoslynFeatures for which slot gains which half of the feature.
            if (RoslynFeatures.IsExtensionContainer(member) && !RoslynFeatures.SupportsExtensionBlocks)
            {
                continue;
            }

            types.Add(member);
        }

        types.Sort(static (a, b) =>
        {
            var result = string.CompareOrdinal(TypeSortKey(a), TypeSortKey(b));
            return result != 0 ? result : a.Arity.CompareTo(b.Arity);
        });

        return types;
    }

    /// <summary>Renders one type and everything it declares.</summary>
    /// <param name="writer">The surface writer.</param>
    /// <param name="type">The type.</param>
    /// <param name="indent">The indentation the declaration starts at.</param>
    /// <param name="options">The render options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    internal static void RenderType(
        SurfaceWriter writer,
        INamedTypeSymbol type,
        string indent,
        ApiRenderOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        writer.Pending = type;
        ApiAttributeRenderer.Append(writer.Builder, type.GetAttributes(), indent, string.Empty, options, writer.CountLineCallback);

        if (type.TypeKind == TypeKind.Delegate)
        {
            writer.BeginLine(indent);
            AppendDelegate(writer.Builder, type);
            writer.EndLine(type);
            return;
        }

        writer.BeginLine(indent);
        if (RoslynFeatures.IsExtensionContainer(type))
        {
            AppendExtensionHeader(writer.Builder, type);
        }
        else
        {
            AppendTypeHeader(writer.Builder, type);
        }

        writer.EndLine(type);
        writer.Line(indent, "{", null);

        var memberIndent = indent + Indent;
        if (type.TypeKind == TypeKind.Enum)
        {
            RenderEnumMembers(writer, type, memberIndent, options);
        }
        else
        {
            RenderMembers(writer, type, memberIndent, options, cancellationToken);
        }

        writer.Line(indent, "}", null);
    }

    /// <summary>Renders a type's members and nested types.</summary>
    /// <param name="writer">The surface writer.</param>
    /// <param name="type">The type.</param>
    /// <param name="indent">The indentation members start at.</param>
    /// <param name="options">The render options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    internal static void RenderMembers(
        SurfaceWriter writer,
        INamedTypeSymbol type,
        string indent,
        ApiRenderOptions options,
        CancellationToken cancellationToken)
    {
        var declared = type.GetMembers();
        var members = new List<ISymbol>(declared.Length);
        foreach (var member in declared)
        {
            // Nested types come from GetTypeMembers, after the members.
            if (member is not INamedTypeSymbol
                && ApiSymbolFilter.IsRenderableMember(member)
                && ApiSymbolFilter.IsExternallyVisible(member)
                && (options.IncludeGeneratedCode || !ApiSymbolFilter.IsGeneratedCode(member)))
            {
                members.Add(member);
            }
        }

        members.Sort(ApiMemberOrder.Comparison);

        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.Pending = member;
            ApiAttributeRenderer.Append(writer.Builder, member.GetAttributes(), indent, string.Empty, options, writer.CountLineCallback);
            writer.BeginLine(indent);
            AppendMember(writer.Builder, member);
            writer.EndLine(member);
        }

        foreach (var nested in VisibleTypes(type, options))
        {
            RenderType(writer, nested, indent, options, cancellationToken);
        }
    }

    /// <summary>Gets the key a type orders under within its container.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The sort key.</returns>
    internal static string TypeSortKey(INamedTypeSymbol type)
    {
        if (!RoslynFeatures.IsExtensionContainer(type))
        {
            return type.Name;
        }

        // An extension container has no usable name, so it orders by the header that describes it.
        // The receiver alone is not enough: several blocks can extend one receiver and differ only
        // in what they constrain it to, and a tie between those would be broken by declaration
        // order — making a block moved down a file read as an API change.
        var header = new PooledStringBuilder();
        AppendExtensionHeader(header, type);
        return header.ToString();
    }

    /// <summary>Appends the header of a C# 14 extension block.</summary>
    /// <param name="builder">The builder the surface is being written into.</param>
    /// <param name="type">The extension container.</param>
    /// <remarks>
    /// The type parameter list is written even though a block has no name to attach it to. A generic
    /// block's receiver is spelled in terms of those parameters, so a header without the list names
    /// something nothing declares — text C# cannot read back as the surface it was rendered from.
    /// </remarks>
    internal static void AppendExtensionHeader(PooledStringBuilder builder, INamedTypeSymbol type)
    {
        _ = builder.Append("extension");
        AppendTypeParameters(builder, type.TypeParameters);
        _ = builder.Append('(');

        if (RoslynFeatures.ExtensionReceiver(type) is { } receiver)
        {
            AppendNormalizedDefault(builder, receiver.ToDisplayString(ApiDisplayFormats.Parameter));
        }

        _ = builder.Append(')');
        ApiConstraints.Append(builder, type.TypeParameters);
    }

    /// <summary>Appends a type's base type and directly implemented interfaces.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="type">The type.</param>
    internal static void AppendBaseList(PooledStringBuilder builder, INamedTypeSymbol type)
    {
        // The base type is written straight out: there is at most one and it always leads. Only the
        // interfaces need collecting, because their order in source carries no meaning and sorting
        // is what keeps the baseline from churning when the list is rearranged. Collecting both into
        // one list and copying cost a second list and its growth array for every type rendered.
        var baseType = type.TypeKind == TypeKind.Class
            && type.BaseType is { SpecialType: not SpecialType.System_Object } declared
            ? declared.ToDisplayString(ApiDisplayFormats.TypeReference)
            : null;

        var interfaces = VisibleInterfaces(type);
        if (baseType is null && interfaces is null)
        {
            return;
        }

        _ = builder.Append(" : ");

        if (baseType is not null)
        {
            _ = builder.Append(baseType);
        }

        AppendInterfaces(builder, interfaces, baseType is not null);
    }

    /// <summary>Appends a property's name, or an indexer's parameter list.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="property">The property.</param>
    internal static void AppendPropertyName(PooledStringBuilder builder, IPropertySymbol property)
    {
        if (!property.IsIndexer)
        {
            _ = builder.Append(ApiLiterals.Identifier(property.Name));
            return;
        }

        _ = builder.Append("this[");
        AppendParameters(builder, property.Parameters);
        _ = builder.Append(']');
    }

    /// <summary>Appends a property's accessor list.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="property">The property.</param>
    internal static void AppendAccessors(PooledStringBuilder builder, IPropertySymbol property)
    {
        _ = builder.Append(" { ");

        if (property.GetMethod is { } getter && ApiSymbolFilter.IsExternallyVisible(getter))
        {
            AppendAccessorAccessibility(builder, getter, property);
            _ = builder.Append("get; ");
        }

        if (property.SetMethod is { } setter && ApiSymbolFilter.IsExternallyVisible(setter))
        {
            AppendAccessorAccessibility(builder, setter, property);
            _ = builder.Append(setter.IsInitOnly ? "init; " : "set; ");
        }

        _ = builder.Append('}');
    }

    /// <summary>
    /// Writes an accessor's accessibility only when it is narrower than the property's, which is
    /// the only case C# lets you write and the only case a consumer sees a difference.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="accessor">The accessor.</param>
    /// <param name="property">The property that declares it.</param>
    internal static void AppendAccessorAccessibility(
        PooledStringBuilder builder,
        IMethodSymbol accessor,
        IPropertySymbol property)
    {
        if (accessor.DeclaredAccessibility != Accessibility.Protected
            || accessor.DeclaredAccessibility == property.DeclaredAccessibility)
        {
            return;
        }

        _ = builder.Append("protected ");
    }

    /// <summary>Appends a method's return type, name, parameters and constraints.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="method">The method.</param>
    internal static void AppendMethod(PooledStringBuilder builder, IMethodSymbol method)
    {
        AppendMethodName(builder, method);
        _ = builder.Append('(');
        AppendParameters(builder, method.Parameters, method.IsExtensionMethod);
        _ = builder.Append(')');

        // Constructors and operators cannot carry constraints of their own.
        if (method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor
            or MethodKind.Conversion or MethodKind.UserDefinedOperator)
        {
            return;
        }

        ApiConstraints.Append(builder, method.TypeParameters);
    }

    /// <summary>Appends the <c>checked</c> keyword when the operator is the checked form.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="method">The operator.</param>
    /// <remarks>C# writes the keyword after <c>operator</c>, before the token or the target type.</remarks>
    internal static void AppendCheckedKeyword(PooledStringBuilder builder, IMethodSymbol method)
    {
        if (!ApiLiterals.IsCheckedOperator(method.Name))
        {
            return;
        }

        _ = builder.Append("checked ");
    }

    /// <summary>
    /// Rewrites a spelled-out default expression to the short form. Roslyn writes the type out in
    /// full; the short form is what the source says and what reads naturally.
    /// </summary>
    /// <param name="builder">The builder the surface is being written into.</param>
    /// <param name="parameter">The rendered parameter.</param>
    internal static void AppendNormalizedDefault(PooledStringBuilder builder, string parameter)
    {
        const string Marker = " = default(";
        var index = parameter.IndexOf(Marker, StringComparison.Ordinal);
        if (index >= 0 && parameter.EndsWith(")", StringComparison.Ordinal))
        {
            _ = builder.Append(parameter, index).Append(" = default");
            return;
        }

        _ = builder.Append(parameter);
    }

    /// <summary>Appends a type parameter list, if there is one.</summary>
    /// <param name="builder">The builder the surface is being written into.</param>
    /// <param name="typeParameters">The type parameters.</param>
    /// <remarks>
    /// Only the names are written; the constraints follow the rest of the declaration, which is
    /// where C# puts them.
    /// </remarks>
    internal static void AppendTypeParameters(PooledStringBuilder builder, ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        if (typeParameters.IsEmpty)
        {
            return;
        }

        _ = builder.Append('<');
        for (var i = 0; i < typeParameters.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            _ = builder.Append(ApiLiterals.Identifier(typeParameters[i].Name));
        }

        _ = builder.Append('>');
    }

    /// <summary>Renders a run of types at one indentation level.</summary>
    /// <param name="writer">The surface writer.</param>
    /// <param name="types">The types.</param>
    /// <param name="indent">The indentation.</param>
    /// <param name="options">The render options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private static void RenderTypes(
        SurfaceWriter writer,
        List<INamedTypeSymbol> types,
        string indent,
        ApiRenderOptions options,
        CancellationToken cancellationToken)
    {
        foreach (var type in types)
        {
            RenderType(writer, type, indent, options, cancellationToken);
        }
    }

    /// <summary>Renders an enum's members.</summary>
    /// <param name="writer">The surface writer.</param>
    /// <param name="type">The enum.</param>
    /// <param name="indent">The indentation members start at.</param>
    /// <param name="options">The render options.</param>
    private static void RenderEnumMembers(
        SurfaceWriter writer,
        INamedTypeSymbol type,
        string indent,
        ApiRenderOptions options)
    {
        // Enum members keep their declared order: the values are what matter, and reordering them
        // alphabetically would make the baseline read nothing like the source it describes.
        foreach (var member in type.GetMembers())
        {
            if (member is not IFieldSymbol { IsConst: true } field || !ApiSymbolFilter.IsExternallyVisible(field))
            {
                continue;
            }

            if (!options.IncludeGeneratedCode && ApiSymbolFilter.IsGeneratedCode(field))
            {
                continue;
            }

            writer.Pending = field;
            ApiAttributeRenderer.Append(writer.Builder, field.GetAttributes(), indent, string.Empty, options, writer.CountLineCallback);
            writer.BeginLine(indent);
            _ = writer.Builder.Append(ApiLiterals.Identifier(field.Name)).Append(" = ").Append(ApiLiterals.FormatConstant(field.ConstantValue)).Append(',');
            writer.EndLine(field);
        }
    }

    /// <summary>Appends the part of a method declaration that precedes its parameter list.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="method">The method.</param>
    private static void AppendMethodName(PooledStringBuilder builder, IMethodSymbol method)
    {
        switch (method.MethodKind)
        {
            case MethodKind.Constructor or MethodKind.StaticConstructor:
            {
                _ = builder.Append(ApiLiterals.Identifier(method.ContainingType.Name));
                break;
            }

            case MethodKind.Conversion:
            {
                _ = builder.Append(method.Name is "op_Implicit" ? "implicit operator " : "explicit operator ");
                AppendCheckedKeyword(builder, method);
                _ = builder.Append(method.ReturnType.ToDisplayString(ApiDisplayFormats.TypeReference));
                break;
            }

            case MethodKind.UserDefinedOperator:
            {
                _ = builder.Append(method.ReturnType.ToDisplayString(ApiDisplayFormats.TypeReference))
                    .Append(" operator ");
                AppendCheckedKeyword(builder, method);
                _ = builder.Append(ApiLiterals.OperatorToken(method.Name));
                break;
            }

            default:
            {
                _ = builder
                    .Append(method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(ApiDisplayFormats.TypeReference))
                    .Append(' ').Append(ApiLiterals.Identifier(method.Name));
                AppendTypeParameters(builder, method.TypeParameters);
                break;
            }
        }
    }

    /// <summary>Appends a comma-separated parameter list.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="isExtensionMethod">Whether the parameters belong to an extension method.</param>
    /// <remarks>
    /// The receiver's <c>this</c> is written here rather than left to symbol display, which only
    /// emits it when asked for a whole method. Rendering each parameter on its own loses that
    /// context, and dropping the keyword would turn an extension method into what looks like an
    /// ordinary static one — a different way of calling it, so a different API.
    /// </remarks>
    private static void AppendParameters(
        PooledStringBuilder builder,
        ImmutableArray<IParameterSymbol> parameters,
        bool isExtensionMethod = false)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            if (i == 0 && isExtensionMethod)
            {
                _ = builder.Append("this ");
            }

            AppendNormalizedDefault(builder, parameters[i].ToDisplayString(ApiDisplayFormats.Parameter));
        }
    }

    /// <summary>Gets a namespace's name as its declaration writes it.</summary>
    /// <param name="namespaceSymbol">The namespace.</param>
    /// <returns>The name, with any keyword segment escaped.</returns>
    private static string DeclaredName(INamespaceSymbol namespaceSymbol) =>
        namespaceSymbol.ToDisplayString(ApiDisplayFormats.NamespaceDeclarationName);

    /// <summary>Gets the fully qualified name of a namespace.</summary>
    /// <param name="namespaceSymbol">The namespace.</param>
    /// <returns>The name.</returns>
    private static string QualifiedName(INamespaceSymbol namespaceSymbol) =>
        namespaceSymbol.ToDisplayString(ApiDisplayFormats.QualifiedName);

    /// <summary>Collects the interfaces a type implements that a consumer can name.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The rendered interface names, or <see langword="null"/> when there are none.</returns>
    /// <remarks>Staying null for a type that implements nothing keeps a list off the heap entirely.</remarks>
    private static List<string>? VisibleInterfaces(INamedTypeSymbol type)
    {
        List<string>? interfaces = null;
        foreach (var implemented in type.Interfaces)
        {
            if (ApiSymbolFilter.IsExternallyVisible(implemented))
            {
                interfaces ??= new List<string>(type.Interfaces.Length);
                interfaces.Add(implemented.ToDisplayString(ApiDisplayFormats.TypeReference));
            }
        }

        return interfaces;
    }

    /// <summary>Appends a type's interfaces, sorted, after whatever already leads the base list.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="interfaces">The interfaces, or <see langword="null"/>.</param>
    /// <param name="afterBaseType">Whether a base type has already been written.</param>
    private static void AppendInterfaces(PooledStringBuilder builder, List<string>? interfaces, bool afterBaseType)
    {
        if (interfaces is null)
        {
            return;
        }

        interfaces.Sort(StringComparer.Ordinal);
        for (var i = 0; i < interfaces.Count; i++)
        {
            if (afterBaseType || i > 0)
            {
                _ = builder.Append(", ");
            }

            _ = builder.Append(interfaces[i]);
        }
    }

    /// <summary>Accumulates the surface text, the symbol behind each line, and the declarations.</summary>
    /// <remarks>
    /// The declarations are collected as the text is written rather than parsed back out of it
    /// afterwards. Each one is a span of the document — its attribute lines and its own line — paired
    /// with the identity derived from its symbol. What a declaration's text comes to is the same
    /// either way: the lines it occupies, each trimmed of the indentation its nesting gave it.
    /// </remarks>
    internal sealed class SurfaceWriter
    {
        /// <summary>The initial buffer a surface document is built in.</summary>
        private const int DocumentCapacity = 4096;

        /// <summary>The initial number of lines a surface document is sized for.</summary>
        private const int LineCapacity = 256;

        /// <summary>The symbol each emitted line belongs to, indexed by line number.</summary>
        private readonly List<ISymbol?> _symbolsByLine = new(LineCapacity);

        /// <summary>Where each declaration sits in the document, and what produced it.</summary>
        private readonly List<RenderedApiSurface.Written> _declarations = new(LineCapacity);

        /// <summary>The symbol of the declaration being written, if one is open.</summary>
        private ISymbol? _openSymbol;

        /// <summary>Where in the document the open declaration started.</summary>
        private int _openStart;

        /// <summary>The line the open declaration started on.</summary>
        private int _openLine;

        /// <summary>The symbol the next emitted line belongs to.</summary>
        private ISymbol? _pending;

        /// <summary>Initializes a new instance of the <see cref="SurfaceWriter"/> class.</summary>
        internal SurfaceWriter() => CountLineCallback = CountLine;

        /// <summary>Gets the text builder.</summary>
        internal PooledStringBuilder Builder { get; } = new(DocumentCapacity);

        /// <summary>
        /// Gets <see cref="CountLine"/> as a delegate, created once. The attribute renderer takes the
        /// callback per call, and a method group conversion there allocates on every member.
        /// </summary>
        internal Action<string> CountLineCallback { get; }

        /// <summary>
        /// Gets or sets the symbol the next emitted line belongs to. A declaration's first line is
        /// its first attribute, matching where the parser reports the declaration as starting.
        /// </summary>
        internal ISymbol? Pending
        {
            get => _pending;
            set
            {
                _pending = value;

                if (value is null)
                {
                    return;
                }

                _openSymbol = value;
                _openStart = Builder.Length;
                _openLine = _symbolsByLine.Count;
            }
        }

        /// <summary>
        /// Starts a line the caller writes into <see cref="Builder"/> directly, rather than handing
        /// over text it has already built. Must be paired with <see cref="EndLine"/>.
        /// </summary>
        /// <param name="indent">The indentation.</param>
        internal void BeginLine(string indent) => _ = Builder.Append(indent);

        /// <summary>Ends the line started by <see cref="BeginLine"/>, closing the open declaration.</summary>
        /// <param name="symbol">The symbol the line declares, when it is the declaration's first line.</param>
        internal void EndLine(ISymbol? symbol)
        {
            CloseDeclaration(symbol);
            _ = Builder.Append('\n');
            _symbolsByLine.Add(_pending ?? symbol);
            _pending = null;
        }

        /// <summary>Writes one indented line.</summary>
        /// <param name="indent">The indentation.</param>
        /// <param name="content">The line content.</param>
        /// <param name="symbol">The symbol the line declares, when it is the declaration's first line.</param>
        internal void Line(string indent, string content, ISymbol? symbol)
        {
            _ = Builder.Append(indent).Append(content);
            CloseDeclaration(symbol);
            _ = Builder.Append('\n');
            _symbolsByLine.Add(_pending ?? symbol);
            _pending = null;
        }

        /// <summary>Records one assembly-level attribute, which stands alone rather than in a declaration.</summary>
        /// <param name="rendered">The attribute as it was written, without its brackets.</param>
        internal void AssemblyAttribute(string rendered)
        {
            var start = Builder.Length;
            _declarations.Add(new(null, rendered, start, start, _symbolsByLine.Count));
            CountLine(rendered);
        }

        /// <summary>Finishes the rendering.</summary>
        /// <returns>The rendered surface.</returns>
        internal RenderedApiSurface Complete() =>
            new(Builder.ToString(), _symbolsByLine.ToArray(), _declarations.ToImmutableArray());

        /// <summary>Records that a line was written directly to <see cref="Builder"/>.</summary>
        /// <param name="rendered">The attribute the line carries, unused for a declaration's own.</param>
        internal void CountLine(string rendered)
        {
            _ = rendered;
            _symbolsByLine.Add(_pending);
            _pending = null;
        }

        /// <summary>Closes the declaration being written, if the line just finished is its own.</summary>
        /// <param name="symbol">The symbol the line declares.</param>
        private void CloseDeclaration(ISymbol? symbol)
        {
            if (_openSymbol is null || (_pending is null && symbol is null))
            {
                return;
            }

            _declarations.Add(new(_openSymbol, null, _openStart, Builder.Length, _openLine));
            _openSymbol = null;
        }
    }
}
