// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Verifies text matching preserves the full comparison's diagnostics and whitespace contract.</summary>
public class ApiTextComparisonTests
{
    /// <summary>A surface with two independently removable declarations.</summary>
    private const string Source = "public static class C { public const int A = 1; public const int B = 2; }";

    /// <summary>The canonical surface for the source.</summary>
    private const string Baseline = "public static class C\n{\n    public const int A = 1;\n    public const int B = 2;\n}\n";

    /// <summary>Verifies line trimming and blank-line removal match the parser's normalization.</summary>
    /// <param name="baseline">The baseline text.</param>
    /// <param name="rendered">The rendered text.</param>
    /// <param name="expected">Whether normalized text matches.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("", "", true)]
    [Arguments(" \t\r\n\n", "", true)]
    [Arguments("", " \t\r\n\n", true)]
    [Arguments("a\r\nb\r\n", "a\nb\n", true)]
    [Arguments("a\nb", "a\nb\n", true)]
    [Arguments("a\nb\n\n", "a\nb", true)]
    [Arguments("\n\t a \t\n \n  b \n", "a\nb\n", true)]
    [Arguments("a\nb\n", "\n  a \n\n\tb\t\n", true)]
    [Arguments("\u00a0a\u2003\n\u0085\nb\r\n", "a\nb", true)]
    [Arguments("a", "b", false)]
    [Arguments("a", "aa", false)]
    [Arguments("aa", "a", false)]
    [Arguments("a", "a\nb", false)]
    [Arguments("a\nb", "a", false)]
    [Arguments("a b", "a  b", false)]
    [Arguments("a\rb", "a\nb", false)]
    public async Task LineNormalizationMatchesTheParserAsync(string baseline, string rendered, bool expected)
    {
        await Assert.That(ApiTextComparison.Matches(SourceText.From(baseline), rendered)).IsEqualTo(expected);
        await Assert.That(string.Equals(ApiTextParser.NormalizeText(baseline), ApiTextParser.NormalizeText(rendered), StringComparison.Ordinal))
            .IsEqualTo(expected);
    }

    /// <summary>Verifies a matching baseline requires no allocation to compare repeatedly.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task MatchingTextComparisonAllocatesNothingAsync()
    {
        const int Iterations = 1000;
        var text = SourceText.From(Baseline.Replace("\n", "\r\n", StringComparison.Ordinal));
        _ = ApiTextComparison.Matches(text, Baseline);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var matches = 0;
        for (var index = 0; index < Iterations; index++)
        {
            if (ApiTextComparison.Matches(text, Baseline))
            {
                matches++;
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(matches).IsEqualTo(Iterations);
        await Assert.That(allocated).IsEqualTo(0);
    }

    /// <summary>Verifies ignored whitespace leaves the full comparison empty and reports nothing.</summary>
    /// <param name="baseline">A baseline with only ignored whitespace differences.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments(Baseline)]
    [Arguments("public static class C\r\n{\r\n    public const int A = 1;\r\n    public const int B = 2;\r\n}\r\n")]
    [Arguments("public static class C\n{\n    public const int A = 1;\n    public const int B = 2;\n}")]
    [Arguments($"{Baseline}\n\n")]
    [Arguments("\n\tpublic static class C \t\n \n { \n\tpublic const int A = 1; \n\n public const int B = 2;\t\n }\n\n")]
    public async Task IgnoredWhitespaceHasNoDifferencesAsync(string baseline)
    {
        await Assert.That(ApiSurfaceTestHost.Render(Source)).IsEqualTo(Baseline);
        await Assert.That(ApiTextComparison.Matches(SourceText.From(baseline), Baseline)).IsTrue();
        await AssertAgreementAsync(Source, baseline, string.Empty);
    }

    /// <summary>Verifies changed text falls through and preserves additions, changes, removals and parse errors.</summary>
    /// <param name="baseline">The changed or malformed baseline.</param>
    /// <param name="diagnosticId">The diagnostic the analyzer must report.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("public static class C\n{\n    public const int A = 9;\n    public const int B = 2;\n}\n", "PAS0003")]
    [Arguments("public static class C\n{\n    public const int A = 1;\n}\n", "PAS0001")]
    [Arguments("public static class C\n{\n    public const int A = 1;\n    public const int B = 2;\n    public const int D = 3;\n}\n", "PAS0002")]
    [Arguments("public static class C\n{\n    public const int A = 1;\n    public const int B = 2\n}\n", "PAS0005")]
    [Arguments("public static class C\n{\n    public const int A  = 1;\n    public const int B = 2;\n}\n", "PAS0003")]
    public async Task NearMatchesPreserveExpectedDiagnosticsAsync(string baseline, string diagnosticId)
    {
        await Assert.That(ApiTextComparison.Matches(SourceText.From(baseline), Baseline)).IsFalse();
        await AssertAgreementAsync(Source, baseline, diagnosticId);
    }

    /// <summary>Verifies reordering misses the text shortcut but reports nothing.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task ReorderedMembersAreSilentAsync()
    {
        const string Reordered = "public static class C\n{\n    public const int B = 2;\n    public const int A = 1;\n}\n";
        await Assert.That(ApiTextComparison.Matches(SourceText.From(Reordered), Baseline)).IsFalse();
        await AssertAgreementAsync(Source, Reordered, string.Empty);
    }

    /// <summary>Verifies a compilation without API ends silently without needing a symbol callback.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task EmptySurfaceHasNoDifferencesAsync()
    {
        await Assert.That(ApiSurfaceTestHost.Render("internal class C { }")).IsEmpty();
        await AssertAgreementAsync("internal class C { }", "\n \t\n", string.Empty);
    }

    /// <summary>Verifies a failed baseline read during parsing abandons comparison quietly.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task BaselineReadFailureDuringParsingIsSilentAsync()
    {
        var compilation = ApiSurfaceTestHost.Compile(Source);
        var text = new FailingReadText();
        await Assert.That(ApiTextComparison.Matches(text, Baseline)).IsFalse();
        var options = new AnalyzerOptions([new MemoryBaseline(text)]);

        var diagnostics = await compilation.WithAnalyzers([new PublicApiBaselineAnalyzer()], options).GetAnalyzerDiagnosticsAsync();

        await Assert.That(text.ReadAttempted).IsTrue();
        await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>Checks matching text against the full comparison and verifies the expected diagnostics.</summary>
    /// <param name="source">The source to compile.</param>
    /// <param name="baseline">The baseline text.</param>
    /// <param name="diagnosticId">The sole expected diagnostic, or empty for a clean comparison.</param>
    /// <returns>A task representing the asynchronous verification.</returns>
    private static async Task AssertAgreementAsync(string source, string baseline, string diagnosticId)
    {
        var compilation = ApiSurfaceTestHost.Compile(source);
        var surface = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None);
        var baselineText = SourceText.From(baseline);
        if (ApiTextComparison.Matches(baselineText, surface.Text))
        {
            var parse = ApiTextParser.Parse(baselineText, CancellationToken.None);
            await Assert.That(parse.Success).IsTrue();
            var comparison = ApiComparisonState.Create(surface, parse, CancellationToken.None);
            await Assert.That(surface.Declarations.Length).IsEqualTo(comparison.BaselineByIdentity.Count);
            foreach (var current in surface.Declarations)
            {
                await Assert.That(comparison.ContainsCurrentIdentity(current.Identity)).IsTrue();
                await Assert.That(comparison.BaselineByIdentity).ContainsKey(current.Identity);
                await Assert.That(comparison.BaselineByIdentity[current.Identity].Text).IsEqualTo(current.Text);
            }

            foreach (var declaration in comparison.DeclarationsBySymbol.Values)
            {
                await Assert.That(comparison.BaselineByIdentity).ContainsKey(declaration.Identity);
                await Assert.That(comparison.BaselineByIdentity[declaration.Identity].Text).IsEqualTo(declaration.Text);
            }
        }

        var options = new AnalyzerOptions([new MemoryBaseline(baselineText)]);
        var diagnostics = await compilation.WithAnalyzers([new PublicApiBaselineAnalyzer()], options).GetAnalyzerDiagnosticsAsync();
        if (diagnosticId.Length == 0)
        {
            await Assert.That(diagnostics).IsEmpty();
        }
        else
        {
            await Assert.That(diagnostics).Count().IsEqualTo(1);
            await Assert.That(diagnostics[0].Id).IsEqualTo(diagnosticId);
        }
    }

    /// <summary>A baseline stored in memory for the analyzer driver.</summary>
    /// <param name="text">The baseline text.</param>
    private sealed class MemoryBaseline(SourceText text) : AdditionalText
    {
        /// <inheritdoc/>
        public override string Path => PublicApiBaselineAnalyzer.BaselineFileName;

        /// <inheritdoc/>
        public override SourceText GetText(CancellationToken cancellationToken = default) => text;
    }

    /// <summary>A baseline that permits character comparisons but fails when the parser reads a block.</summary>
    private sealed class FailingReadText : SourceText
    {
        /// <summary>A baseline differing from the rendered surface so parsing is required.</summary>
        private const string Content = "public static class C { }";

        /// <inheritdoc/>
        public override Encoding Encoding => Encoding.UTF8;

        /// <inheritdoc/>
        public override int Length => Content.Length;

        /// <summary>Gets a value indicating whether the parser attempted to read the baseline.</summary>
        internal bool ReadAttempted { get; private set; }

        /// <inheritdoc/>
        public override char this[int position] => Content[position];

        /// <inheritdoc/>
        public override void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
        {
            ReadAttempted = true;
            throw new InvalidOperationException("The baseline text could not be read.");
        }
    }
}
