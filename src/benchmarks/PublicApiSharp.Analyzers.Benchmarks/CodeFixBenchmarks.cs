// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Composition.Hosting;
using System.Runtime.CompilerServices;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;

using PublicApiSharp.Analyzers.CodeFixes;

namespace PublicApiSharp.Analyzers.Benchmarks;

/// <summary>Measures accepting an API change, which regenerates the whole baseline document.</summary>
/// <remarks>
/// This is the interactive half: it runs when someone takes the lightbulb, so it is felt directly
/// rather than absorbed into a build. One edit resolves every diagnostic in the project, so the cost
/// is paid once however many declarations changed.
/// </remarks>
[ShortRunJob]
[MemoryDiagnoser]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class CodeFixBenchmarks : IDisposable
{
    /// <summary>The workspace holding the project under fix.</summary>
    private AdhocWorkspace _workspace = null!;

    /// <summary>The project whose baseline is regenerated.</summary>
    private Project _project = null!;

    /// <summary>The provider whose registration entry point is measured.</summary>
    private UpdatePublicApiBaselineCodeFixProvider _provider = null!;

    /// <summary>A real added-property diagnostic and its registration callback.</summary>
    private CodeFixContext _context;

    /// <summary>The registered baseline action, prepared outside application measurements.</summary>
    private CodeAction _action = null!;

    /// <summary>The project-wide fix-all context.</summary>
    private FixAllContext _fixAllContext = null!;

    /// <summary>The fix-all provider returned by the code fix.</summary>
    private FixAllProvider _fixAllProvider = null!;

    /// <summary>The composition scope owning the shared code fix provider.</summary>
    private CompositionHost _composition = null!;

    /// <summary>Gets or sets the number of public types the project declares.</summary>
    [Params(BenchmarkParameterValues.SmallTypeCount, BenchmarkParameterValues.LargeTypeCount)]
    public int TypeCount { get; set; }

    /// <summary>Builds a project with a source file and a baseline additional document.</summary>
    /// <returns>A task completing when the project's compilation and action are ready.</returns>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _workspace = new();

        var compilation = BenchmarkWorkload.Scaled(TypeCount);
        var source = compilation.SyntaxTrees.First().ToString();
        var baseline = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None).Text;

        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        CSharpCompilationOptions compilationOptions = new(OutputKind.DynamicallyLinkedLibrary);

        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Default,
            "Benchmark",
            "Benchmark",
            LanguageNames.CSharp,
            compilationOptions: compilationOptions,
            metadataReferences: BenchmarkWorkload.BuildReferences());

        var solution = _workspace.CurrentSolution
            .AddProject(projectInfo)
            .AddDocument(documentId, "Source.cs", SourceText.From(source))
            .AddAdditionalDocument(
                DocumentId.CreateNewId(projectId),
                PublicApiBaselineAnalyzer.BaselineFileName,
                SourceText.From(BenchmarkWorkload.RemoveOneProperty(baseline)));

        _project = solution.GetProject(projectId)!;
        _composition = new ContainerConfiguration().WithPart<UpdatePublicApiBaselineCodeFixProvider>().CreateContainer();
        _provider = (UpdatePublicApiBaselineCodeFixProvider)_composition.GetExport<CodeFixProvider>();
        var document = _project.GetDocument(documentId)!;
        var current = (await _project.GetCompilationAsync(CancellationToken.None).ConfigureAwait(false))!;
        var property = BenchmarkWorkload.Member(BenchmarkWorkload.Type(current, "Sample.Thing0"), "Value");
        var diagnostic = Diagnostic.Create(PublicApiRules.Added, property.Locations[0], "Value");
        _context = new(document, diagnostic, (action, _) => _action = action, CancellationToken.None);
        await _provider.RegisterCodeFixesAsync(_context).ConfigureAwait(false);
        _fixAllProvider = _provider.GetFixAllProvider();
        _fixAllContext = new(
            document,
            _provider,
            FixAllScope.Project,
            _action.EquivalenceKey,
            _provider.FixableDiagnosticIds,
            new EmptyDiagnosticProvider(),
            CancellationToken.None);
    }

    /// <summary>Regenerates the baseline document from the project's current surface.</summary>
    /// <returns>Whether a solution came back, so the work cannot be optimized away.</returns>
    [Benchmark]
    public async Task<bool> UpdateBaselineAsync()
    {
        var solution = await UpdatePublicApiBaselineCodeFixProvider
            .UpdateBaselineAsync(_project, CancellationToken.None)
            .ConfigureAwait(false);
        return solution is not null;
    }

    /// <summary>Finds the baseline among the project's additional documents.</summary>
    /// <returns>Whether one was found, so the work cannot be optimized away.</returns>
    [Benchmark]
    public bool FindBaselineDocument() =>
        UpdatePublicApiBaselineCodeFixProvider.FindBaselineDocument(_project) is not null;

    /// <summary>Registers the action for one added-property diagnostic.</summary>
    /// <returns>The completed registration task.</returns>
    [Benchmark]
    public Task RegisterCodeFixesAsync() => _provider.RegisterCodeFixesAsync(_context);

    /// <summary>Constructs the provider used for a project-wide fix.</summary>
    /// <returns>The new fix-all provider.</returns>
    [Benchmark]
    public object CreateFixAllProvider() => _provider.GetFixAllProvider();

    /// <summary>Creates the single baseline action returned by fix-all registration.</summary>
    /// <returns>The baseline action.</returns>
    [Benchmark]
    public Task<CodeAction?> RegisterFixAllAsync() => _fixAllProvider.GetFixAsync(_fixAllContext);

    /// <summary>Applies the registered action through Roslyn's public operation API.</summary>
    /// <returns>The number of operations produced.</returns>
    [Benchmark]
    public async Task<int> ApplyCodeActionAsync()
    {
        var operations = await _action.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
        return operations.Length;
    }

    /// <summary>Releases the workspace once the parameter set is done.</summary>
    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>Releases the workspace.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the workspace this benchmark owns.</summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        _workspace?.Dispose();
        _composition?.Dispose();
    }

    /// <summary>Supplies the unused diagnostic service required by the fix-all context.</summary>
    private sealed class EmptyDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        /// <inheritdoc/>
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>([]);

        /// <inheritdoc/>
        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>([]);

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            GetProjectDiagnosticsAsync(project, cancellationToken);
    }
}
