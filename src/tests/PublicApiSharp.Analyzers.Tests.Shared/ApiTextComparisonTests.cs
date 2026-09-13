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

    /// <summary>Verifies boundary whitespace and significant interior changes agree with full comparison.</summary>
    /// <param name="baseline">The baseline with an edge-case text representation.</param>
    /// <param name="matches">Whether the text shortcut should accept it.</param>
    /// <param name="count">The diagnostic count implied by the full comparison.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments($"﻿{Baseline}", false, 0)]
    [Arguments("public static class C\r\n{\n    public const int A = 1;\r\n    public const int B = 2;\n}", true, 0)]
    [Arguments("public static class C\r{\r    public const int A = 1;\r    public const int B = 2;\r}", false, 0)]
    [Arguments("\tpublic static class C \t\n{\t\n\tpublic const int A = 1; \t\n\tpublic const int B = 2;\t\n}\t", true, 0)]
    [Arguments("public static class C\n{\n    public const int A = 1;\n    public const int B = 2;\n}", true, 0)]
    [Arguments("", false, 3)]
    [Arguments(" \t\r\n\u00a0\u2003\u0085\u2028\u2029", false, 3)]
    [Arguments("public static class C\n{\n    public const int A  = 1;\n    public const int B = 2;\n}\n", false, 1)]
    public async Task BaselineTextEdgesAgreeWithFullComparisonAsync(string baseline, bool matches, int count)
    {
        var compilation = ApiSurfaceTestHost.Compile(Source);
        await Assert.That(ApiTextComparison.Matches(SourceText.From(baseline), Baseline)).IsEqualTo(matches);
        await AssertFullComparisonAsync(compilation, SourceText.From(baseline), count);
    }

    /// <summary>Verifies each Unicode whitespace character is ignored at line ends but retained inside declarations.</summary>
    /// <param name="whitespace">The Unicode whitespace under comparison.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("\u00a0")]
    [Arguments("\u2003")]
    [Arguments("\u0085")]
    [Arguments("\u2028")]
    [Arguments("\u2029")]
    public async Task UnicodeWhitespacePreservesInteriorDifferencesAsync(string whitespace)
    {
        var compilation = ApiSurfaceTestHost.Compile(Source);
        var trailing = Baseline.Replace("\n", $"{whitespace}\n", StringComparison.Ordinal);
        var interior = Baseline.Replace("A = 1", $"A{whitespace}= 1", StringComparison.Ordinal);

        await Assert.That(ApiTextComparison.Matches(SourceText.From(trailing), Baseline)).IsTrue();
        await Assert.That(ApiTextComparison.Matches(SourceText.From(interior), Baseline)).IsFalse();
        await AssertFullComparisonAsync(compilation, SourceText.From(trailing), 0);
        await AssertFullComparisonAsync(compilation, SourceText.From(interior), 1);
    }

    /// <summary>Verifies a UTF-8 preamble is decoded before comparison and does not cause diagnostics.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task Utf8BaselinePreambleDoesNotChangeTheSurfaceAsync()
    {
        await using var stream = new MemoryStream();
        var encoding = new UTF8Encoding(true);
        stream.Write(encoding.GetPreamble());
        stream.Write(encoding.GetBytes(Baseline));
        stream.Position = 0;
        var text = SourceText.From(stream, encoding);

        await Assert.That(text.ToString()).IsEqualTo(Baseline);
        await Assert.That(ApiTextComparison.Matches(text, Baseline)).IsTrue();
        await AssertFullComparisonAsync(ApiSurfaceTestHost.Compile(Source), text, 0);
    }

    /// <summary>Verifies empty and whitespace-only baselines compare cleanly with an empty surface.</summary>
    /// <param name="baseline">An empty representation of the baseline.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("")]
    [Arguments(" \t\r\n\u00a0\u2003\u0085\u2028\u2029")]
    public async Task EmptyBaselineMatchesAnEmptyPublicSurfaceAsync(string baseline)
    {
        var compilation = ApiSurfaceTestHost.Compile("internal class Hidden { }");
        await Assert.That(ApiTextComparison.Matches(SourceText.From(baseline), string.Empty)).IsTrue();
        await AssertFullComparisonAsync(compilation, SourceText.From(baseline), 0);
    }

    /// <summary>Verifies the shortcut checks thousands of declarations through the last line.</summary>
    /// <param name="changeLastLine">Whether the final declaration differs.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LargeSurfaceComparisonReachesTheLastDeclarationAsync(bool changeLastLine)
    {
        const int Count = 3000;
        var builder = new PooledStringBuilder();
        for (var index = 0; index < Count; index++)
        {
            _ = builder.Append("public delegate void D").Append(index).Append("();\n");
        }

        var compilation = ApiSurfaceTestHost.Compile(builder.ToString());
        var surface = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None);
        var lines = surface.Text.TrimEnd().Split('\n');
        var lastLine = lines[^1];
        var baseline = changeLastLine
            ? $"{surface.Text[..(surface.Text.Length - lastLine.Length - 1)]}{lastLine.Replace("void", "int", StringComparison.Ordinal)}\n"
            : surface.Text;

        await Assert.That(surface.Declarations.Length).IsEqualTo(Count);
        await Assert.That(ApiTextComparison.Matches(SourceText.From(baseline), surface.Text)).IsEqualTo(!changeLastLine);
        await AssertFullComparisonAsync(compilation, SourceText.From(baseline), changeLastLine ? 1 : 0);
    }

    /// <summary>Derives complete diagnostics from parsed declarations independently of the shortcut.</summary>
    /// <param name="compilation">The source compilation.</param>
    /// <param name="baseline">The baseline text.</param>
    /// <param name="count">The independently expected number of diagnostics.</param>
    /// <returns>A task representing the asynchronous verification.</returns>
    internal static async Task AssertFullComparisonAsync(Compilation compilation, SourceText baseline, int count)
    {
        var surface = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None);
        var parsed = ApiTextParser.Parse(baseline, CancellationToken.None);
        var file = new MemoryBaseline(baseline);
        var expected = new List<Diagnostic>();
        if (!parsed.Success)
        {
            expected.Add(Diagnostic.Create(
                PublicApiRules.UnreadableBaseline,
                PublicApiBaselineAnalyzer.BaselineLocation(file, baseline, parsed.ErrorSpan),
                parsed.Error));
        }
        else
        {
            var comparison = ApiComparisonState.Create(surface, parsed, CancellationToken.None);
            foreach (var (symbol, current) in comparison.DeclarationsBySymbol)
            {
                if (!comparison.BaselineByIdentity.TryGetValue(current.Identity, out var previous))
                {
                    expected.Add(Diagnostic.Create(
                        PublicApiRules.Added,
                        PublicApiBaselineAnalyzer.SymbolLocation(symbol),
                        PublicApiBaselineAnalyzer.FinalLine(current.Text)));
                }
                else if (!string.Equals(previous.Text, current.Text, StringComparison.Ordinal))
                {
                    expected.Add(Diagnostic.Create(
                        PublicApiRules.Changed,
                        PublicApiBaselineAnalyzer.SymbolLocation(symbol),
                        PublicApiBaselineAnalyzer.FinalLine(current.Text),
                        previous.Text.Replace('\n', ' '),
                        current.Text.Replace('\n', ' ')));
                }
            }

            foreach (var (identity, previous) in comparison.BaselineByIdentity)
            {
                if (!comparison.ContainsCurrentIdentity(identity))
                {
                    expected.Add(Diagnostic.Create(
                        PublicApiRules.Removed,
                        PublicApiBaselineAnalyzer.BaselineLocation(file, baseline, previous.Span),
                        previous.Text.Replace('\n', ' ')));
                }
            }
        }

        await Assert.That(expected).Count().IsEqualTo(count);
        var actual = await compilation.WithAnalyzers([new PublicApiBaselineAnalyzer()], new AnalyzerOptions([file]))
            .GetAnalyzerDiagnosticsAsync();
        await Assert.That(DiagnosticMessages(actual)).IsEquivalentTo(DiagnosticMessages(expected));
    }

    /// <summary>Retains diagnostic identifiers, messages and locations for comparison.</summary>
    /// <param name="diagnostics">The diagnostics to describe.</param>
    /// <returns>The complete diagnostic descriptions.</returns>
    private static List<string> DiagnosticMessages(IEnumerable<Diagnostic> diagnostics)
    {
        var messages = new List<string>();
        foreach (var diagnostic in diagnostics)
        {
            messages.Add(diagnostic.ToString());
        }

        return messages;
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
