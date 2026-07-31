// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace PublicApiSharp.Analyzers;

/// <summary>Renders the attributes that form part of a declaration's contract.</summary>
/// <remarks>
/// Working from source symbols rather than from compiled metadata removes most of the noise a
/// metadata reader has to filter out: <c>required</c>, nullability, <c>ref struct</c> and
/// <c>readonly struct</c> are symbol properties here, not synthesized attributes, and the compiler
/// never invents an <c>Obsolete</c> to describe them. What is left to exclude is the build's own
/// bookkeeping — assembly identity stamped in by the SDK, and attributes that only describe how the
/// compiler emitted something.
/// </remarks>
internal static class ApiAttributeRenderer
{
    /// <summary>The suffix C# lets an attribute be written without.</summary>
    private const string AttributeSuffix = "Attribute";

    /// <summary>Attributes that describe the build or the compiler's output rather than the API.</summary>
    /// <remarks>
    /// The version and target-framework attributes matter most here. The SDK stamps them into every
    /// assembly, so leaving them in would rewrite every baseline in a repository on each release —
    /// a version bump is not an API change — and would restate the target framework that the
    /// baseline's own folder already names.
    /// </remarks>
    private static readonly HashSet<string> NotPartOfTheApi = new(StringComparer.Ordinal)
    {
        "System.CodeDom.Compiler.GeneratedCodeAttribute",
        "System.Reflection.AssemblyCultureAttribute",
        "System.Reflection.AssemblyDelaySignAttribute",
        "System.Reflection.AssemblyKeyFileAttribute",
        "System.Reflection.AssemblyKeyNameAttribute",
        "System.Reflection.AssemblyMetadataAttribute",
        "System.Reflection.AssemblySignatureKeyAttribute",
        "System.Reflection.AssemblyVersionAttribute",
        "System.Runtime.Versioning.TargetFrameworkAttribute",
        "System.Runtime.Versioning.TargetPlatformAttribute",
        "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute",
        "System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute",
        "System.Diagnostics.DebuggableAttribute",
        "System.Diagnostics.DebuggerNonUserCodeAttribute",
        "System.Diagnostics.DebuggerStepThroughAttribute",
        "System.Reflection.AssemblyCompanyAttribute",
        "System.Reflection.AssemblyConfigurationAttribute",
        "System.Reflection.AssemblyCopyrightAttribute",
        "System.Reflection.AssemblyDescriptionAttribute",
        "System.Reflection.AssemblyFileVersionAttribute",
        "System.Reflection.AssemblyInformationalVersionAttribute",
        "System.Reflection.AssemblyProductAttribute",
        "System.Reflection.AssemblyTitleAttribute",
        "System.Reflection.AssemblyTrademarkAttribute",
        "System.Reflection.DefaultMemberAttribute",
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
        "System.Runtime.CompilerServices.CompilationRelaxationsAttribute",
        "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute",
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
        "System.Runtime.CompilerServices.ExtensionAttribute",
        "System.Runtime.CompilerServices.IsByRefLikeAttribute",
        "System.Runtime.CompilerServices.IsReadOnlyAttribute",
        "System.Runtime.CompilerServices.IsUnmanagedAttribute",
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute",
        "System.Runtime.CompilerServices.NullableAttribute",
        "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.RequiredMemberAttribute",
        "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",
    };

    /// <summary>Renders a symbol's attributes as one bracketed line each, in a stable order.</summary>
    /// <param name="builder">The builder to append to.</param>
    /// <param name="attributes">The attributes to render.</param>
    /// <param name="indent">The indentation each line starts with.</param>
    /// <param name="target">The attribute target prefix, for example an assembly target, or an empty string.</param>
    /// <param name="options">The render options.</param>
    /// <param name="lineCallback">Invoked for every line appended, so the caller can track line numbers.</param>
    internal static void Append(
        PooledStringBuilder builder,
        ImmutableArray<AttributeData> attributes,
        string indent,
        string target,
        ApiRenderOptions options,
        Action lineCallback)
    {
        if (attributes.IsEmpty)
        {
            return;
        }

        var rendered = new List<string>(attributes.Length);
        foreach (var attribute in attributes)
        {
            if (ShouldInclude(attribute, options))
            {
                rendered.Add(Render(attribute));
            }
        }

        // Attribute order in source is not meaningful, so sorting keeps the baseline from churning
        // when someone reorders them.
        rendered.Sort(StringComparer.Ordinal);

        foreach (var text in rendered)
        {
            _ = builder.Append(indent).Append('[').Append(target).Append(text).Append(']').Append('\n');
            lineCallback();
        }
    }

    /// <summary>Determines whether an attribute forms part of the public surface.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="options">The render options.</param>
    /// <returns><see langword="true"/> when the attribute should be rendered.</returns>
    internal static bool ShouldInclude(AttributeData attribute, ApiRenderOptions options)
    {
        if (attribute.AttributeClass is not { } attributeClass)
        {
            return false;
        }

        var fullName = attributeClass.ToDisplayString(ApiDisplayFormats.QualifiedName);

        // An attribute a consumer cannot name is not part of the surface they can depend on, and no
        // configuration overrides that. After that the precedence is: an explicit exclusion wins
        // over everything, then an explicit inclusion overrides the built-in list, and otherwise
        // the built-in list applies.
        return ApiSymbolFilter.IsExternallyVisible(attributeClass)
            && !options.IsAttributeExcluded(fullName)
            && (options.IsAttributeIncluded(fullName) || !NotPartOfTheApi.Contains(fullName));
    }

    /// <summary>Renders a single attribute application without its enclosing brackets.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <returns>The rendered attribute.</returns>
    internal static string Render(AttributeData attribute)
    {
        var builder = new PooledStringBuilder();
        var name = attribute.AttributeClass!.ToDisplayString(ApiDisplayFormats.QualifiedName);

        // C# lets an attribute be written without its suffix, and that is how it is written in
        // source, so that is how it is recorded.
        if (name.EndsWith(AttributeSuffix, StringComparison.Ordinal) && name.Length > AttributeSuffix.Length)
        {
            name = name.Substring(0, name.Length - AttributeSuffix.Length);
        }

        _ = builder.Append(name);

        if (attribute.ConstructorArguments.IsEmpty && attribute.NamedArguments.IsEmpty)
        {
            return builder.ToString();
        }

        _ = builder.Append('(');
        var first = true;

        foreach (var argument in attribute.ConstructorArguments)
        {
            if (!first)
            {
                _ = builder.Append(", ");
            }

            first = false;
            _ = builder.Append(argument.ToCSharpString());
        }

        AppendNamedArguments(builder, attribute, first);
        return builder.Append(')').ToString();
    }

    /// <summary>Appends an attribute's named arguments in a stable order.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="attribute">The attribute.</param>
    /// <param name="first">Whether no argument has been written yet.</param>
    private static void AppendNamedArguments(PooledStringBuilder builder, AttributeData attribute, bool first)
    {
        // Named arguments are unordered in source; sort so the baseline is stable.
        var named = new List<string>(attribute.NamedArguments.Length);
        foreach (var argument in attribute.NamedArguments)
        {
            named.Add($"{argument.Key}={argument.Value.ToCSharpString()}");
        }

        named.Sort(StringComparer.Ordinal);

        foreach (var argument in named)
        {
            if (!first)
            {
                _ = builder.Append(", ");
            }

            first = false;
            _ = builder.Append(argument);
        }
    }
}
