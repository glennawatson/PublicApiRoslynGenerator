// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class CodeFixBenchmarks : IDisposable
{
    /// <summary>The workspace holding the project under fix.</summary>
    private AdhocWorkspace _workspace = null!;

    /// <summary>The project whose baseline is regenerated.</summary>
    private Project _project = null!;

    /// <summary>Gets or sets the number of public types the project declares.</summary>
    [Params(BenchmarkParameterValues.SmallTypeCount, BenchmarkParameterValues.LargeTypeCount)]
    public int TypeCount { get; set; }

    /// <summary>Builds a project with a source file and a baseline additional document.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _workspace = new();

        var compilation = BenchmarkWorkload.Scaled(TypeCount);
        var source = compilation.SyntaxTrees.First().ToString();
        var baseline = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None).Text;

        var projectId = ProjectId.CreateNewId();
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
            .AddDocument(DocumentId.CreateNewId(projectId), "Source.cs", SourceText.From(source))
            .AddAdditionalDocument(
                DocumentId.CreateNewId(projectId),
                PublicApiBaselineAnalyzer.BaselineFileName,
                SourceText.From(baseline));

        _project = solution.GetProject(projectId)!;
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
    }
}
