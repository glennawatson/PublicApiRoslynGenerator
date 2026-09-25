// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Composition.Hosting;
using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

using PublicApiSharp.Analyzers.CodeFixes;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Tests the code fix for diagnostics that are not located in a source document.</summary>
/// <remarks>
/// PAS0002 is reported on a line of the baseline, an additional document, and PAS0004 has no location
/// at all. The testing package's code fix verifier only applies fixes to source-located diagnostics,
/// so these tests take the analyzer's real diagnostic and drive the provider the way a host does.
/// </remarks>
public class UpdateBaselineOutsideSourceTests
{
    /// <summary>The source every test compiles.</summary>
    private const string Source = """
                                  namespace Sample;

                                  public class Thing
                                  {
                                      public int Value { get; set; }
                                  }
                                  """;

    /// <summary>A baseline that records one member the source no longer has.</summary>
    private const string StaleBaseline = """
                                         namespace Sample;

                                         public class Thing
                                         {
                                             public Thing() { }
                                             public int Value { get; set; }
                                             public int Gone { get; set; }
                                         }

                                         """;

    /// <summary>The path the package's MSBuild targets would resolve for the baseline.</summary>
    private const string BaselinePath = "/Project/PublicAPI/net10.0/PublicAPI.txt";

    /// <summary>The title the provider gives its action.</summary>
    private const string Title = "Update the public API baseline";

    /// <summary>Verifies the provider asks hosts for additional-document diagnostics as well as source ones.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task ProviderIsExportedForAdditionalDocumentsAsync()
    {
        var export = typeof(UpdatePublicApiBaselineCodeFixProvider).GetCustomAttribute<ExportCodeFixProviderAttribute>()!;
        using var container = CreateContainer();
        var provider = container.GetExport<CodeFixProvider>();

        await Assert.That(export.DocumentKinds).Contains(nameof(TextDocumentKind.Document));
        await Assert.That(export.DocumentKinds).Contains(nameof(TextDocumentKind.AdditionalDocument));
        await Assert.That(provider.FixableDiagnosticIds).Contains(PublicApiRules.RemovedId);
        await Assert.That(provider.FixableDiagnosticIds).Contains(PublicApiRules.MissingBaselineId);
    }

    /// <summary>Verifies a removal alone, reported in the baseline file, is fixed by regenerating it.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task RemovedEntryAloneIsDroppedFromTheBaselineAsync()
    {
        using var workspace = await PublicApiVerifier.CreateWorkspaceAsync();
        var projectId = ProjectId.CreateNewId();
        var baselineId = DocumentId.CreateNewId(projectId);
        var project = CreateProject(workspace, projectId, globalConfig: null)
            .AddAdditionalDocument(baselineId, PublicApiVerifier.BaselineFileName, SourceText.From(StaleBaseline), filePath: BaselinePath)
            .GetProject(projectId)!;

        var diagnostics = await AnalyzeAsync(project);
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var removed = diagnostics[0];
        await Assert.That(removed.Id).IsEqualTo(PublicApiRules.RemovedId);
        await Assert.That(removed.Location.Kind).IsEqualTo(LocationKind.ExternalFile);

        var action = await RegisterSingleFixAsync(project.GetAdditionalDocument(baselineId)!, removed);
        var result = await ApplyAsync(action);
        var written = await result.GetAdditionalDocument(baselineId)!.GetTextAsync();

        await Assert.That(written.ToString()).IsEqualTo(ApiSurfaceTestHost.Render(Source));
        await Assert.That(await AnalyzeAsync(result.GetProject(projectId)!)).IsEmpty();
    }

    /// <summary>Verifies a missing baseline is created at the configured path with the rendered surface.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task MissingBaselineIsCreatedAtTheConfiguredPathAsync()
    {
        using var workspace = await PublicApiVerifier.CreateWorkspaceAsync();
        var projectId = ProjectId.CreateNewId();
        var project = CreateProject(
                workspace,
                projectId,
                $"""
                 dotnet_diagnostic.PAS0004.severity = warning
                 build_property.PublicApiBaselineFile = {BaselinePath}
                 build_property.TargetFramework = net10.0
                 """)
            .GetProject(projectId)!;

        var diagnostics = await AnalyzeAsync(project);
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        var missing = diagnostics[0];
        await Assert.That(missing.Id).IsEqualTo(PublicApiRules.MissingBaselineId);
        await Assert.That(missing.Location).IsEqualTo(Location.None);

        var action = await RegisterSingleFixAsync(project.GetDocument(project.DocumentIds[0])!, missing);
        var result = await ApplyAsync(action);
        var fixedProject = result.GetProject(projectId)!;
        await Assert.That(fixedProject.AdditionalDocumentIds.Count).IsEqualTo(1);
        var created = fixedProject.GetAdditionalDocument(fixedProject.AdditionalDocumentIds[0])!;
        var written = await created.GetTextAsync();

        await Assert.That(created.Name).IsEqualTo(PublicApiVerifier.BaselineFileName);
        await Assert.That(created.FilePath).IsEqualTo(BaselinePath);
        await Assert.That(written.ToString()).IsEqualTo(ApiSurfaceTestHost.Render(Source));
        await Assert.That(await AnalyzeAsync(result.GetProject(projectId)!)).IsEmpty();
    }

    /// <summary>Verifies a missing baseline with no configured path leaves the solution unchanged.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task MissingBaselineWithoutAConfiguredPathIsANoOpAsync()
    {
        using var workspace = await PublicApiVerifier.CreateWorkspaceAsync();
        var projectId = ProjectId.CreateNewId();
        var project = CreateProject(workspace, projectId, "build_property.TargetFramework = net10.0").GetProject(projectId)!;
        var missing = Diagnostic.Create(PublicApiRules.MissingBaseline, Location.None, BaselinePath, "net10.0");

        var action = await RegisterSingleFixAsync(project.GetDocument(project.DocumentIds[0])!, missing);
        var result = await ApplyAsync(action);

        await Assert.That(result).IsSameReferenceAs(project.Solution);
        await Assert.That(result.GetProject(projectId)!.AdditionalDocumentIds).IsEmpty();
    }

    /// <summary>Verifies a differently named baseline already at the configured path is not duplicated.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task DifferentlyNamedBaselineAtTheConfiguredPathIsLeftAloneAsync()
    {
        const string CustomPath = "/Project/PublicAPI/net10.0/Api.txt";
        using var workspace = await PublicApiVerifier.CreateWorkspaceAsync();
        var projectId = ProjectId.CreateNewId();
        var project = CreateProject(workspace, projectId, $"build_property.PublicApiBaselineFile = {CustomPath}")
            .AddAdditionalDocument(DocumentId.CreateNewId(projectId), "Api.txt", SourceText.From(StaleBaseline), filePath: CustomPath)
            .AddAdditionalDocument(DocumentId.CreateNewId(projectId), "Notes.txt", SourceText.From(string.Empty), filePath: "/Project/Notes.txt")
            .GetProject(projectId)!;

        var result = await UpdatePublicApiBaselineCodeFixProvider.UpdateBaselineAsync(project, CancellationToken.None);

        await Assert.That(result).IsSameReferenceAs(project.Solution);
        await Assert.That(UpdatePublicApiBaselineCodeFixProvider.FindMissingBaselinePath(project)).IsNull();
    }

    /// <summary>Verifies an empty configured path is treated as no path at all.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task EmptyConfiguredPathIsTreatedAsMissingAsync()
    {
        using var workspace = await PublicApiVerifier.CreateWorkspaceAsync();
        var projectId = ProjectId.CreateNewId();
        var project = CreateProject(workspace, projectId, "build_property.PublicApiBaselineFile =").GetProject(projectId)!;

        await Assert.That(UpdatePublicApiBaselineCodeFixProvider.FindMissingBaselinePath(project)).IsNull();
    }

    /// <summary>Adds a C# project containing <see cref="Source"/> and an optional global config.</summary>
    /// <param name="workspace">The workspace whose solution the project is added to.</param>
    /// <param name="projectId">The project id.</param>
    /// <param name="globalConfig">The global config body, without the header, or <see langword="null"/> for none.</param>
    /// <returns>The solution with the project added.</returns>
    private static Solution CreateProject(Workspace workspace, ProjectId projectId, string? globalConfig)
    {
        var compilation = ApiSurfaceTestHost.Compile(Source);
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Default,
                "OutsideSource",
                "TestAssembly",
                LanguageNames.CSharp,
                filePath: "/Project/OutsideSource.csproj",
                compilationOptions: compilation.Options,
                parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
                metadataReferences: compilation.References))
            .AddDocument(DocumentId.CreateNewId(projectId), "Thing.cs", SourceText.From(Source), filePath: "/Project/Thing.cs");

        return globalConfig is null
            ? solution
            : solution.AddAnalyzerConfigDocument(
                DocumentId.CreateNewId(projectId),
                ".globalconfig",
                SourceText.From($"is_global = true{Environment.NewLine}{globalConfig}"),
                filePath: "/Project/.globalconfig");
    }

    /// <summary>Composes the provider the way a host does, from its export.</summary>
    /// <returns>A container the caller owns and must dispose.</returns>
    private static CompositionHost CreateContainer() =>
        new ContainerConfiguration().WithPart<UpdatePublicApiBaselineCodeFixProvider>().CreateContainer();

    /// <summary>Runs the analyzer over a project the way the host would.</summary>
    /// <param name="project">The project.</param>
    /// <returns>The analyzer's diagnostics.</returns>
    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(Project project)
    {
        var compilation = (await project.GetCompilationAsync())!;
        return await compilation
            .WithAnalyzers([new PublicApiBaselineAnalyzer()], project.AnalyzerOptions)
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None);
    }

    /// <summary>Asks the provider for its fix, as a host does for a diagnostic in the given document.</summary>
    /// <param name="document">The document the host associates with the diagnostic.</param>
    /// <param name="diagnostic">The diagnostic.</param>
    /// <returns>The single registered action.</returns>
    private static async Task<CodeAction> RegisterSingleFixAsync(TextDocument document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (registered, _) => actions.Add(registered), CancellationToken.None);
        using var container = CreateContainer();
        await container.GetExport<CodeFixProvider>().RegisterCodeFixesAsync(context);

        await Assert.That(actions.Count).IsEqualTo(1);
        await Assert.That(actions[0].Title).IsEqualTo(Title);
        return actions[0];
    }

    /// <summary>Applies an action's single change operation without touching the workspace.</summary>
    /// <param name="action">The action.</param>
    /// <returns>The changed solution.</returns>
    private static async Task<Solution> ApplyAsync(CodeAction action)
    {
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        await Assert.That(operations.Length).IsEqualTo(1);
        var changes = await Assert.That(operations[0]).IsTypeOf<ApplyChangesOperation>();
        return changes!.ChangedSolution;
    }
}
