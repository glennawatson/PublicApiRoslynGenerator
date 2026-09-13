// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Verifies callback registration and the comparison shared by a compilation.</summary>
public class PublicApiCompilationStartTests
{
    /// <summary>The configured path used when a baseline is missing.</summary>
    private const string BaselinePath = "/PublicAPI.txt";

    /// <summary>An internal surface with several symbol callbacks and no public declarations.</summary>
    private const string Source = "internal class C { internal int Value { get; set; } internal void M() { } }";

    /// <summary>Verifies an unconfigured project registers no symbol or compilation-end action.</summary>
    /// <param name="path">The absent or empty baseline path.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task NoBaselinePathRegistersNoActionsAsync(string? path)
    {
        var provider = new CountingOptionsProvider(path);
        var context = new CapturingContext(Compile(), new([], provider));

        PublicApiBaselineAnalyzer.OnCompilationStart(context);

        await Assert.That(context.SymbolAction).IsNull();
        await Assert.That(context.EndAction).IsNull();
    }

    /// <summary>Verifies enabling PAS0004 does not opt a project into baseline tracking.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public Task EnabledMissingBaselineRuleWithoutAPathIsSilentAsync() =>
        PublicApiVerifier.AnalyzeWithConfigAsync(
            "public class C { }",
            baseline: null,
            "Unused.txt",
            "dotnet_diagnostic.PAS0004.severity = warning");

    /// <summary>Verifies symbol and compilation-end callbacks share one state object.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task RegisteredCallbacksShareTheirStateAsync()
    {
        var context = CreateContext(new());

        PublicApiBaselineAnalyzer.OnCompilationStart(context);

        await Assert.That(context.SymbolAction).IsNotNull();
        await Assert.That(context.EndAction).IsNotNull();
        await Assert.That(context.SymbolAction!.Target).IsNotNull();
        await Assert.That(ReferenceEquals(context.SymbolAction.Target, context.EndAction!.Target)).IsTrue();
    }

    /// <summary>Verifies concurrent symbol callbacks and compilation end initialize the comparison once.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task ComparisonInitializesOnceAcrossCallbacksAsync()
    {
        var provider = new CountingOptionsProvider();
        var context = CreateContext(provider);

        var diagnostics = await AnalyzeAsync(context);

        await Assert.That(provider.Initializations).IsEqualTo(1);
        await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>Verifies a factory failure is retained for later symbol and compilation-end callbacks.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task ComparisonRetainsItsFactoryExceptionAsync()
    {
        var provider = new CountingOptionsProvider(fail: true);
        var context = CreateContext(provider);
        var failures = new ConcurrentQueue<Exception>();

        _ = await AnalyzeAsync(context, (exception, _, _) => failures.Enqueue(exception));

        await Assert.That(failures).Count().IsGreaterThan(1);
        foreach (var failure in failures)
        {
            await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        }

        await Assert.That(provider.Initializations).IsEqualTo(1);
    }

    /// <summary>Verifies only the first source tree supplies compilation-wide file options.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task FirstSyntaxTreeSuppliesFileOptionsAsync()
    {
        var first = CSharpSyntaxTree.ParseText("class First { }");
        var second = CSharpSyntaxTree.ParseText("class Second { }");
        var compilation = CSharpCompilation.Create("Options", [first, second]);
        var provider = new CountingOptionsProvider();

        var options = PublicApiBaselineAnalyzer.FileScopedOptions(new([], provider), compilation);

        await Assert.That(options).IsEqualTo(provider.FileOptions);
        await Assert.That(provider.Tree).IsEqualTo(first);
        await Assert.That(provider.Reads).IsEqualTo(1);
    }

    /// <summary>Verifies the reporting helper also preserves the unconfigured-project contract.</summary>
    /// <param name="path">The absent or empty baseline path.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task MissingBaselineHelperIgnoresAbsentPathsAsync(string? path)
    {
        var provider = new CountingOptionsProvider(path);
        var context = default(CompilationAnalysisContext);

        await Assert.That(() => PublicApiBaselineAnalyzer.ReportMissingBaseline(in context, provider.GlobalOptions, path)).ThrowsNothing();
    }

    /// <summary>Verifies a configured missing baseline registers only its compilation-end warning.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task ConfiguredMissingBaselineRegistersOnlyCompilationEndAsync()
    {
        var context = new CapturingContext(
            Compile(),
            new([], new CountingOptionsProvider(BaselinePath)));
        PublicApiBaselineAnalyzer.OnCompilationStart(context);
        await Assert.That(context.SymbolAction).IsNull();
        await Assert.That(context.EndAction).IsNotNull();
        var diagnostics = await AnalyzeAsync(context);

        await Assert.That(diagnostics).Count().IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo(PublicApiRules.MissingBaselineId);
    }

    /// <summary>Creates a compilation whose empty public surface matches the baseline.</summary>
    /// <param name="provider">The options whose reads are observed.</param>
    /// <returns>The compilation-start context.</returns>
    private static CapturingContext CreateContext(CountingOptionsProvider provider) =>
        new(Compile(), new([new EmptyBaseline()], provider));

    /// <summary>Compiles the internal surface with missing-baseline warnings enabled.</summary>
    /// <returns>The compilation.</returns>
    private static CSharpCompilation Compile()
    {
        var compilation = ApiSurfaceTestHost.Compile(Source);
        return compilation.WithOptions(compilation.Options.WithSpecificDiagnosticOptions(
            compilation.Options.SpecificDiagnosticOptions.SetItem(PublicApiRules.MissingBaselineId, ReportDiagnostic.Warn)));
    }

    /// <summary>Runs the analyzer with real symbol and compilation-end contexts.</summary>
    /// <param name="context">The compilation-start context.</param>
    /// <param name="onException">The optional failure observer.</param>
    /// <returns>The reported diagnostics.</returns>
    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        CapturingContext context,
        Action<Exception, DiagnosticAnalyzer, Diagnostic>? onException = null)
    {
        var options = new CompilationWithAnalyzersOptions(context.Options, onException, concurrentAnalysis: true, logAnalyzerExecutionTime: false);
        return context.Compilation.WithAnalyzers([new PublicApiBaselineAnalyzer()], options).GetAnalyzerDiagnosticsAsync(CancellationToken.None);
    }

    /// <summary>Records the callbacks registered for a compilation.</summary>
    private sealed class CapturingContext : CompilationStartAnalysisContext
    {
        /// <summary>Initializes a new instance of the <see cref="CapturingContext"/> class.</summary>
        /// <param name="compilation">The compilation to analyze.</param>
        /// <param name="options">The baseline and configuration.</param>
        internal CapturingContext(Compilation compilation, AnalyzerOptions options)
            : base(compilation, options, CancellationToken.None)
        {
        }

        /// <summary>Gets the registered symbol callback.</summary>
        internal Action<SymbolAnalysisContext>? SymbolAction { get; private set; }

        /// <summary>Gets the registered compilation-end callback.</summary>
        internal Action<CompilationAnalysisContext>? EndAction { get; private set; }

        /// <inheritdoc/>
        public override void RegisterCompilationEndAction(Action<CompilationAnalysisContext> action) => EndAction = action;

        /// <inheritdoc/>
        public override void RegisterSymbolAction(Action<SymbolAnalysisContext> action, ImmutableArray<SymbolKind> symbolKinds) => SymbolAction = action;

        /// <inheritdoc/>
        public override void RegisterSemanticModelAction(Action<SemanticModelAnalysisContext> action) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void RegisterCodeBlockStartAction<TLanguageKindEnum>(Action<CodeBlockStartAnalysisContext<TLanguageKindEnum>> action) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void RegisterCodeBlockAction(Action<CodeBlockAnalysisContext> action) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void RegisterSyntaxTreeAction(Action<SyntaxTreeAnalysisContext> action) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void RegisterSyntaxNodeAction<TLanguageKindEnum>(Action<SyntaxNodeAnalysisContext> action, ImmutableArray<TLanguageKindEnum> syntaxKinds) => throw new NotSupportedException();
    }

    /// <summary>Counts the file-scoped option reads performed by the lazy factory.</summary>
    private sealed class CountingOptionsProvider : AnalyzerConfigOptionsProvider
    {
        /// <summary>The global and file options.</summary>
        private readonly BaselineOptions _options;

        /// <summary>The rendering options read only by the comparison factory.</summary>
        private readonly FactoryOptions _fileOptions;

        /// <summary>The number of file option reads.</summary>
        private int _reads;

        /// <summary>Initializes a new instance of the <see cref="CountingOptionsProvider"/> class.</summary>
        /// <param name="path">The configured baseline path.</param>
        /// <param name="fail">Whether reading file options fails.</param>
        internal CountingOptionsProvider(string? path = null, bool fail = false)
        {
            _options = new(path);
            _fileOptions = new(fail);
        }

        /// <inheritdoc/>
        public override AnalyzerConfigOptions GlobalOptions => _options;

        /// <summary>Gets the file-scoped rendering options.</summary>
        internal AnalyzerConfigOptions FileOptions => _fileOptions;

        /// <summary>Gets the number of comparison factory evaluations.</summary>
        internal int Initializations => _fileOptions.Reads;

        /// <summary>Gets the number of file option reads.</summary>
        internal int Reads => Volatile.Read(ref _reads);

        /// <summary>Gets the tree whose options were requested.</summary>
        internal SyntaxTree? Tree { get; private set; }

        /// <inheritdoc/>
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            _ = Interlocked.Increment(ref _reads);
            Tree = tree;
            return _fileOptions;
        }

        /// <inheritdoc/>
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    /// <summary>Observes a rendering option separately from Roslyn's diagnostic option reads.</summary>
    private sealed class FactoryOptions : AnalyzerConfigOptions
    {
        /// <summary>Whether evaluating the rendering options fails.</summary>
        private readonly bool _fail;

        /// <summary>The number of rendering option evaluations.</summary>
        private int _reads;

        /// <summary>Initializes a new instance of the <see cref="FactoryOptions"/> class.</summary>
        /// <param name="fail">Whether evaluating the rendering options fails.</param>
        internal FactoryOptions(bool fail) => _fail = fail;

        /// <summary>Gets the number of rendering option evaluations.</summary>
        internal int Reads => Volatile.Read(ref _reads);

        /// <inheritdoc/>
        public override bool TryGetValue(string key, out string value)
        {
            value = string.Empty;
            if (key != "publicapisharp.include_assembly_attributes")
            {
                return false;
            }

            _ = Interlocked.Increment(ref _reads);
            return _fail ? throw new InvalidOperationException("Rendering options cannot be read.") : false;
        }
    }

    /// <summary>Supplies only the optional baseline path.</summary>
    private sealed class BaselineOptions : AnalyzerConfigOptions
    {
        /// <summary>The configured baseline path.</summary>
        private readonly string? _path;

        /// <summary>Initializes a new instance of the <see cref="BaselineOptions"/> class.</summary>
        /// <param name="path">The configured baseline path.</param>
        internal BaselineOptions(string? path) => _path = path;

        /// <inheritdoc/>
        public override bool TryGetValue(string key, out string value)
        {
            value = _path ?? string.Empty;
            return key == PublicApiBaselineAnalyzer.BaselinePathOptionKey && _path is not null;
        }
    }

    /// <summary>A baseline matching a compilation with no externally visible declarations.</summary>
    private sealed class EmptyBaseline : AdditionalText
    {
        /// <summary>The empty baseline text.</summary>
        private readonly SourceText _text = SourceText.From(string.Empty);

        /// <inheritdoc/>
        public override string Path => BaselinePath;

        /// <inheritdoc/>
        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }
}
