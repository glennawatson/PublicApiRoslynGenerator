// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Generic;

using BenchmarkDotNet.Attributes;

using BenchmarkDotNet.Diagnosers;

using Microsoft.CodeAnalysis;

namespace PublicApiSharp.Analyzers.Benchmarks;

/// <summary>Measures each step the renderer takes to turn one symbol into declaration text.</summary>
/// <remarks>
/// The whole-surface benchmark says what a compilation costs; these say which part of it. Every one
/// of them runs once per declaration in a library, so a per-call allocation here is multiplied by the
/// size of the surface.
/// </remarks>
[ShortRunJob]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class RenderingBenchmarks
{
    /// <summary>The compilation whose symbols are rendered.</summary>
    private Compilation _compilation = null!;

    /// <summary>A type with a base list, interfaces and attributes.</summary>
    private INamedTypeSymbol _currency = null!;

    /// <summary>A generic delegate, for the delegate path.</summary>
    private INamedTypeSymbol _delegateType = null!;

    /// <summary>An extension container, whose header is composed by hand.</summary>
    private INamedTypeSymbol _extension = null!;

    /// <summary>A constant field.</summary>
    private IFieldSymbol _constant = null!;

    /// <summary>A property with an init accessor.</summary>
    private IPropertySymbol _property = null!;

    /// <summary>An indexer, whose name is a parameter list.</summary>
    private IPropertySymbol _indexer = null!;

    /// <summary>An event.</summary>
    private IEventSymbol _event = null!;

    /// <summary>A generic method with constraints.</summary>
    private IMethodSymbol _generic = null!;

    /// <summary>A method whose parameters carry every reference kind.</summary>
    private IMethodSymbol _byReference = null!;

    /// <summary>A checked operator.</summary>
    private IMethodSymbol _checkedOperator = null!;

    /// <summary>The namespace holding the benchmark types.</summary>
    private INamespaceSymbol _namespace = null!;

    /// <summary>Every namespace in the compilation, for the file-scoped decision.</summary>
    private List<INamespaceSymbol> _namespaces = null!;

    /// <summary>Resolves the symbols each benchmark renders.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _compilation = BenchmarkWorkload.Broad();
        _currency = BenchmarkWorkload.Type(_compilation, "Sample.Currency");
        _delegateType = BenchmarkWorkload.Type(_compilation, "Sample.Transform`2");
        _constant = (IFieldSymbol)BenchmarkWorkload.Member(_currency, "Limit");
        _property = (IPropertySymbol)BenchmarkWorkload.Member(_currency, "Code");
        _event = (IEventSymbol)BenchmarkWorkload.Member(_currency, "Changed");
        _generic = (IMethodSymbol)BenchmarkWorkload.Member(_currency, "Reshape");
        _byReference = (IMethodSymbol)BenchmarkWorkload.Member(_currency, "Pass");
        _checkedOperator = (IMethodSymbol)BenchmarkWorkload.Member(_currency, "op_CheckedAddition");
        _namespace = _currency.ContainingNamespace;

        foreach (var member in _currency.GetMembers())
        {
            if (member is IPropertySymbol { IsIndexer: true } indexer)
            {
                _indexer = indexer;
                break;
            }
        }

        var helpers = BenchmarkWorkload.Type(_compilation, "Sample.Helpers");
        foreach (var nested in helpers.GetTypeMembers())
        {
            if (RoslynFeatures.IsExtensionContainer(nested))
            {
                _extension = nested;
                break;
            }
        }

        _namespaces = [_compilation.Assembly.GlobalNamespace, _namespace];
    }

    /// <summary>Renders a constant field, the shortest member path.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendConstantField() => Render(builder => ApiSurfaceRenderer.AppendMember(builder, _constant));

    /// <summary>Renders a property, which composes a name and an accessor list.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendProperty() => Render(builder => ApiSurfaceRenderer.AppendMember(builder, _property));

    /// <summary>Renders an indexer, whose name is itself a parameter list.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendIndexer() => Render(builder => ApiSurfaceRenderer.AppendMember(builder, _indexer));

    /// <summary>Renders an event.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendEvent() => Render(builder => ApiSurfaceRenderer.AppendMember(builder, _event));

    /// <summary>Renders a generic method, the path that also writes constraints.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendGenericMethod() => Render(builder => ApiSurfaceRenderer.AppendMember(builder, _generic));

    /// <summary>Renders a method whose parameters carry ref, out, in and params.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendByReferenceMethod() => Render(builder => ApiSurfaceRenderer.AppendMember(builder, _byReference));

    /// <summary>Renders a checked operator, which asks the compiler for its token.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendCheckedOperator() => Render(builder => ApiSurfaceRenderer.AppendMember(builder, _checkedOperator));

    /// <summary>Renders a delegate declaration.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendDelegate() => Render(builder => ApiSurfaceRenderer.AppendDelegate(builder, _delegateType));

    /// <summary>Renders a type header: modifiers, name, base list and constraints.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendTypeHeader() => Render(builder => ApiSurfaceRenderer.AppendTypeHeader(builder, _currency));

    /// <summary>Renders a type's base list on its own.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendBaseList() => Render(builder => ApiSurfaceRenderer.AppendBaseList(builder, _currency));

    /// <summary>Renders an extension block header, which is composed rather than displayed.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendExtensionHeader() => Render(builder => ApiSurfaceRenderer.AppendExtensionHeader(builder, _extension));

    /// <summary>Renders a method's type parameter list.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendTypeParameters() =>
        Render(builder => ApiSurfaceRenderer.AppendTypeParameters(builder, _generic.TypeParameters));

    /// <summary>Renders a property's accessor list.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendAccessors() => Render(builder => ApiSurfaceRenderer.AppendAccessors(builder, _property));

    /// <summary>Renders a property's name.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendPropertyName() => Render(builder => ApiSurfaceRenderer.AppendPropertyName(builder, _property));

    /// <summary>Rewrites a spelled-out default expression to its short form.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendNormalizedDefault() =>
        Render(static builder => ApiSurfaceRenderer.AppendNormalizedDefault(builder, "System.Threading.CancellationToken token = default(System.Threading.CancellationToken)"));

    /// <summary>Writes the checked keyword for an operator that has one.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendCheckedKeyword() =>
        Render(builder => ApiSurfaceRenderer.AppendCheckedKeyword(builder, _checkedOperator));

    /// <summary>Writes an accessor's accessibility when it is narrower than its property's.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendAccessorAccessibility() =>
        Render(builder => ApiSurfaceRenderer.AppendAccessorAccessibility(builder, _property.SetMethod!, _property));

    /// <summary>Selects and orders the visible types of a namespace.</summary>
    /// <returns>The number of types, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int VisibleTypes() => ApiSurfaceRenderer.VisibleTypes(_namespace, ApiRenderOptions.Default).Count;

    /// <summary>Builds the key a type orders under within its container.</summary>
    /// <returns>The sort key.</returns>
    [Benchmark]
    public string TypeSortKey() => ApiSurfaceRenderer.TypeSortKey(_extension);

    /// <summary>Decides whether the surface can use a file-scoped namespace declaration.</summary>
    /// <returns>The decision.</returns>
    [Benchmark]
    public bool UsesFileScopedNamespace() =>
        ApiSurfaceRenderer.UsesFileScopedNamespace(_namespaces, ApiRenderOptions.Default);

    /// <summary>Walks the assembly's namespaces.</summary>
    /// <returns>The number of namespaces found.</returns>
    [Benchmark]
    public int CollectNamespaces()
    {
        var into = new List<INamespaceSymbol>();
        ApiSurfaceRenderer.CollectNamespaces(_compilation.Assembly.GlobalNamespace, into, ApiRenderOptions.Default, CancellationToken.None);
        return into.Count;
    }

    /// <summary>Renders one whole type, header and members.</summary>
    /// <returns>The rendered length, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int RenderType()
    {
        var writer = new ApiSurfaceRenderer.SurfaceWriter();
        ApiSurfaceRenderer.RenderType(writer, _currency, string.Empty, ApiRenderOptions.Default, CancellationToken.None);
        return writer.Complete().Text.Length;
    }

    /// <summary>Renders a type's members, without its header or nested types.</summary>
    /// <returns>The rendered length, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int RenderMembers()
    {
        var writer = new ApiSurfaceRenderer.SurfaceWriter();
        ApiSurfaceRenderer.RenderMembers(writer, _currency, string.Empty, ApiRenderOptions.Default, CancellationToken.None);
        return writer.Complete().Text.Length;
    }

    /// <summary>Renders a whole namespace.</summary>
    /// <returns>The rendered length, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int RenderNamespace()
    {
        var writer = new ApiSurfaceRenderer.SurfaceWriter();
        ApiSurfaceRenderer.RenderNamespace(writer, _namespace, ApiRenderOptions.Default, fileScoped: true, CancellationToken.None);
        return writer.Complete().Text.Length;
    }

    /// <summary>Runs one append through a fresh builder and materializes the result.</summary>
    /// <param name="append">The append to measure.</param>
    /// <returns>The accumulated text.</returns>
    /// <remarks>
    /// The builder is single-use — <c>ToString</c> hands its buffer back to the pool — so each
    /// iteration needs its own, exactly as the renderer creates one per fragment.
    /// </remarks>
    private static string Render(Action<PooledStringBuilder> append)
    {
        var builder = new PooledStringBuilder();
        append(builder);
        return builder.ToString();
    }
}
