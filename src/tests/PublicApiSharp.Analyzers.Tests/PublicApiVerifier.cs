// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

using PublicApiSharp.Analyzers.CodeFixes;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Runs the baseline analyzer, and its code fix, against a source plus a baseline file.</summary>
/// <remarks>
/// This is deliberately concrete rather than the generic verifier facade the testing package ships:
/// there is one analyzer and one code fix here, so naming them directly removes a layer and lets the
/// helpers take the thing every test actually varies — the baseline file's contents.
/// </remarks>
internal static class PublicApiVerifier
{
    /// <summary>The name the analyzer and the code fix both look for.</summary>
    internal const string BaselineFileName = "PublicAPI.txt";

    /// <summary>
    /// Compiler diagnostics for nullable reference types, promoted to errors so the test framework
    /// validates them. It only checks errors by default, and the renderer's output depends on
    /// nullability being switched on.
    /// </summary>
    private static readonly ImmutableDictionary<string, ReportDiagnostic> NullableWarnings = BuildNullableWarnings();

    /// <summary>Builds an expected diagnostic for one of this package's rules.</summary>
    /// <param name="descriptor">The rule.</param>
    /// <returns>The expected diagnostic.</returns>
    internal static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor) => new(descriptor);

    /// <summary>Runs the analyzer against a source and a baseline.</summary>
    /// <param name="source">The C# source, with diagnostic markup.</param>
    /// <param name="baseline">The baseline file's contents.</param>
    /// <param name="expected">Diagnostics expected in addition to any markup in the source.</param>
    /// <returns>A task that represents the asynchronous verification operation.</returns>
    internal static async Task AnalyzeAsync(string source, string baseline, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<PublicApiBaselineAnalyzer, DefaultVerifier> { TestCode = source };
        test.TestState.AdditionalFiles.Add((BaselineFileName, baseline));
        test.ExpectedDiagnostics.AddRange(expected);
        PromoteNullableWarnings(test.SolutionTransforms);
        await test.RunAsync(CancellationToken.None);
    }

    /// <summary>Runs the analyzer with no baseline file present at all.</summary>
    /// <param name="source">The C# source, with diagnostic markup.</param>
    /// <param name="expected">Diagnostics expected in addition to any markup in the source.</param>
    /// <returns>A task that represents the asynchronous verification operation.</returns>
    internal static async Task AnalyzeWithoutBaselineAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<PublicApiBaselineAnalyzer, DefaultVerifier> { TestCode = source };
        test.ExpectedDiagnostics.AddRange(expected);
        PromoteNullableWarnings(test.SolutionTransforms);
        await test.RunAsync(CancellationToken.None);
    }

    /// <summary>Runs the analyzer with a global config and an optionally differently-named baseline.</summary>
    /// <param name="source">The C# source, with diagnostic markup.</param>
    /// <param name="baseline">The baseline file's contents, or <see langword="null"/> for no baseline at all.</param>
    /// <param name="baselineFileName">The name to give the baseline file.</param>
    /// <param name="globalConfig">The contents of a global analyzer config, without the header.</param>
    /// <param name="expected">Diagnostics expected in addition to any markup in the source.</param>
    /// <returns>A task that represents the asynchronous verification operation.</returns>
    internal static async Task AnalyzeWithConfigAsync(
        string source,
        string? baseline,
        string baselineFileName,
        string globalConfig,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<PublicApiBaselineAnalyzer, DefaultVerifier> { TestCode = source };

        if (baseline is not null)
        {
            test.TestState.AdditionalFiles.Add((baselineFileName, baseline));
        }

        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", $"is_global = true{Environment.NewLine}{globalConfig}"));
        test.ExpectedDiagnostics.AddRange(expected);
        PromoteNullableWarnings(test.SolutionTransforms);
        await test.RunAsync(CancellationToken.None);
    }

    /// <summary>Runs the code fix and asserts the baseline it rewrites.</summary>
    /// <param name="source">The C# source, with diagnostic markup.</param>
    /// <param name="fixedSource">The same source without markup: the fix never edits C#, only the baseline.</param>
    /// <param name="baseline">The baseline file's contents before the fix.</param>
    /// <param name="fixedBaseline">The baseline file's contents the fix should produce.</param>
    /// <returns>A task that represents the asynchronous verification operation.</returns>
    /// <param name="expected">Diagnostics expected against the baseline file, which carries no markup.</param>
    internal static async Task FixAsync(
        string source,
        string fixedSource,
        string baseline,
        string fixedBaseline,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpCodeFixTest<PublicApiBaselineAnalyzer, UpdatePublicApiBaselineCodeFixProvider, DefaultVerifier> { TestCode = source, FixedCode = fixedSource };

        test.TestState.AdditionalFiles.Add((BaselineFileName, baseline));
        test.TestState.ExpectedDiagnostics.AddRange(expected);

        // Regenerating the baseline resolves the diagnostics raised against it, so the fixed state
        // starts from nothing rather than inheriting them.
        test.FixedState.InheritanceMode = StateInheritanceMode.Explicit;
        test.FixedState.AdditionalFiles.Add((BaselineFileName, fixedBaseline));
        PromoteNullableWarnings(test.SolutionTransforms);
        await test.RunAsync(CancellationToken.None);
    }

    /// <summary>Adds the transform that switches nullable compiler warnings on for a test project.</summary>
    /// <param name="transforms">The test's solution transform list.</param>
    private static void PromoteNullableWarnings(List<Func<Solution, ProjectId, Solution>> transforms) =>
        transforms.Add(static (solution, projectId) =>
        {
            var options = (CSharpCompilationOptions)solution.GetProject(projectId)!.CompilationOptions!;
            return solution.WithProjectCompilationOptions(
                projectId,
                options.WithNullableContextOptions(NullableContextOptions.Enable)
                    .WithSpecificDiagnosticOptions(options.SpecificDiagnosticOptions.SetItems(NullableWarnings)));
        });

    /// <summary>Asks the compiler which diagnostic ids <c>/warnaserror:nullable</c> covers.</summary>
    /// <returns>Those ids, mapped to <see cref="ReportDiagnostic.Error"/>.</returns>
    private static ImmutableDictionary<string, ReportDiagnostic> BuildNullableWarnings()
    {
        string[] arguments = ["/warnaserror:nullable"];
        var parsed = CSharpCommandLineParser.Default.Parse(
            arguments,
            baseDirectory: Environment.CurrentDirectory,
            sdkDirectory: Environment.CurrentDirectory);

        // CS8632 and CS8669 are not in the compiler's own nullable group, but a test that annotates
        // a nullable reference type in a disabled context should still fail rather than warn.
        return parsed.CompilationOptions.SpecificDiagnosticOptions
            .SetItem("CS8632", ReportDiagnostic.Error)
            .SetItem("CS8669", ReportDiagnostic.Error);
    }
}
