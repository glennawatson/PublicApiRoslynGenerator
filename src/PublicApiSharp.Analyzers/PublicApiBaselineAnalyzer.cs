// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Threading;

namespace PublicApiSharp.Analyzers;

/// <summary>
/// Compares the compilation's externally visible surface against the checked-in baseline for the
/// target framework being built, and reports every difference.
/// </summary>
/// <remarks>
/// <para>
/// Reports <see cref="PublicApiRules.Added"/> (PAS0001), <see cref="PublicApiRules.Removed"/>
/// (PAS0002), <see cref="PublicApiRules.Changed"/> (PAS0003),
/// <see cref="PublicApiRules.MissingBaseline"/> (PAS0004) and
/// <see cref="PublicApiRules.UnreadableBaseline"/> (PAS0005).
/// </para>
/// <para>
/// There is one baseline per target framework and no shipped/unshipped split: the baseline states
/// what the assembly exposes right now. A change to the surface is accepted by updating the file in
/// the same commit that makes the change, so the diff a reviewer reads is the API change itself
/// rather than a later promotion step.
/// </para>
/// <para>
/// Additions and changes are reported from a <em>symbol</em> action, not at compilation end. A
/// diagnostic raised by a compilation action is not local to any document, and Roslyn refuses to
/// offer a code fix for one — so reporting them that way would silently cost the lightbulb that
/// accepts the change. Removals have no symbol left to point at and are reported at compilation end,
/// against the baseline line a reviewer has to agree to delete.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublicApiBaselineAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The MSBuild property, made compiler-visible by the package, holding the baseline path.</summary>
    internal const string BaselinePathOptionKey = "build_property.PublicApiBaselineFile";

    /// <summary>The MSBuild property holding the target framework the baseline describes.</summary>
    internal const string TargetFrameworkOptionKey = "build_property.TargetFramework";

    /// <summary>The file name a baseline uses, under a per-target-framework folder.</summary>
    internal const string BaselineFileName = "PublicAPI.txt";

    /// <summary>Caches the parse of a baseline so an unchanged file is only read once.</summary>
    private static readonly SourceTextValueProvider<ApiTextParseResult> BaselineProvider =
        new(static text => ApiTextParser.Parse(text, CancellationToken.None));

    /// <summary>The symbol kinds that can produce a declaration in the surface.</summary>
    private static readonly ImmutableArray<SymbolKind> TrackedSymbolKinds = ImmutableArrays.Of(
        SymbolKind.NamedType,
        SymbolKind.Method,
        SymbolKind.Property,
        SymbolKind.Field,
        SymbolKind.Event);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArrays.Of(
        PublicApiRules.Added,
        PublicApiRules.Removed,
        PublicApiRules.Changed,
        PublicApiRules.MissingBaseline,
        PublicApiRules.UnreadableBaseline);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();

        // A source generator's output is as public as hand-written code, so generated declarations
        // belong in the baseline and must be analyzed.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    /// <summary>Runs the two rules that can only be decided once the whole compilation is known.</summary>
    /// <param name="context">The compilation context.</param>
    /// <param name="state">The shared comparison state.</param>
    /// <param name="baselineFile">The baseline file.</param>
    /// <param name="baselineText">The baseline file's text.</param>
    /// <remarks>
    /// Both rules need the whole comparison, so it is resolved once here. Nothing is reported
    /// without it, which happens only when this package rendered a surface it could not read back —
    /// its own defect rather than anything the consumer can act on.
    /// </remarks>
    internal static void ReportAtCompilationEnd(
        in CompilationAnalysisContext context,
        Lazy<ApiComparisonState?> state,
        AdditionalText baselineFile,
        SourceText baselineText)
    {
        if (state.Value is not { } comparison)
        {
            return;
        }

        ReportImplicitAdditions(in context, comparison);
        ReportRemoved(in context, comparison, baselineFile, baselineText);
    }

    /// <summary>Finds where in the user's source a diagnostic about a symbol should be reported.</summary>
    /// <param name="symbol">The symbol.</param>
    /// <returns>The location.</returns>
    internal static Location SymbolLocation(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource)
            {
                return location;
            }
        }

        // An implicit constructor has no source location of its own; the type that declares it is
        // where a reader would go to act on the diagnostic.
        return symbol.ContainingType is { } containingType ? SymbolLocation(containingType) : Location.None;
    }

    /// <summary>Resolves the baseline once, then wires up the per-symbol and end-of-compilation rules.</summary>
    /// <param name="context">The compilation start context.</param>
    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var globalOptions = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        _ = globalOptions.TryGetValue(BaselinePathOptionKey, out var baselinePath);

        var baselineFile = FindBaseline(context.Options.AdditionalFiles, baselinePath);
        if (baselineFile is null)
        {
            // Nothing to compare against. Say so once rather than reporting every member in the
            // assembly as newly added.
            context.RegisterCompilationEndAction(endContext => ReportMissingBaseline(in endContext, globalOptions, baselinePath));
            return;
        }

        var baselineText = baselineFile.GetText(context.CancellationToken);
        if (baselineText is null || !context.TryGetValue(baselineText, BaselineProvider, out var baselineParse))
        {
            return;
        }

        if (!baselineParse.Success)
        {
            context.RegisterCompilationEndAction(endContext => endContext.ReportDiagnostic(Diagnostic.Create(
                PublicApiRules.UnreadableBaseline,
                BaselineLocation(baselineFile, baselineText, baselineParse.ErrorSpan),
                baselineParse.Error)));
            return;
        }

        var options = ApiRenderOptions.Read(globalOptions);
        var compilation = context.Compilation;

        // Rendering the whole surface is compilation-wide work, so it happens once, on first use,
        // and every symbol callback then costs a dictionary lookup.
        var state = new Lazy<ApiComparisonState?>(
            () => ApiComparisonState.Create(compilation, baselineParse, options, CancellationToken.None));

        context.RegisterSymbolAction(symbolContext => ReportForSymbol(in symbolContext, state), TrackedSymbolKinds);
        context.RegisterCompilationEndAction(
            endContext => ReportAtCompilationEnd(in endContext, state, baselineFile, baselineText));
    }

    /// <summary>Reports a symbol that the baseline does not have, or has differently.</summary>
    /// <param name="context">The symbol context.</param>
    /// <param name="state">The shared comparison state.</param>
    private static void ReportForSymbol(in SymbolAnalysisContext context, Lazy<ApiComparisonState?> state)
    {
        if (state.Value is not { } comparison
            || !comparison.DeclarationsBySymbol.TryGetValue(context.Symbol, out var current))
        {
            // Not part of the surface, so there is nothing the baseline should be saying about it.
            return;
        }

        if (!comparison.BaselineByIdentity.TryGetValue(current.Identity, out var declared))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PublicApiRules.Added,
                SymbolLocation(context.Symbol),
                FinalLine(current.Text)));
            return;
        }

        if (string.Equals(declared.Text, current.Text, StringComparison.Ordinal))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            PublicApiRules.Changed,
            SymbolLocation(context.Symbol),
            FinalLine(current.Text),
            Flatten(declared.Text),
            Flatten(current.Text)));
    }

    /// <summary>Reports surface entries whose symbol a symbol action can never see.</summary>
    /// <param name="context">The compilation context.</param>
    /// <param name="comparison">The resolved comparison.</param>
    /// <remarks>
    /// Roslyn does not raise symbol actions for compiler-supplied symbols, so an implicit
    /// constructor would otherwise be written into the baseline yet never reported when it appears.
    /// It appears whenever a class loses its last explicit constructor, which is a real change to
    /// what a consumer can call, so it has to be caught somewhere — here, at compilation end.
    /// </remarks>
    private static void ReportImplicitAdditions(in CompilationAnalysisContext context, ApiComparisonState comparison)
    {
        foreach (var pair in comparison.DeclarationsBySymbol)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (pair.Key.IsImplicitlyDeclared && !comparison.BaselineByIdentity.ContainsKey(pair.Value.Identity))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PublicApiRules.Added,
                    SymbolLocation(pair.Key),
                    FinalLine(pair.Value.Text)));
            }
        }
    }

    /// <summary>Reports baseline declarations the compilation no longer exposes.</summary>
    /// <param name="context">The compilation context.</param>
    /// <param name="comparison">The resolved comparison.</param>
    /// <param name="baselineFile">The baseline file.</param>
    /// <param name="baselineText">The baseline text.</param>
    private static void ReportRemoved(
        in CompilationAnalysisContext context,
        ApiComparisonState comparison,
        AdditionalText baselineFile,
        SourceText baselineText)
    {
        foreach (var declared in comparison.BaselineByIdentity)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!comparison.CurrentByIdentity.ContainsKey(declared.Key))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PublicApiRules.Removed,
                    BaselineLocation(baselineFile, baselineText, declared.Value.Span),
                    Flatten(declared.Value.Text)));
            }
        }
    }

    /// <summary>Reports that the target framework being built has no baseline.</summary>
    /// <param name="context">The compilation context.</param>
    /// <param name="globalOptions">The compilation-wide analyzer config options.</param>
    /// <param name="baselinePath">The path a baseline was expected at, if the project resolved one.</param>
    private static void ReportMissingBaseline(
        in CompilationAnalysisContext context,
        AnalyzerConfigOptions globalOptions,
        string? baselinePath)
    {
        if (string.IsNullOrEmpty(baselinePath))
        {
            // The consuming project has not opted in (the package's MSBuild targets did not resolve
            // a path), so public API tracking is simply not in use here.
            return;
        }

        _ = globalOptions.TryGetValue(TargetFrameworkOptionKey, out var targetFramework);
        context.ReportDiagnostic(Diagnostic.Create(
            PublicApiRules.MissingBaseline,
            Location.None,
            baselinePath,
            string.IsNullOrEmpty(targetFramework) ? "(unknown)" : targetFramework));
    }

    /// <summary>
    /// Finds the baseline among the additional files. The resolved MSBuild path is preferred; the
    /// file-name fallback keeps the analyzer working when a project wires the additional file up by
    /// hand instead of through the package's targets.
    /// </summary>
    /// <param name="additionalFiles">The compilation's additional files.</param>
    /// <param name="baselinePath">The resolved baseline path, if the project has one.</param>
    /// <returns>The baseline file, or <see langword="null"/> when there is none.</returns>
    private static AdditionalText? FindBaseline(ImmutableArray<AdditionalText> additionalFiles, string? baselinePath)
    {
        AdditionalText? byName = null;

        foreach (var file in additionalFiles)
        {
            if (!string.IsNullOrEmpty(baselinePath)
                && string.Equals(file.Path, baselinePath, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }

            if (byName is null && EndsWithFileName(file.Path))
            {
                byName = file;
            }
        }

        return byName;
    }

    /// <summary>Determines whether a path's final segment is the baseline file name.</summary>
    /// <param name="path">The path.</param>
    /// <returns><see langword="true"/> when the path names a baseline file.</returns>
    private static bool EndsWithFileName(string path)
    {
        if (path.Length < BaselineFileName.Length
            || !path.EndsWith(BaselineFileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (path.Length == BaselineFileName.Length)
        {
            return true;
        }

        var separator = path[path.Length - BaselineFileName.Length - 1];
        return separator is '/' or '\\';
    }

    /// <summary>Builds a location inside the baseline file.</summary>
    /// <param name="baseline">The baseline file.</param>
    /// <param name="text">The baseline text.</param>
    /// <param name="span">The span within the text.</param>
    /// <returns>The location.</returns>
    private static Location BaselineLocation(AdditionalText baseline, SourceText text, TextSpan span)
    {
        var clamped = span.End <= text.Length ? span : new TextSpan(0, 0);
        return Location.Create(baseline.Path, clamped, text.Lines.GetLinePositionSpan(clamped));
    }

    /// <summary>Takes the declaration line itself, without any attributes that precede it.</summary>
    /// <param name="text">The declaration text.</param>
    /// <returns>The final line.</returns>
    private static string FinalLine(string text)
    {
        var lastNewLine = text.LastIndexOf('\n');
        return lastNewLine >= 0 ? text.Substring(lastNewLine + 1) : text;
    }

    /// <summary>Collapses a multi-line declaration onto one line so it fits in a diagnostic message.</summary>
    /// <param name="text">The declaration text.</param>
    /// <returns>The flattened text.</returns>
    private static string Flatten(string text) => text.Replace('\n', ' ');
}
