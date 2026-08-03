// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Globalization;

namespace PublicApiSharp.Analyzers;

/// <summary>Builds a declaration's identity from its symbol, without reading the rendered text back.</summary>
/// <remarks>
/// <para>
/// <see cref="ApiTextParser"/> derives the same identity from syntax, because the checked-in baseline
/// is text and has no symbols behind it. Deriving it here as well means the surface does not have to
/// be parsed only to be keyed — which was the single largest cost of a comparison.
/// </para>
/// <para>
/// The two derivations must agree exactly. Where they disagree a member is filed under two different
/// keys and reads as removed and added at once, so every rule below mirrors a rule in the parser and
/// <c>SymbolIdentitiesMatchParsedIdentitiesAsync</c> holds them together.
/// </para>
/// </remarks>
internal static class ApiIdentity
{
    /// <summary>The name the parser gives a constructor, which has no identifier of its own.</summary>
    private const string ConstructorName = ".ctor";

    /// <summary>Builds the identity of a declaration.</summary>
    /// <param name="symbol">The declared symbol.</param>
    /// <returns>The identity, matching what the parser derives from the rendered text.</returns>
    internal static string Of(ISymbol symbol)
    {
        var builder = new PooledStringBuilder();
        AppendContainer(builder, symbol.ContainingSymbol);
        AppendMember(builder, symbol);
        return builder.ToString();
    }

    /// <summary>Builds the identity of an assembly-level attribute application.</summary>
    /// <param name="rendered">The attribute as the surface writes it, without its brackets.</param>
    /// <returns>The identity.</returns>
    /// <remarks>
    /// An assembly attribute has no identity beyond its whole application: the same attribute type
    /// can be applied more than once with different arguments.
    /// </remarks>
    internal static string OfAssemblyAttribute(string rendered)
    {
        var builder = new PooledStringBuilder();
        _ = builder.Append("[assembly]");
        AppendWithoutWhitespace(builder, rendered);
        return builder.ToString();
    }

    /// <summary>Appends the dotted path of everything a declaration sits inside.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="container">The containing symbol.</param>
    private static void AppendContainer(PooledStringBuilder builder, ISymbol? container)
    {
        if (container is null or INamespaceSymbol { IsGlobalNamespace: true } or IAssemblySymbol or IModuleSymbol)
        {
            return;
        }

        AppendContainer(builder, container.ContainingSymbol);
        AppendSeparator(builder);

        // An extension block has no name a consumer could write — the compiler generates one — so a
        // member inside it is placed under the block's header, exactly as the parser reads it.
        if (container is INamedTypeSymbol containingType)
        {
            AppendType(builder, containingType);
            return;
        }

        _ = builder.Append(container.Name);
    }

    /// <summary>Appends the dot that joins a path to what follows it, unless nothing precedes it.</summary>
    /// <param name="builder">The builder.</param>
    private static void AppendSeparator(PooledStringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        _ = builder.Append('.');
    }

    /// <summary>Appends the part of an identity that names the declaration itself.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="symbol">The declared symbol.</param>
    private static void AppendMember(PooledStringBuilder builder, ISymbol symbol)
    {
        AppendSeparator(builder);

        switch (symbol)
        {
            case INamedTypeSymbol type:
            {
                AppendType(builder, type);
                break;
            }

            case IMethodSymbol method:
            {
                AppendMethod(builder, method);
                break;
            }

            case IPropertySymbol property:
            {
                AppendProperty(builder, property);
                break;
            }

            default:
            {
                _ = builder.Append(symbol.Name);
                break;
            }
        }
    }

    /// <summary>Appends a type's own part of its identity.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="type">The type.</param>
    private static void AppendType(PooledStringBuilder builder, INamedTypeSymbol type)
    {
        if (RoslynFeatures.IsExtensionContainer(type))
        {
            AppendExtensionBlock(builder, type);
            return;
        }

        _ = builder.Append(type.Name);
        AppendArity(builder, type.Arity);
    }

    /// <summary>Appends an extension block's part of its identity: arity, receiver and constraints.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="type">The extension container.</param>
    private static void AppendExtensionBlock(PooledStringBuilder builder, INamedTypeSymbol type)
    {
        _ = builder.Append("extension");
        AppendArity(builder, type.Arity);
        _ = builder.Append('(');

        if (RoslynFeatures.ExtensionReceiver(type) is { } receiver)
        {
            AppendParameter(builder, receiver);
        }

        _ = builder.Append(')');

        // The constraints are what separate two blocks over one receiver, so they are part of the
        // key. The parser reads them from the clause text, which is what the renderer writes.
        var constraints = new PooledStringBuilder();
        ApiConstraints.Append(constraints, type.TypeParameters);
        AppendWithoutWhitespace(builder, constraints.ToString());
    }

    /// <summary>Appends a method's part of its identity.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="method">The method.</param>
    private static void AppendMethod(PooledStringBuilder builder, IMethodSymbol method)
    {
        switch (method.MethodKind)
        {
            case MethodKind.Constructor or MethodKind.StaticConstructor:
            {
                _ = builder.Append(ConstructorName);
                AppendParameters(builder, method.Parameters);
                return;
            }

            case MethodKind.Conversion:
            {
                _ = builder.Append("op").Append(method.Name is "op_Implicit" ? "implicit" : "explicit");
                AppendChecked(builder, method);
                AppendParameters(builder, method.Parameters);
                _ = builder.Append("->");
                AppendWithoutWhitespace(builder, method.ReturnType.ToDisplayString(ApiDisplayFormats.TypeReference));
                return;
            }

            case MethodKind.UserDefinedOperator:
            {
                _ = builder.Append("op");
                AppendChecked(builder, method);
                _ = builder.Append(ApiLiterals.OperatorToken(method.Name));
                AppendParameters(builder, method.Parameters);
                return;
            }

            default:
            {
                _ = builder.Append(method.Name);
                AppendArity(builder, method.Arity);
                AppendParameters(builder, method.Parameters);
                return;
            }
        }
    }

    /// <summary>Appends a property's part of its identity, which is a parameter list for an indexer.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="property">The property.</param>
    private static void AppendProperty(PooledStringBuilder builder, IPropertySymbol property)
    {
        if (!property.IsIndexer)
        {
            _ = builder.Append(property.Name);
            return;
        }

        _ = builder.Append("this");
        AppendParameters(builder, property.Parameters);
    }

    /// <summary>Appends the <c>checked</c> marker an operator's checked form carries.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="method">The operator.</param>
    private static void AppendChecked(PooledStringBuilder builder, IMethodSymbol method)
    {
        if (!ApiLiterals.IsCheckedOperator(method.Name))
        {
            return;
        }

        _ = builder.Append("checked");
    }

    /// <summary>Appends a parameter list the way overload identity sees it.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="parameters">The parameters.</param>
    private static void AppendParameters(PooledStringBuilder builder, ImmutableArray<IParameterSymbol> parameters)
    {
        _ = builder.Append('(');

        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(',');
            }

            AppendParameter(builder, parameters[i]);
        }

        _ = builder.Append(')');
    }

    /// <summary>Appends one parameter's contribution: how it is passed, then its type.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="parameter">The parameter.</param>
    private static void AppendParameter(PooledStringBuilder builder, IParameterSymbol parameter)
    {
        // Only the keywords the parser treats as changing the signature, spelled as it reads them.
        switch (parameter.RefKind)
        {
            case RefKind.Ref:
            {
                _ = builder.Append("ref ");
                break;
            }

            case RefKind.Out:
            {
                _ = builder.Append("out ");
                break;
            }

            case RefKind.In:
            {
                _ = builder.Append("in ");
                break;
            }

            case RefKind.RefReadOnlyParameter:
            {
                _ = builder.Append("ref readonly ");
                break;
            }

            default:
            {
                // By value, or a kind that does not change how the member is called.
                break;
            }
        }

        AppendWithoutWhitespace(builder, parameter.Type.ToDisplayString(ApiDisplayFormats.TypeReference));
    }

    /// <summary>Appends a generic arity marker, writing nothing for a declaration with none.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="arity">The arity.</param>
    private static void AppendArity(PooledStringBuilder builder, int arity)
    {
        if (arity == 0)
        {
            return;
        }

        _ = builder.Append('`').Append(arity.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Appends text with every whitespace character dropped.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="value">The text.</param>
    private static void AppendWithoutWhitespace(PooledStringBuilder builder, string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsWhiteSpace(value[i]))
            {
                _ = builder.Append(value[i]);
            }
        }
    }
}
