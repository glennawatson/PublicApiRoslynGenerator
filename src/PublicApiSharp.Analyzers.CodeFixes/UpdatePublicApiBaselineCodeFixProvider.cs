// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiSharp.Analyzers.CodeFixes;

/// <summary>Rewrites the public API baseline so it states what the assembly currently exposes.</summary>
/// <remarks>
/// <para>
/// Fixes <see cref="PublicApiRules.AddedId"/> (PAS0001),
/// <see cref="PublicApiRules.RemovedId"/> (PAS0002) and
/// <see cref="PublicApiRules.ChangedId"/> (PAS0003).
/// </para>
/// <para>
/// There is no shipped/unshipped split to promote between, so accepting an API change is a single
/// action: regenerate the file. Because one edit resolves every diagnostic in the project at once,
/// the fix-all provider deliberately does the work once per project rather than once per diagnostic
/// — batching the same whole-file rewrite N times would be N identical edits racing each other.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UpdatePublicApiBaselineCodeFixProvider))]
[Shared]
public sealed class UpdatePublicApiBaselineCodeFixProvider : CodeFixProvider
{
    /// <summary>The title shown in the lightbulb, and the equivalence key that groups the fix.</summary>
    private const string Title = "Update the public API baseline";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArrays.Of(
        PublicApiRules.AddedId,
        PublicApiRules.ChangedId);

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => new UpdateBaselineFixAllProvider();

    /// <inheritdoc/>
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    cancellationToken => UpdateBaselineAsync(context.Document.Project, cancellationToken),
                    equivalenceKey: Title),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    /// <summary>Rewrites the project's baseline document with the freshly rendered surface.</summary>
    /// <param name="project">The project whose baseline is being updated.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The updated solution, or the original when there is nothing to write to.</returns>
    internal static async Task<Solution> UpdateBaselineAsync(Project project, CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is null)
        {
            return project.Solution;
        }

        var baseline = FindBaselineDocument(project);
        if (baseline is null)
        {
            return project.Solution;
        }

        var options = ApiRenderOptions.Read(project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions);
        var surface = ApiSurfaceRenderer.Render(compilation, options, cancellationToken);
        return project.Solution.WithAdditionalDocumentText(baseline.Id, SourceText.From(surface.Text));
    }

    /// <summary>Finds the baseline among the project's additional documents.</summary>
    /// <param name="project">The project.</param>
    /// <returns>The baseline document, or <see langword="null"/>.</returns>
    internal static TextDocument? FindBaselineDocument(Project project)
    {
        foreach (var document in project.AdditionalDocuments)
        {
            if (string.Equals(document.Name, PublicApiBaselineAnalyzer.BaselineFileName, StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }
        }

        return null;
    }

    /// <summary>Applies the whole-file rewrite once per project, however many diagnostics ask for it.</summary>
    private sealed class UpdateBaselineFixAllProvider : FixAllProvider
    {
        /// <inheritdoc/>
        public override Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
        {
            var project = fixAllContext.Project;
            return Task.FromResult<CodeAction?>(CodeAction.Create(
                Title,
                cancellationToken => UpdateBaselineAsync(project, cancellationToken),
                equivalenceKey: Title));
        }
    }
}
