// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Tests for how the analyzer locates the baseline it should compare against.</summary>
/// <remarks>
/// The package's MSBuild targets resolve a path per target framework and make it compiler-visible.
/// The file-name fallback exists so a project that wires the additional file up by hand still works.
/// </remarks>
public class PublicApiBaselineDiscoveryTests
{
    /// <summary>A source whose whole surface the shared baseline already describes.</summary>
    private const string MatchingSource = """
                                          namespace Sample;

                                          public class Thing
                                          {
                                              public int Value { get; set; }
                                          }
                                          """;

    /// <summary>The baseline describing <see cref="MatchingSource"/>.</summary>
    private const string MatchingBaseline = """
                                            namespace Sample;

                                            public class Thing
                                            {
                                                public Thing() { }
                                                public int Value { get; set; }
                                            }

                                            """;

    /// <summary>Verifies the resolved MSBuild path finds a baseline the name alone would not.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public Task ResolvedPathFindsADifferentlyNamedBaselineAsync() =>
        PublicApiVerifier.AnalyzeWithConfigAsync(
            MatchingSource,
            MatchingBaseline,
            "Surface.txt",
            "build_property.PublicApiBaselineFile = Surface.txt");

    /// <summary>Verifies a baseline in a per-target-framework folder is found by name.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>This is the shape the package's targets produce: PublicAPI/&lt;tfm&gt;/PublicAPI.txt.</remarks>
    [Test]
    public Task BaselineInATargetFrameworkFolderIsFoundAsync() =>
        PublicApiVerifier.AnalyzeWithConfigAsync(
            MatchingSource,
            MatchingBaseline,
            "PublicAPI/net10.0/PublicAPI.txt",
            "build_property.PublicApiBaselineFile = PublicAPI/net10.0/PublicAPI.txt");

    /// <summary>Verifies a file whose name merely ends with the baseline name is not mistaken for one.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The fallback matches on a path segment, so <c>NotPublicAPI.txt</c> must not be picked up —
    /// otherwise an unrelated file would silently become the surface of record.
    /// </remarks>
    [Test]
    public Task NameSuffixWithoutASeparatorIsNotABaselineAsync() =>
        PublicApiVerifier.AnalyzeWithConfigAsync(
            MatchingSource,
            MatchingBaseline,
            "NotPublicAPI.txt",
            "# no baseline path configured");

    /// <summary>Verifies a target framework with no baseline is reported when the rule is enabled.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task MissingBaselineIsReportedWhenEnabledAsync()
    {
        var expected = PublicApiVerifier.Diagnostic(PublicApiRules.MissingBaseline)
            .WithNoLocation()
            .WithArguments("PublicAPI/net10.0/PublicAPI.txt", "net10.0");

        await PublicApiVerifier.AnalyzeWithConfigAsync(
            MatchingSource,
            baseline: null,
            "unused.txt",
            """
            dotnet_diagnostic.PAS0004.severity = warning
            build_property.PublicApiBaselineFile = PublicAPI/net10.0/PublicAPI.txt
            build_property.TargetFramework = net10.0
            """,
            expected);
    }

    /// <summary>Verifies the message falls back gracefully when the target framework is unknown.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task MissingBaselineWithoutATargetFrameworkStillReportsAsync()
    {
        var expected = PublicApiVerifier.Diagnostic(PublicApiRules.MissingBaseline)
            .WithNoLocation()
            .WithArguments("PublicAPI.txt", "(unknown)");

        await PublicApiVerifier.AnalyzeWithConfigAsync(
            MatchingSource,
            baseline: null,
            "unused.txt",
            """
            dotnet_diagnostic.PAS0004.severity = warning
            build_property.PublicApiBaselineFile = PublicAPI.txt
            """,
            expected);
    }

    /// <summary>Verifies an implicit constructor is reported on the type that declares it.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// A compiler-supplied constructor has no source location of its own, so the diagnostic has to
    /// fall back to somewhere a reader can act on.
    /// </remarks>
    [Test]
    public async Task ImplicitConstructorIsReportedOnItsTypeAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class {|PAS0001:Thing|}
                              {
                              }
                              """;

        const string Baseline = """
                                namespace Sample;

                                public class Thing
                                {
                                }

                                """;

        await PublicApiVerifier.AnalyzeAsync(Source, Baseline);
    }

    /// <summary>Verifies an unrelated additional file is not treated as the baseline.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// Projects pass all sorts of things as additional files. Picking the wrong one would make an
    /// unrelated file the surface of record and report the entire assembly as new.
    /// </remarks>
    [Test]
    public Task UnrelatedAdditionalFileIsIgnoredAsync() =>
        PublicApiVerifier.AnalyzeWithConfigAsync(
            MatchingSource,
            "some unrelated content",
            "Notes.txt",
            "# no baseline path configured");

    /// <summary>Verifies a baseline whose contents cannot be read is passed over quietly.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The compiler hands the analyzer the file, not its contents, and reading them can come back
    /// empty-handed. Reporting the whole assembly as new would be worse than saying nothing, and
    /// throwing would fail the build over a file the analyzer only reads.
    /// </remarks>
    [Test]
    public async Task BaselineThatCannotBeReadIsPassedOverAsync()
    {
        var compilation = ApiSurfaceTestHost.Compile(MatchingSource);
        AnalyzerOptions options = new([new UnreadableBaseline()]);

        var diagnostics = await compilation
            .WithAnalyzers([new PublicApiBaselineAnalyzer()], options)
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        await Assert.That(diagnostics).IsEmpty();
    }

    /// <summary>A baseline file the compiler can name but cannot produce contents for.</summary>
    private sealed class UnreadableBaseline : AdditionalText
    {
        /// <inheritdoc/>
        public override string Path => PublicApiVerifier.BaselineFileName;

        /// <inheritdoc/>
        public override SourceText? GetText(CancellationToken cancellationToken = default) => null;
    }
}
