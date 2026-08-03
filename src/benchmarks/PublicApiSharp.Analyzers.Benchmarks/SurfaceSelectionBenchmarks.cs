// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Collections.Immutable;

using BenchmarkDotNet.Attributes;

using BenchmarkDotNet.Diagnosers;

using Microsoft.CodeAnalysis;

namespace PublicApiSharp.Analyzers.Benchmarks;

/// <summary>Measures deciding what belongs in the surface, in what order, and with which attributes.</summary>
/// <remarks>
/// The filters run for every member of every type, and the comparer runs O(n log n) times per type,
/// so both are executed far more often than anything that writes text.
/// </remarks>
[ShortRunJob]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class SurfaceSelectionBenchmarks
{
    /// <summary>The members of a type, unsorted, as the renderer receives them.</summary>
    private ISymbol[] _members = null!;

    /// <summary>A public method.</summary>
    private IMethodSymbol _method = null!;

    /// <summary>Two overloads separated only by a reference kind.</summary>
    private IMethodSymbol _left = null!;

    /// <summary>The second of those overloads.</summary>
    private IMethodSymbol _right = null!;

    /// <summary>A type carrying several attributes.</summary>
    private INamedTypeSymbol _decorated = null!;

    /// <summary>One attribute with a constructor argument and a named one.</summary>
    private AttributeData _attribute = null!;

    /// <summary>The assembly's own attributes.</summary>
    private ImmutableArray<AttributeData> _assemblyAttributes;

    /// <summary>Options configured with patterns, so matching does real work.</summary>
    private ApiRenderOptions _configured = null!;

    /// <summary>Resolves the symbols each benchmark selects over.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var compilation = BenchmarkWorkload.Broad();
        _decorated = BenchmarkWorkload.Type(compilation, "Sample.Currency");
        _members = [.. _decorated.GetMembers()];
        _method = (IMethodSymbol)BenchmarkWorkload.Member(_decorated, "CompareTo");
        _left = (IMethodSymbol)BenchmarkWorkload.Member(_decorated, "Pass");
        _right = (IMethodSymbol)BenchmarkWorkload.Member(_decorated, "Reshape");
        _assemblyAttributes = compilation.Assembly.GetAttributes();

        foreach (var attribute in _decorated.GetAttributes())
        {
            _attribute = attribute;
            if (!attribute.NamedArguments.IsEmpty)
            {
                break;
            }
        }

        _configured = ApiRenderOptions.Read(BenchmarkWorkload.StubConfigOptions.From(
            ("publicapisharp.excluded_attributes", "System.Diagnostics.CodeAnalysis.*, *.InternalUseAttribute"),
            ("publicapisharp.included_attributes", "System.Reflection.AssemblyMetadataAttribute"),
            ("publicapisharp.excluded_namespace_prefixes", "Sample.Internals, Contoso.Private")));
    }

    /// <summary>Decides whether a symbol is visible to a consumer of the assembly.</summary>
    /// <returns>The decision.</returns>
    [Benchmark]
    public bool IsExternallyVisible() => ApiSymbolFilter.IsExternallyVisible(_method);

    /// <summary>Decides whether a member is rendered at all, before accessibility.</summary>
    /// <returns>The decision.</returns>
    [Benchmark]
    public bool IsRenderableMember() => ApiSymbolFilter.IsRenderableMember(_method);

    /// <summary>Decides whether a declaration was written by a tool.</summary>
    /// <returns>The decision.</returns>
    [Benchmark]
    public bool IsGeneratedCode() => ApiSymbolFilter.IsGeneratedCode(_decorated);

    /// <summary>Sorts a type's members into the order the surface writes them.</summary>
    /// <returns>The member count, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int SortMembers()
    {
        var members = new List<ISymbol>(_members);
        members.Sort(ApiMemberOrder.Comparison);
        return members.Count;
    }

    /// <summary>Orders two members of different kinds.</summary>
    /// <returns>The comparison result.</returns>
    [Benchmark]
    public int CompareNonNull() => ApiMemberOrder.CompareNonNull(_left, _right);

    /// <summary>Orders two overloads by arity and parameters.</summary>
    /// <returns>The comparison result.</returns>
    [Benchmark]
    public int CompareOverloads() => ApiMemberOrder.CompareOverloads(_left, _right);

    /// <summary>Orders two parameter lists, comparing types and reference kinds.</summary>
    /// <returns>The comparison result.</returns>
    [Benchmark]
    public int CompareParameters() => ApiMemberOrder.CompareParameters(_left.Parameters, _right.Parameters);

    /// <summary>Renders one attribute application.</summary>
    /// <returns>The rendered attribute.</returns>
    [Benchmark]
    public string RenderAttribute() => ApiAttributeRenderer.Render(_attribute);

    /// <summary>Decides whether an attribute forms part of the surface.</summary>
    /// <returns>The decision.</returns>
    [Benchmark]
    public bool ShouldIncludeAttribute() => ApiAttributeRenderer.ShouldInclude(_attribute, ApiRenderOptions.Default);

    /// <summary>Decides the same thing against configured patterns, which have to be matched.</summary>
    /// <returns>The decision.</returns>
    [Benchmark]
    public bool ShouldIncludeAttributeConfigured() => ApiAttributeRenderer.ShouldInclude(_attribute, _configured);

    /// <summary>Renders a symbol's attributes as one sorted line each.</summary>
    /// <returns>The rendered block.</returns>
    [Benchmark]
    public string AppendAttributes()
    {
        var builder = new PooledStringBuilder();
        ApiAttributeRenderer.Append(builder, _assemblyAttributes, string.Empty, "assembly: ", ApiRenderOptions.Default, static _ => { });
        return builder.ToString();
    }

    /// <summary>Appends an attribute's named arguments in a stable order.</summary>
    /// <returns>The rendered arguments.</returns>
    [Benchmark]
    public string AppendNamedArguments()
    {
        var builder = new PooledStringBuilder();
        ApiAttributeRenderer.AppendNamedArguments(builder, _attribute, first: true);
        return builder.ToString();
    }

    /// <summary>Reads the render options out of analyzer config.</summary>
    /// <returns>Whether generated code is recorded, so the work cannot be optimized away.</returns>
    [Benchmark]
    public bool ReadOptions() =>
        ApiRenderOptions.Read(BenchmarkWorkload.StubConfigOptions.From(
            ("publicapisharp.include_assembly_attributes", "false"),
            ("publicapisharp.include_generated_code", "true"),
            ("publicapisharp.excluded_attributes", "System.Diagnostics.*"))).IncludeGeneratedCode;

    /// <summary>Matches a name against the configured exclusions.</summary>
    /// <returns>The decision.</returns>
    [Benchmark]
    public bool IsAttributeExcluded() => _configured.IsAttributeExcluded("System.Diagnostics.CodeAnalysis.NotNullWhenAttribute");

    /// <summary>Matches a name against the configured inclusions.</summary>
    /// <returns>The decision.</returns>
    [Benchmark]
    public bool IsAttributeIncluded() => _configured.IsAttributeIncluded("System.Reflection.AssemblyMetadataAttribute");

    /// <summary>Tests a namespace against the configured prefixes.</summary>
    /// <returns>The decision.</returns>
    [Benchmark]
    public bool IsNamespaceExcluded() => _configured.IsNamespaceExcluded("Sample.Internals.Deep");

    /// <summary>Matches a name against a pattern whose wildcard has to backtrack.</summary>
    /// <returns>The decision.</returns>
    [Benchmark]
    public bool MatchWildcardPattern() => NamePattern.Matches("*.CodeAnalysis.*Attribute", "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute");

    /// <summary>Matches a name that fails every pattern, the common case for an unconfigured project.</summary>
    /// <returns>The decision.</returns>
    [Benchmark]
    public bool MatchNoPattern() => NamePattern.MatchesAny(["System.Diagnostics.*", "*.InternalUseAttribute"], "Sample.WidgetAttribute");
}
