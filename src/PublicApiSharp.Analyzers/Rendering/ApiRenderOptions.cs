// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;

namespace PublicApiSharp.Analyzers;

/// <summary>The knobs that change what the rendered surface contains, read from editorconfig.</summary>
/// <remarks>
/// Configuration comes from the compiler's <see cref="AnalyzerConfigOptions"/> rather than from a
/// file this package reads itself: an analyzer that does its own I/O cannot be cached by the
/// compiler and misbehaves in the IDE, where the "file" may be an unsaved buffer.
/// </remarks>
internal sealed class ApiRenderOptions
{
    /// <summary>The prefix every option key in this package shares.</summary>
    private const string Prefix = "publicapisharp.";

    /// <summary>Patterns matching attributes the project has chosen not to record.</summary>
    private readonly string[] _excludedAttributes;

    /// <summary>Patterns matching attributes the project wants recorded despite the built-in list.</summary>
    private readonly string[] _includedAttributes;

    /// <summary>Namespace prefixes the project has chosen not to record.</summary>
    private readonly string[] _excludedNamespacePrefixes;

    /// <summary>Initializes a new instance of the <see cref="ApiRenderOptions"/> class.</summary>
    /// <param name="includeAssemblyAttributes">Whether assembly-level attributes are recorded.</param>
    /// <param name="excludedAttributes">Patterns matching attributes not to record.</param>
    /// <param name="includedAttributes">Patterns matching attributes to record despite the built-in list.</param>
    /// <param name="excludedNamespacePrefixes">Namespace prefixes not to record.</param>
    private ApiRenderOptions(
        bool includeAssemblyAttributes,
        string[] excludedAttributes,
        string[] includedAttributes,
        string[] excludedNamespacePrefixes)
    {
        IncludeAssemblyAttributes = includeAssemblyAttributes;
        _excludedAttributes = excludedAttributes;
        _includedAttributes = includedAttributes;
        _excludedNamespacePrefixes = excludedNamespacePrefixes;
    }

    /// <summary>Gets the options used when nothing is configured.</summary>
    internal static ApiRenderOptions Default { get; } = new(true, [], [], []);

    /// <summary>
    /// Gets a value indicating whether assembly-level attributes are recorded. They are part of the
    /// surface — InternalsVisibleTo, CLSCompliant and the platform attributes all change what
    /// consumers can do — so this defaults to on.
    /// </summary>
    internal bool IncludeAssemblyAttributes { get; }

    /// <summary>Reads the options for a compilation.</summary>
    /// <param name="options">The analyzer config options for the compilation.</param>
    /// <param name="fileScoped">Options from a file the compilation contains, or <see langword="null"/>.</param>
    /// <returns>The render options.</returns>
    /// <remarks>
    /// These describe the compilation as a whole, so the natural source is the global options: a
    /// global config, and whatever MSBuild makes compiler-visible. An ordinary <c>.editorconfig</c>
    /// is sectioned and therefore per-file, so nothing written in one ever reaches those, which is
    /// why a file's options are consulted as well. A setting of this kind is meant to be written
    /// once for the project rather than varied between files.
    /// </remarks>
    internal static ApiRenderOptions Read(AnalyzerConfigOptions options, AnalyzerConfigOptions? fileScoped = null)
    {
        var includeAssemblyAttributes = true;
        if (AnalyzerOptionReader.TryRead(options, fileScoped, $"{Prefix}include_assembly_attributes", out var value)
            && bool.TryParse(value, out var parsed))
        {
            includeAssemblyAttributes = parsed;
        }

        return new(
            includeAssemblyAttributes,
            AnalyzerOptionReader.ReadCommaSeparatedList(options, fileScoped, $"{Prefix}excluded_attributes"),
            AnalyzerOptionReader.ReadCommaSeparatedList(options, fileScoped, $"{Prefix}included_attributes"),
            AnalyzerOptionReader.ReadCommaSeparatedList(options, fileScoped, $"{Prefix}excluded_namespace_prefixes"));
    }

    /// <summary>Determines whether configuration excludes an attribute.</summary>
    /// <param name="fullName">The attribute type's fully qualified name.</param>
    /// <returns><see langword="true"/> when the attribute should not be rendered.</returns>
    internal bool IsAttributeExcluded(string fullName) => NamePattern.MatchesAny(_excludedAttributes, fullName);

    /// <summary>Determines whether configuration asks for an attribute the built-in list would drop.</summary>
    /// <param name="fullName">The attribute type's fully qualified name.</param>
    /// <returns><see langword="true"/> when the attribute should be rendered regardless.</returns>
    /// <remarks>
    /// The escape hatch for the built-in exclusions. A project that genuinely tracks, say, its
    /// assembly version can ask for it back without losing the rest of the defaults.
    /// </remarks>
    internal bool IsAttributeIncluded(string fullName) => NamePattern.MatchesAny(_includedAttributes, fullName);

    /// <summary>Determines whether a namespace is excluded by configuration.</summary>
    /// <param name="namespaceName">The fully qualified namespace name.</param>
    /// <returns><see langword="true"/> when types in the namespace should not be rendered.</returns>
    internal bool IsNamespaceExcluded(string namespaceName)
    {
        for (var i = 0; i < _excludedNamespacePrefixes.Length; i++)
        {
            var prefix = _excludedNamespacePrefixes[i];
            if (namespaceName.StartsWith(prefix, StringComparison.Ordinal)
                && (namespaceName.Length == prefix.Length || namespaceName[prefix.Length] == '.'))
            {
                return true;
            }
        }

        return false;
    }
}
