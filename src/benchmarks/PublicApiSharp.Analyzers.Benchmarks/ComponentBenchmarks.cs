// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Benchmarks;

/// <summary>Measures component entry points whose allocations are too small for whole-path sampling.</summary>
[ShortRunJob]
[MemoryDiagnoser]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class ComponentBenchmarks
{
    /// <summary>A stable declaration identity passed to record constructors.</summary>
    private const string Identity = "Sample.Thing0.Value";

    /// <summary>The declaration text passed to record constructors.</summary>
    private const string DeclarationText = "public int Value { get; set; }";

    /// <summary>The configured list key read on both analyzer paths.</summary>
    private const string ExcludedAttributesKey = "publicapisharp.excluded_attributes";

    /// <summary>The matching rendered text.</summary>
    private string _text = null!;

    /// <summary>The matching baseline.</summary>
    private SourceText _matching = null!;

    /// <summary>The baseline missing one property.</summary>
    private SourceText _violating = null!;

    /// <summary>A generic method whose identity includes a parameter type.</summary>
    private ISymbol _symbol = null!;

    /// <summary>The ordinary type used to exercise slot capabilities.</summary>
    private INamedTypeSymbol _type = null!;

    /// <summary>One declaration's input map, reused without mutation.</summary>
    private List<RenderedApiSurface.Written> _written = null!;

    /// <summary>A namespace's input type segment, prepared before measurement.</summary>
    private ArraySegment<INamedTypeSymbol> _types;

    /// <summary>Parsed declarations supplied to result construction.</summary>
    private ImmutableArray<ApiDeclaration> _declarations;

    /// <summary>The default editorconfig input.</summary>
    private BenchmarkWorkload.StubConfigOptions _empty = null!;

    /// <summary>A configured list with whitespace and empty entries.</summary>
    private BenchmarkWorkload.StubConfigOptions _configured = null!;

    /// <summary>Prepares Roslyn symbols and immutable input data outside measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var compilation = BenchmarkWorkload.Scaled(1);
        _type = BenchmarkWorkload.Type(compilation, "Sample.Thing0");
        _symbol = BenchmarkWorkload.Member(_type, "Find");
        _text = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None).Text;
        _matching = SourceText.From(_text);
        _violating = SourceText.From(BenchmarkWorkload.RemoveOneProperty(_text));
        _declarations = ApiTextParser.Parse(_matching, CancellationToken.None).Declarations;
        _written = [new(BenchmarkWorkload.Member(_type, "Value"), null, 0, DeclarationText.Length, 0, 0)];
        _types = new([_type]);
        _empty = BenchmarkWorkload.StubConfigOptions.From();
        _configured = BenchmarkWorkload.StubConfigOptions.From((ExcludedAttributesKey, " System.Diagnostics.* , , Sample.HiddenAttribute "));
    }

    /// <summary>Tests the clean analyzer's complete text match.</summary>
    /// <returns>True for the matching input.</returns>
    [Benchmark]
    public bool MatchingText() => ApiTextComparison.Matches(_matching, _text);

    /// <summary>Tests the comparison that sends a violation to declaration parsing.</summary>
    /// <returns>False for the baseline missing one property.</returns>
    [Benchmark]
    public bool ViolatingText() => ApiTextComparison.Matches(_violating, _text);

    /// <summary>Constructs the declaration record produced only after the clean fast path fails.</summary>
    /// <returns>The newly constructed record.</returns>
    [Benchmark]
    public object CreateDeclaration() => new ApiDeclaration(Identity, DeclarationText, 0, new(0, DeclarationText.Length));

    /// <summary>Constructs a successful parser result around existing declarations.</summary>
    /// <returns>The newly constructed result.</returns>
    [Benchmark]
    public object ParsedResult() => ApiTextParseResult.Parsed(_declarations);

    /// <summary>Constructs the result used by an unreadable baseline.</summary>
    /// <returns>The newly constructed result.</returns>
    [Benchmark]
    public object MalformedResult() => ApiTextParseResult.Malformed("Expected closing brace", default);

    /// <summary>Builds the symbol identity requested by declaration comparison.</summary>
    /// <returns>The method identity.</returns>
    [Benchmark]
    public string SymbolIdentity() => ApiIdentity.Of(_symbol);

    /// <summary>Builds the identity of an assembly attribute.</summary>
    /// <returns>The normalized attribute identity.</returns>
    [Benchmark]
    public string AttributeIdentity() => ApiIdentity.OfAssemblyAttribute("System.CLSCompliant(false)");

    /// <summary>Constructs the surface wrapper after rendering, on all three paths.</summary>
    /// <returns>The new wrapper over input storage.</returns>
    [Benchmark]
    public object CreateSurface() => new RenderedApiSurface(DeclarationText, _written);

    /// <summary>Materializes one declaration once, as a violating analyzer does.</summary>
    /// <returns>The declaration count.</returns>
    [Benchmark]
    public int BuildSurfaceDeclarations() => new RenderedApiSurface(DeclarationText, _written).Declarations.Length;

    /// <summary>Constructs the value record used while writing a declaration.</summary>
    /// <returns>The recorded end offset.</returns>
    [Benchmark]
    public int WrittenRecord() => new RenderedApiSurface.Written(_symbol, null, 0, DeclarationText.Length, 0, 0).End;

    /// <summary>Constructs the value record grouping a namespace's selected types.</summary>
    /// <returns>The recorded type count.</returns>
    [Benchmark]
    public int NamespaceRecord() => new ApiSurfaceRenderer.NamespaceTypes("Sample", _type.ContainingNamespace, _types).Types.Count;

    /// <summary>Reads an unconfigured compilation's render options.</summary>
    /// <returns>The shared default options.</returns>
    [Benchmark]
    public object DefaultOptions() => ApiRenderOptions.Read(_empty);

    /// <summary>Reads configured options with a nonempty attribute exclusion list.</summary>
    /// <returns>The newly constructed options.</returns>
    [Benchmark]
    public object ConfiguredOptions() => ApiRenderOptions.Read(_configured);

    /// <summary>Reads a missing list, as the default clean and violating paths do.</summary>
    /// <returns>The empty list length.</returns>
    [Benchmark]
    public int MissingOptionList() => AnalyzerOptionReader.ReadCommaSeparatedList(_empty, null, ExcludedAttributesKey).Length;

    /// <summary>Parses the configured list into its output array and strings.</summary>
    /// <returns>The nonempty entry count.</returns>
    [Benchmark]
    public int ConfiguredOptionList() => AnalyzerOptionReader.ReadCommaSeparatedList(_configured, null, ExcludedAttributesKey).Length;

    /// <summary>Exercises the slot-specific symbol capability on an ordinary type.</summary>
    /// <returns>False for the ordinary type.</returns>
    [Benchmark]
    public bool ExtensionCapability() => RoslynFeatures.IsExtensionContainer(_type);

    /// <summary>Constructs the five descriptors exposed by a new analyzer instance.</summary>
    /// <returns>The immutable array length.</returns>
    [Benchmark]
    public int DescriptorArray() => ImmutableArrays.Of(PublicApiRules.Added, PublicApiRules.Removed, PublicApiRules.Changed, PublicApiRules.MissingBaseline, PublicApiRules.UnreadableBaseline).Length;

    /// <summary>Constructs the IDs exposed by a new code fix provider instance.</summary>
    /// <returns>The immutable array length.</returns>
    [Benchmark]
    public int DiagnosticIdArray() => ImmutableArrays.Of(PublicApiRules.AddedId, PublicApiRules.ChangedId).Length;
}
