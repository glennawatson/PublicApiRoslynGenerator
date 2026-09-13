// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Composition.Hosting;
using System.Runtime.CompilerServices;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;

using PublicApiSharp.Analyzers.CodeFixes;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Tests the deferred work and identity of ordinary and project-wide baseline actions.</summary>
public class UpdatePublicApiBaselineCodeActionTests
{
    /// <summary>Verifies registration stays lazy and application writes the complete baseline.</summary>
    /// <param name="fixAll">Whether to request the project-wide action.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RegisteredActionDefersCompilationAndPreservesItsIdentityAsync(bool fixAll)
    {
        const string Source = "public interface Current { }";
        const string Title = "Update the public API baseline";
        var compilation = ApiSurfaceTestHost.Compile(Source);
        using var workspace = await PublicApiVerifier.CreateWorkspaceAsync();
        var projectId = ProjectId.CreateNewId();
        var sourceId = DocumentId.CreateNewId(projectId);
        var baselineId = DocumentId.CreateNewId(projectId);
        var project = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(projectId, VersionStamp.Default, "DeferredFix", "DeferredFix", LanguageNames.CSharp, metadataReferences: compilation.References))
            .AddDocument(sourceId, "Source.cs", SourceText.From(Source))
            .AddAdditionalDocument(baselineId, PublicApiVerifier.BaselineFileName, SourceText.From("public interface Stale { }"))
            .GetProject(projectId)!;
        var document = project.GetDocument(sourceId)!;
        using var container = new ContainerConfiguration().WithPart<UpdatePublicApiBaselineCodeFixProvider>().CreateContainer();
        var provider = container.GetExport<CodeFixProvider>();
        CodeAction action;
        if (fixAll)
        {
            var context = new FixAllContext(document, provider, FixAllScope.Project, Title, provider.FixableDiagnosticIds, new EmptyDiagnostics(), CancellationToken.None);
            action = (await provider.GetFixAllProvider()!.GetFixAsync(context))!;
        }
        else
        {
            var diagnostic = Diagnostic.Create(PublicApiRules.Added, Location.Create((await document.GetSyntaxTreeAsync())!, default), "public interface Current");
            var actions = new List<CodeAction>();
            var context = new CodeFixContext(document, diagnostic, (registered, _) => actions.Add(registered), CancellationToken.None);
            await provider.RegisterCodeFixesAsync(context);
            await Assert.That(actions.Count).IsEqualTo(1);
            action = actions[0];
        }

        await Assert.That(action.Title).IsEqualTo(Title);
        await Assert.That(action.EquivalenceKey).IsEqualTo(Title);
        await Assert.That(project.TryGetCompilation(out _)).IsFalse();

        var operations = await action.GetOperationsAsync(CancellationToken.None);
        await Assert.That(operations.Length).IsEqualTo(1);
        var changes = await Assert.That(operations[0]).IsTypeOf<ApplyChangesOperation>();
        var result = changes!.ChangedSolution;
        var baseline = await result.GetAdditionalDocument(baselineId)!.GetTextAsync();
        var source = await result.GetDocument(sourceId)!.GetTextAsync();

        await Assert.That(baseline.ToString()).IsEqualTo(ApiSurfaceTestHost.Render(Source));
        await Assert.That(source.ToString()).IsEqualTo(Source);
        await Assert.That(workspace.CurrentSolution.ProjectIds).IsEmpty();
    }

    /// <summary>Provides an empty diagnostic set because the rewrite uses the project's surface.</summary>
    private sealed class EmptyDiagnostics : FixAllContext.DiagnosticProvider
    {
        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            GetProjectDiagnosticsAsync(document.Project, cancellationToken);

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>([]);

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) => GetProjectDiagnosticsAsync(project, cancellationToken);
    }
}
