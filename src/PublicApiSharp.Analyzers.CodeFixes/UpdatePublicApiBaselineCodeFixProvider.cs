// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiSharp.Analyzers.CodeFixes;

/// <summary>Rewrites the public API baseline so it states what the assembly currently exposes.</summary>
/// <remarks>
/// <para>
/// Fixes <see cref="PublicApiRules.AddedId"/> (PAS0001),
/// <see cref="PublicApiRules.RemovedId"/> (PAS0002),
/// <see cref="PublicApiRules.ChangedId"/> (PAS0003) and
/// <see cref="PublicApiRules.MissingBaselineId"/> (PAS0004).
/// </para>
/// <para>
/// Only PAS0001 and PAS0003 sit in a source document. PAS0002 is reported on a line of the baseline,
/// which is an additional document, and PAS0004 has no location at all, so the project is taken from
/// <see cref="CodeFixContext.TextDocument"/> rather than <see cref="CodeFixContext.Document"/>, which
/// throws for anything that is not a source document.
/// </para>
/// <para>
/// There is no shipped/unshipped split to promote between, so accepting an API change is a single
/// action: regenerate the file. Because one edit resolves every diagnostic in the project at once,
/// the fix-all provider deliberately does the work once per project rather than once per diagnostic
/// — batching the same whole-file rewrite N times would be N identical edits racing each other.
/// </para>
/// </remarks>
[ExportCodeFixProvider(
    LanguageNames.CSharp,
    Name = nameof(UpdatePublicApiBaselineCodeFixProvider),
    DocumentKinds = [nameof(TextDocumentKind.Document), nameof(TextDocumentKind.AdditionalDocument)])]
[Shared]
public sealed class UpdatePublicApiBaselineCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArrays.Of(
        PublicApiRules.AddedId,
        PublicApiRules.RemovedId,
        PublicApiRules.ChangedId,
        PublicApiRules.MissingBaselineId);

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => new UpdateBaselineFixAllProvider();

    /// <inheritdoc/>
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            context.RegisterCodeFix(new UpdateBaselineCodeAction(context.TextDocument.Project), diagnostic);
        }

        return Task.CompletedTask;
    }

    /// <summary>Writes the freshly rendered surface to the project's baseline, creating it when it is missing.</summary>
    /// <param name="project">The project whose baseline is being updated.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The updated solution, or the original when there is nothing to write to.</returns>
    /// <remarks>
    /// A project without a baseline document gets one at the path the package's MSBuild targets
    /// resolved (<see cref="PublicApiBaselineAnalyzer.BaselinePathOptionKey"/>), which is where the
    /// analyzer looks for it on the next build. Without a resolved path there is nowhere to put it,
    /// so the project is left alone.
    /// </remarks>
    internal static async Task<Solution> UpdateBaselineAsync(Project project, CancellationToken cancellationToken)
    {
        var baseline = FindBaselineDocument(project);
        var missingBaselinePath = baseline is null ? FindMissingBaselinePath(project) : null;
        if (baseline is null && missingBaselinePath is null)
        {
            return project.Solution;
        }

        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        if (compilation is null)
        {
            return project.Solution;
        }

        var options = ApiRenderOptions.Read(project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions);
        var surface = ApiSurfaceRenderer.Render(compilation, options, cancellationToken);
        var text = SourceText.From(surface.Text);
        return baseline is not null
            ? project.Solution.WithAdditionalDocumentText(baseline.Id, text)
            : project.Solution.AddAdditionalDocument(
                DocumentId.CreateNewId(project.Id, PublicApiBaselineAnalyzer.BaselineFileName),
                PublicApiBaselineAnalyzer.BaselineFileName,
                text,
                folders: null,
                filePath: missingBaselinePath);
    }

    /// <summary>Gets the configured path a missing baseline should be created at.</summary>
    /// <param name="project">The project, which has no additional document named <c>PublicAPI.txt</c>.</param>
    /// <returns>
    /// The configured baseline path, or <see langword="null"/> when none is configured or an
    /// additional document already sits at it. A differently named baseline at the configured path
    /// is edited by hand, not duplicated.
    /// </returns>
    internal static string? FindMissingBaselinePath(Project project)
    {
        var globalOptions = project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
        if (!globalOptions.TryGetValue(PublicApiBaselineAnalyzer.BaselinePathOptionKey, out var path)
            || string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var document in project.AdditionalDocuments)
        {
            if (string.Equals(document.FilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return path;
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

    /// <summary>Carries the project until the baseline rewrite is requested.</summary>
    private sealed class UpdateBaselineCodeAction : CodeAction
    {
        /// <summary>The project whose baseline will be rewritten.</summary>
        private readonly Project _project;

        /// <summary>Initializes a new instance of the <see cref="UpdateBaselineCodeAction"/> class.</summary>
        /// <param name="project">The project whose baseline will be rewritten.</param>
        internal UpdateBaselineCodeAction(Project project)
        {
            _project = project;
        }

        /// <inheritdoc/>
        public override string Title => "Update the public API baseline";

        /// <inheritdoc/>
        public override string EquivalenceKey => Title;

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override Task<Solution?> GetChangedSolutionAsync(CancellationToken cancellationToken) => UpdateBaselineAsync(_project, cancellationToken)!;
    }

    /// <summary>Applies the whole-file rewrite once per project, however many diagnostics ask for it.</summary>
    private sealed class UpdateBaselineFixAllProvider : FixAllProvider
    {
        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext) =>
            Task.FromResult<CodeAction?>(new UpdateBaselineCodeAction(fixAllContext.Project));
    }
}
