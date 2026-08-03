// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;

using BenchmarkDotNet.Attributes;

using BenchmarkDotNet.Diagnosers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Benchmarks;

/// <summary>Measures the whole job: render, read back, pair the two sides, and report.</summary>
/// <remarks>
/// These are what a build actually pays. The analyzer benchmark runs the real Roslyn driver, so it
/// includes the per-symbol callbacks the design deliberately keeps cheap.
/// </remarks>
[ShortRunJob]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class PipelineBenchmarks
{
    /// <summary>The line of the rendered surface the symbol lookup is asked about.</summary>
    private const int SampleLine = 3;

    /// <summary>The length of the span the baseline location is built over.</summary>
    private const int SampleSpanLength = 10;

    /// <summary>The compilation under analysis.</summary>
    private Compilation _compilation = null!;

    /// <summary>The rendered surface, for the comparison benchmarks.</summary>
    private RenderedApiSurface _surface = null!;

    /// <summary>The baseline, parsed, matching the compilation exactly.</summary>
    private ApiTextParseResult _baseline = null!;

    /// <summary>The declarations the comparison indexes.</summary>
    private ImmutableArray<ApiDeclaration> _declarations;

    /// <summary>The baseline as an additional file, as the analyzer receives it.</summary>
    private ImmutableArray<AdditionalText> _additionalFiles;

    /// <summary>The baseline's text.</summary>
    private SourceText _baselineText = null!;

    /// <summary>The analyzer under a real Roslyn driver.</summary>
    private CompilationWithAnalyzers _driver = null!;

    /// <summary>A symbol to locate a diagnostic against.</summary>
    private ISymbol _symbol = null!;

    /// <summary>Gets or sets the number of public types the analyzed assembly declares.</summary>
    [Params(BenchmarkParameterValues.SmallTypeCount, BenchmarkParameterValues.LargeTypeCount)]
    public int TypeCount { get; set; }

    /// <summary>Builds the compilation, its baseline and the analyzer driver once per parameter set.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _compilation = BenchmarkWorkload.Scaled(TypeCount);
        _surface = ApiSurfaceRenderer.Render(_compilation, ApiRenderOptions.Default, CancellationToken.None);
        _baselineText = SourceText.From(_surface.Text);
        _baseline = ApiTextParser.Parse(_baselineText, CancellationToken.None);
        _declarations = _baseline.Declarations;
        _symbol = BenchmarkWorkload.Type(_compilation, "Sample.Thing0");
        _additionalFiles = [new InMemoryAdditionalText(PublicApiBaselineAnalyzer.BaselineFileName, _baselineText)];

        _driver = CreateDriver();
    }

    /// <summary>Gives each iteration a driver that has not already answered.</summary>
    /// <remarks>
    /// <see cref="CompilationWithAnalyzers"/> memoizes its diagnostics, so reusing one across
    /// iterations measures a dictionary lookup — it reported ~120ns for a whole assembly, and was
    /// faster at a hundred types than at ten. The driver has to be new for the run to be real.
    /// </remarks>
    [IterationSetup(Target = nameof(AnalyzeMatchingBaselineAsync))]
    public void ResetDriver() => _driver = CreateDriver();

    /// <summary>Runs the analyzer end to end against a matching baseline: the everyday build cost.</summary>
    /// <returns>The diagnostic count, so the work cannot be optimized away.</returns>
    [Benchmark]
    public async Task<int> AnalyzeMatchingBaselineAsync()
    {
        var diagnostics = await _driver.GetAnalyzerDiagnosticsAsync(CancellationToken.None).ConfigureAwait(false);
        return diagnostics.Length;
    }

    /// <summary>Pairs a rendered surface with a parsed baseline.</summary>
    /// <returns>The number of indexed declarations, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int CreateComparisonState() =>
        ApiComparisonState.Create(_surface, _baseline, CancellationToken.None)!.CurrentByIdentity.Count;

    /// <summary>Renders and compares in one step, as the analyzer's lazy state does.</summary>
    /// <returns>The number of indexed declarations, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int CreateComparisonStateFromCompilation() =>
        ApiComparisonState.Create(_compilation, _baseline, ApiRenderOptions.Default, CancellationToken.None)!
            .BaselineByIdentity.Count;

    /// <summary>Indexes declarations by identity, the dictionary every lookup goes through.</summary>
    /// <returns>The entry count, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int IndexDeclarations() => ApiComparisonState.Index(_declarations).Count;

    /// <summary>Maps a line of the rendered surface back to the symbol that produced it.</summary>
    /// <returns>Whether a symbol was found, so the work cannot be optimized away.</returns>
    [Benchmark]
    public bool SymbolAtLine() => _surface.SymbolAtLine(SampleLine) is not null;

    /// <summary>Finds where in source a diagnostic about a symbol should be reported.</summary>
    /// <returns>Whether the location is in source, so the work cannot be optimized away.</returns>
    [Benchmark]
    public bool SymbolLocation() => PublicApiBaselineAnalyzer.SymbolLocation(_symbol).IsInSource;

    /// <summary>Finds the baseline among the compilation's additional files.</summary>
    /// <returns>Whether one was found, so the work cannot be optimized away.</returns>
    [Benchmark]
    public bool FindBaseline() => PublicApiBaselineAnalyzer.FindBaseline(_additionalFiles, null) is not null;

    /// <summary>Tests whether a path's final segment names a baseline file.</summary>
    /// <returns>The decision.</returns>
    [Benchmark]
    public bool EndsWithFileName() =>
        PublicApiBaselineAnalyzer.EndsWithFileName("/repo/src/PublicAPI/net10.0/PublicAPI.txt");

    /// <summary>Takes the declaration line out of text that begins with attributes.</summary>
    /// <returns>The final line.</returns>
    [Benchmark]
    public string FinalLine() =>
        PublicApiBaselineAnalyzer.FinalLine("[System.Obsolete]\npublic int Value { get; set; }");

    /// <summary>Builds a location inside the baseline file.</summary>
    /// <returns>Whether the location has a path, so the work cannot be optimized away.</returns>
    [Benchmark]
    public bool BaselineLocation()
    {
        TextSpan span = new(0, SampleSpanLength);
        var location = PublicApiBaselineAnalyzer.BaselineLocation(_additionalFiles[0], _baselineText, span);
        return location.GetLineSpan().Path.Length > 0;
    }

    /// <summary>Gets the analyzer config options of a file the compilation contains.</summary>
    /// <returns>Whether options were found, so the work cannot be optimized away.</returns>
    [Benchmark]
    public bool FileScopedOptions()
    {
        AnalyzerOptions options = new(_additionalFiles);
        return PublicApiBaselineAnalyzer.FileScopedOptions(options, _compilation) is not null;
    }

    /// <summary>Builds a fresh analyzer driver over the benchmark compilation.</summary>
    /// <returns>The driver.</returns>
    private CompilationWithAnalyzers CreateDriver()
    {
        // The overload taking a cancellation token is obsolete, and the one taking bare analyzer
        // options wants one; this third form asks for neither.
        AnalyzerOptions analyzerOptions = new(_additionalFiles);
        CompilationWithAnalyzersOptions analysisOptions = new(
            analyzerOptions,
            onAnalyzerException: null,
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false);

        return _compilation.WithAnalyzers([new PublicApiBaselineAnalyzer()], analysisOptions);
    }

    /// <summary>An <see cref="AdditionalText"/> whose content is held in memory.</summary>
    private sealed class InMemoryAdditionalText : AdditionalText
    {
        /// <summary>The file's content.</summary>
        private readonly SourceText _text;

        /// <summary>Initializes a new instance of the <see cref="InMemoryAdditionalText"/> class.</summary>
        /// <param name="path">The file's path.</param>
        /// <param name="text">The file's content.</param>
        internal InMemoryAdditionalText(string path, SourceText text)
        {
            Path = path;
            _text = text;
        }

        /// <inheritdoc/>
        public override string Path { get; }

        /// <inheritdoc/>
        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }
}
