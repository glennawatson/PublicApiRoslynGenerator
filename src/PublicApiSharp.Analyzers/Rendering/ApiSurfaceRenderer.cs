// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
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
                writer.CountLine);
        }

        var namespaces = new List<INamespaceSymbol>();
        CollectNamespaces(compilation.Assembly.GlobalNamespace, namespaces, options, cancellationToken);
        namespaces.Sort(static (a, b) => string.CompareOrdinal(QualifiedName(a), QualifiedName(b)));

        var fileScoped = UsesFileScopedNamespace(namespaces);

        foreach (var namespaceSymbol in namespaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderNamespace(writer, namespaceSymbol, options, fileScoped, cancellationToken);
        }

        return writer.Complete();
    }

    /// <summary>Renders a type's declaration header: modifiers, name, base list and constraints.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The header text.</returns>
    internal static string RenderTypeHeader(INamedTypeSymbol type)
    {
        var builder = new PooledStringBuilder();
        _ = builder.Append(ApiModifiers.ForType(type)).Append(type.ToDisplayString(ApiDisplayFormats.TypeDeclarationName));

        if (type.TypeKind == TypeKind.Enum)
        {
            // The underlying type is only written when it is not the default.
            if (type.EnumUnderlyingType is { SpecialType: not SpecialType.System_Int32 } underlying)
            {
                _ = builder.Append(" : ").Append(underlying.ToDisplayString(ApiDisplayFormats.TypeReference));
            }

            return builder.ToString();
        }

        AppendBaseList(builder, type);
        return builder.Append(ApiConstraints.Render(type.TypeParameters)).ToString();
    }

    /// <summary>Renders a single member's declaration.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The declaration text.</returns>
    internal static string RenderMember(ISymbol member)
    {
        var builder = new PooledStringBuilder();
        _ = builder.Append(ApiModifiers.ForMember(member));

        switch (member)
        {
            case IFieldSymbol field:
            {
                _ = builder.Append(field.Type.ToDisplayString(ApiDisplayFormats.TypeReference))
                    .Append(' ').Append(field.Name);
                if (field.IsConst)
                {
                    _ = builder.Append(" = ").Append(FormatConstant(field.ConstantValue));
                }

                return builder.Append(';').ToString();
            }

            case IPropertySymbol property:
            {
                _ = builder.Append(property.Type.ToDisplayString(ApiDisplayFormats.TypeReference)).Append(' ');
                AppendPropertyName(builder, property);
                AppendAccessors(builder, property);
                return builder.ToString();
            }

            case IEventSymbol evt:
            {
                _ = builder.Append("event ").Append(evt.Type.ToDisplayString(ApiDisplayFormats.TypeReference))
                    .Append(' ').Append(evt.Name);
                return builder.Append(';').ToString();
            }

            case IMethodSymbol method:
            {
                AppendMethod(builder, method);
                return builder.Append(" { }").ToString();
            }

            default:
            {
                return builder.Append(member.ToDisplayString(ApiDisplayFormats.MemberSignature)).ToString();
            }
        }
    }

    /// <summary>Renders one namespace and the types it declares.</summary>
    /// <param name="writer">The surface writer.</param>
    /// <param name="namespaceSymbol">The namespace.</param>
    /// <param name="options">The render options.</param>
    /// <param name="fileScoped">Whether the surface uses a file-scoped namespace declaration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private static void RenderNamespace(
        SurfaceWriter writer,
        INamespaceSymbol namespaceSymbol,
        ApiRenderOptions options,
        bool fileScoped,
        CancellationToken cancellationToken)
    {
        var types = VisibleTypes(namespaceSymbol);
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
            writer.Line(string.Empty, $"namespace {QualifiedName(namespaceSymbol)};", null);
            writer.Line(string.Empty, string.Empty, null);
            RenderTypes(writer, types, string.Empty, options, cancellationToken);
            return;
        }

        writer.Line(string.Empty, $"namespace {QualifiedName(namespaceSymbol)}", null);
        writer.Line(string.Empty, "{", null);
        RenderTypes(writer, types, Indent, options, cancellationToken);
        writer.Line(string.Empty, "}", null);
    }

    /// <summary>Decides whether the surface can use a file-scoped namespace declaration.</summary>
    /// <param name="namespaces">Every namespace the assembly declares, including the global one.</param>
    /// <returns><see langword="true"/> when exactly one namespace holds types and none sit at global scope.</returns>
    /// <remarks>
    /// C# permits one file-scoped namespace per file, and it may not be mixed with a block-scoped one
    /// or preceded by a type at global scope. So the modern flatter form is used whenever the
    /// assembly's surface allows it, and the block form is the fallback rather than the default —
    /// an assembly that later grows a second namespace reformats its baseline once, which is a real
    /// API change being recorded, not churn.
    /// </remarks>
    private static bool UsesFileScopedNamespace(List<INamespaceSymbol> namespaces)
    {
        var withTypes = 0;
        foreach (var namespaceSymbol in namespaces)
        {
            if (VisibleTypes(namespaceSymbol).Count == 0)
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

    /// <summary>Collects every namespace the assembly declares, skipping excluded ones.</summary>
    /// <param name="namespaceSymbol">The namespace to walk.</param>
    /// <param name="into">The list to add to.</param>
    /// <param name="options">The render options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private static void CollectNamespaces(
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
    /// <returns>The types.</returns>
    private static List<INamedTypeSymbol> VisibleTypes(INamespaceOrTypeSymbol container)
    {
        var types = new List<INamedTypeSymbol>();
        foreach (var member in container.GetTypeMembers())
        {
            if (!ApiSymbolFilter.IsExternallyVisible(member))
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
    private static void RenderType(
        SurfaceWriter writer,
        INamedTypeSymbol type,
        string indent,
        ApiRenderOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        writer.Pending = type;
        ApiAttributeRenderer.Append(writer.Builder, type.GetAttributes(), indent, string.Empty, options, writer.CountLine);

        if (type.TypeKind == TypeKind.Delegate)
        {
            writer.Line(indent, RenderDelegate(type), type);
            return;
        }

        var header = RoslynFeatures.IsExtensionContainer(type) ? RenderExtensionHeader(type) : RenderTypeHeader(type);
        writer.Line(indent, header, type);
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

            writer.Pending = field;
            ApiAttributeRenderer.Append(writer.Builder, field.GetAttributes(), indent, string.Empty, options, writer.CountLine);
            writer.Line(indent, $"{field.Name} = {FormatConstant(field.ConstantValue)},", field);
        }
    }

    /// <summary>Renders a type's members and nested types.</summary>
    /// <param name="writer">The surface writer.</param>
    /// <param name="type">The type.</param>
    /// <param name="indent">The indentation members start at.</param>
    /// <param name="options">The render options.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private static void RenderMembers(
        SurfaceWriter writer,
        INamedTypeSymbol type,
        string indent,
        ApiRenderOptions options,
        CancellationToken cancellationToken)
    {
        var members = new List<ISymbol>();
        foreach (var member in type.GetMembers())
        {
            // Nested types come from GetTypeMembers, after the members.
            if (member is not INamedTypeSymbol
                && ApiSymbolFilter.IsRenderableMember(member)
                && ApiSymbolFilter.IsExternallyVisible(member))
            {
                members.Add(member);
            }
        }

        members.Sort(ApiMemberOrder.Instance);

        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.Pending = member;
            ApiAttributeRenderer.Append(writer.Builder, member.GetAttributes(), indent, string.Empty, options, writer.CountLine);
            writer.Line(indent, RenderMember(member), member);
        }

        foreach (var nested in VisibleTypes(type))
        {
            RenderType(writer, nested, indent, options, cancellationToken);
        }
    }

    /// <summary>Gets the key a type orders under within its container.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The sort key.</returns>
    private static string TypeSortKey(INamedTypeSymbol type)
    {
        if (!RoslynFeatures.IsExtensionContainer(type))
        {
            return type.Name;
        }

        // An extension container has no usable name, so it orders by the receiver it extends.
        var receiver = RoslynFeatures.ExtensionReceiver(type);
        return receiver is null ? string.Empty : receiver.Type.ToDisplayString(ApiDisplayFormats.TypeReference);
    }

    /// <summary>Renders the header of a C# 14 extension block.</summary>
    /// <param name="type">The extension container.</param>
    /// <returns>The header text.</returns>
    private static string RenderExtensionHeader(INamedTypeSymbol type)
    {
        var builder = new PooledStringBuilder();
        _ = builder.Append("extension(");

        if (RoslynFeatures.ExtensionReceiver(type) is { } receiver)
        {
            _ = builder.Append(NormalizeDefault(receiver.ToDisplayString(ApiDisplayFormats.Parameter)));
        }

        return builder.Append(')').Append(ApiConstraints.Render(type.TypeParameters)).ToString();
    }

    /// <summary>Appends a type's base type and directly implemented interfaces.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="type">The type.</param>
    private static void AppendBaseList(PooledStringBuilder builder, INamedTypeSymbol type)
    {
        var bases = new List<string>();

        if (type.TypeKind == TypeKind.Class && type.BaseType is { SpecialType: not SpecialType.System_Object } baseType)
        {
            bases.Add(baseType.ToDisplayString(ApiDisplayFormats.TypeReference));
        }

        var interfaces = new List<string>();
        foreach (var implemented in type.Interfaces)
        {
            if (ApiSymbolFilter.IsExternallyVisible(implemented))
            {
                interfaces.Add(implemented.ToDisplayString(ApiDisplayFormats.TypeReference));
            }
        }

        // The order interfaces appear in source carries no meaning, so sorting keeps the baseline
        // from churning when the list is rearranged.
        interfaces.Sort(StringComparer.Ordinal);
        bases.AddRange(interfaces);

        if (bases.Count == 0)
        {
            return;
        }

        _ = builder.Append(" : ");
        for (var i = 0; i < bases.Count; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            _ = builder.Append(bases[i]);
        }
    }

    /// <summary>Appends a property's name, or an indexer's parameter list.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="property">The property.</param>
    private static void AppendPropertyName(PooledStringBuilder builder, IPropertySymbol property)
    {
        if (!property.IsIndexer)
        {
            _ = builder.Append(property.Name);
            return;
        }

        _ = builder.Append("this[");
        AppendParameters(builder, property.Parameters);
        _ = builder.Append(']');
    }

    /// <summary>Appends a property's accessor list.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="property">The property.</param>
    private static void AppendAccessors(PooledStringBuilder builder, IPropertySymbol property)
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
    private static void AppendAccessorAccessibility(
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
    private static void AppendMethod(PooledStringBuilder builder, IMethodSymbol method)
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

        _ = builder.Append(ApiConstraints.Render(method.TypeParameters));
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
                _ = builder.Append(method.ContainingType.Name);
                break;
            }

            case MethodKind.Conversion:
            {
                _ = builder.Append(method.Name is "op_Implicit" ? "implicit operator " : "explicit operator ")
                    .Append(method.ReturnType.ToDisplayString(ApiDisplayFormats.TypeReference));
                break;
            }

            case MethodKind.UserDefinedOperator:
            {
                _ = builder.Append(method.ReturnType.ToDisplayString(ApiDisplayFormats.TypeReference))
                    .Append(" operator ").Append(OperatorToken(method.Name));
                break;
            }

            default:
            {
                _ = builder
                    .Append(method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(ApiDisplayFormats.TypeReference))
                    .Append(' ').Append(method.Name).Append(TypeParameters(method));
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

            _ = builder.Append(NormalizeDefault(parameters[i].ToDisplayString(ApiDisplayFormats.Parameter)));
        }
    }

    /// <summary>
    /// Rewrites a spelled-out default expression to the short form. Roslyn writes the type out in
    /// full; the short form is what the source says and what reads naturally.
    /// </summary>
    /// <param name="parameter">The rendered parameter.</param>
    /// <returns>The normalized parameter.</returns>
    private static string NormalizeDefault(string parameter)
    {
        const string Marker = " = default(";
        var index = parameter.IndexOf(Marker, StringComparison.Ordinal);
        return index >= 0 && parameter.EndsWith(")", StringComparison.Ordinal)
            ? $"{parameter.Remove(index)} = default"
            : parameter;
    }

    /// <summary>Renders a delegate as its single declaration line.</summary>
    /// <param name="type">The delegate type.</param>
    /// <returns>The declaration text.</returns>
    private static string RenderDelegate(INamedTypeSymbol type)
    {
        // ForType already ends with the type keyword and a trailing space.
        var builder = new PooledStringBuilder();
        _ = builder.Append(ApiModifiers.ForType(type));

        var invoke = type.DelegateInvokeMethod;
        if (invoke is null)
        {
            return builder.Append(type.ToDisplayString(ApiDisplayFormats.TypeDeclarationName)).Append(';').ToString();
        }

        _ = builder
            .Append(invoke.ReturnsVoid ? "void" : invoke.ReturnType.ToDisplayString(ApiDisplayFormats.TypeReference))
            .Append(' ')
            .Append(type.ToDisplayString(ApiDisplayFormats.TypeDeclarationName))
            .Append('(');
        AppendParameters(builder, invoke.Parameters);
        return builder.Append(')').Append(ApiConstraints.Render(type.TypeParameters)).Append(';').ToString();
    }

    /// <summary>Renders a method's type parameter list.</summary>
    /// <param name="method">The method.</param>
    /// <returns>The type parameter list, or an empty string.</returns>
    private static string TypeParameters(IMethodSymbol method)
    {
        if (method.Arity == 0)
        {
            return string.Empty;
        }

        var builder = new PooledStringBuilder();
        _ = builder.Append('<');
        for (var i = 0; i < method.TypeParameters.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            _ = builder.Append(method.TypeParameters[i].Name);
        }

        return builder.Append('>').ToString();
    }

    /// <summary>Gets the fully qualified name of a namespace.</summary>
    /// <param name="namespaceSymbol">The namespace.</param>
    /// <returns>The name.</returns>
    private static string QualifiedName(INamespaceSymbol namespaceSymbol) =>
        namespaceSymbol.ToDisplayString(ApiDisplayFormats.QualifiedName);

    /// <summary>Maps an operator's metadata name back to the token it overloads.</summary>
    /// <param name="metadataName">The metadata name.</param>
    /// <returns>The operator token.</returns>
    private static string OperatorToken(string metadataName) => metadataName switch
    {
        "op_Addition" or "op_UnaryPlus" => "+",
        "op_Subtraction" or "op_UnaryNegation" => "-",
        "op_Multiply" => "*",
        "op_Division" => "/",
        "op_Modulus" => "%",
        "op_BitwiseAnd" => "&",
        "op_BitwiseOr" => "|",
        "op_ExclusiveOr" => "^",
        "op_LeftShift" => "<<",
        "op_RightShift" => ">>",
        "op_UnsignedRightShift" => ">>>",
        "op_Equality" => "==",
        "op_Inequality" => "!=",
        "op_LessThan" => "<",
        "op_GreaterThan" => ">",
        "op_LessThanOrEqual" => "<=",
        "op_GreaterThanOrEqual" => ">=",
        "op_LogicalNot" => "!",
        "op_OnesComplement" => "~",
        "op_Increment" => "++",
        "op_Decrement" => "--",
        "op_True" => "true",
        "op_False" => "false",
        _ => metadataName,
    };

    /// <summary>Formats a constant value as the C# literal a reader would write.</summary>
    /// <param name="value">The constant value.</param>
    /// <returns>The literal.</returns>
    private static string FormatConstant(object? value) => value switch
    {
        null => "null",
        string text => SymbolDisplay.FormatLiteral(text, quote: true),
        char character => SymbolDisplay.FormatLiteral(character, quote: true),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Accumulates the surface text while recording which symbol each line belongs to.</summary>
    private sealed class SurfaceWriter
    {
        /// <summary>The initial buffer a surface document is built in.</summary>
        private const int DocumentCapacity = 4096;

        /// <summary>The initial number of lines a surface document is sized for.</summary>
        private const int LineCapacity = 256;

        /// <summary>The symbol each emitted line belongs to, indexed by line number.</summary>
        private readonly List<ISymbol?> _symbolsByLine = new(LineCapacity);

        /// <summary>Gets the text builder.</summary>
        internal PooledStringBuilder Builder { get; } = new(DocumentCapacity);

        /// <summary>
        /// Gets or sets the symbol the next emitted line belongs to. A declaration's first line is
        /// its first attribute, matching where the parser reports the declaration as starting.
        /// </summary>
        internal ISymbol? Pending { get; set; }

        /// <summary>Records that a line was written directly to <see cref="Builder"/>.</summary>
        internal void CountLine()
        {
            _symbolsByLine.Add(Pending);
            Pending = null;
        }

        /// <summary>Writes one indented line.</summary>
        /// <param name="indent">The indentation.</param>
        /// <param name="content">The line content.</param>
        /// <param name="symbol">The symbol the line declares, when it is the declaration's first line.</param>
        internal void Line(string indent, string content, ISymbol? symbol)
        {
            _ = Builder.Append(indent).Append(content).Append('\n');
            _symbolsByLine.Add(Pending ?? symbol);
            Pending = null;
        }

        /// <summary>Finishes the rendering.</summary>
        /// <returns>The rendered surface.</returns>
        internal RenderedApiSurface Complete() => new(Builder.ToString(), _symbolsByLine.ToArray());
    }
}
