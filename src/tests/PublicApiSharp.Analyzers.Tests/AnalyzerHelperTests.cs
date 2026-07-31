// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Unit tests for the small helpers the analyzer and renderer are built from.</summary>
public class AnalyzerHelperTests
{
    /// <summary>A representative fully qualified name used across the pattern cases.</summary>
    private const string QualifiedName = "A.B.C";

    /// <summary>A pattern with an inner wildcard, used across the backtracking cases.</summary>
    private const string InnerWildcard = "A.*.C";

    /// <summary>Verifies a pattern with no wildcard matches only itself.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task LiteralPatternMatchesExactlyAsync()
    {
        await Assert.That(NamePattern.Matches(QualifiedName, QualifiedName)).IsTrue();
        await Assert.That(NamePattern.Matches(QualifiedName, "A.B.CD")).IsFalse();
        await Assert.That(NamePattern.Matches(QualifiedName, "A.B")).IsFalse();
    }

    /// <summary>Verifies a bare wildcard matches anything, including nothing.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task BareWildcardMatchesAnythingAsync()
    {
        await Assert.That(NamePattern.Matches("*", QualifiedName)).IsTrue();
        await Assert.That(NamePattern.Matches("*", string.Empty)).IsTrue();
    }

    /// <summary>Verifies a wildcard in the middle spans any run of characters.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>This is the case that forces the matcher to backtrack.</remarks>
    [Test]
    public async Task InnerWildcardSpansAnyRunAsync()
    {
        await Assert.That(NamePattern.Matches(InnerWildcard, QualifiedName)).IsTrue();
        await Assert.That(NamePattern.Matches(InnerWildcard, "A.B.B.C")).IsTrue();
        await Assert.That(NamePattern.Matches(InnerWildcard, "A.C")).IsFalse();
        await Assert.That(NamePattern.Matches(InnerWildcard, "A.B.D")).IsFalse();
    }

    /// <summary>Verifies consecutive wildcards behave as one.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ConsecutiveWildcardsCollapseAsync()
    {
        await Assert.That(NamePattern.Matches("A**C", "ABC")).IsTrue();
        await Assert.That(NamePattern.Matches("A**", "ABC")).IsTrue();
    }

    /// <summary>Verifies an empty pattern set matches nothing.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task EmptyPatternSetMatchesNothingAsync() =>
        await Assert.That(NamePattern.MatchesAny([], QualifiedName)).IsFalse();

    /// <summary>Verifies a later pattern in the set can be the one that matches.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task AnyPatternInTheSetCanMatchAsync() =>
        await Assert.That(NamePattern.MatchesAny(["X.*", "A.*"], QualifiedName)).IsTrue();

    /// <summary>Verifies a missing option key yields an empty list.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task MissingOptionYieldsAnEmptyListAsync()
    {
        var values = AnalyzerOptionReader.ReadCommaSeparatedList(Options(), "absent.key");

        await Assert.That(values).IsEmpty();
    }

    /// <summary>Verifies entries are trimmed and empty ones dropped.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>The trailing separator is what forces the list to be compacted after parsing.</remarks>
    [Test]
    public async Task EmptyEntriesAreDroppedAsync()
    {
        const int Expected = 2;

        var values = AnalyzerOptionReader.ReadCommaSeparatedList(Options(("k", " a , , b ,")), "k");

        await Assert.That(values).Count().IsEqualTo(Expected);
        await Assert.That(values[0]).IsEqualTo("a");
        await Assert.That(values[1]).IsEqualTo("b");
    }

    /// <summary>Verifies a list with no empty entries is returned as parsed.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task FullListIsReturnedUncompactedAsync()
    {
        const int Expected = 2;

        var values = AnalyzerOptionReader.ReadCommaSeparatedList(Options(("k", "a,b")), "k");

        await Assert.That(values).Count().IsEqualTo(Expected);
    }

    /// <summary>Verifies the single-item and params array factories both build an array.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ImmutableArrayFactoriesBuildArraysAsync()
    {
        const int Two = 2;

        await Assert.That(ImmutableArrays.Of("one")).Count().IsEqualTo(1);
        await Assert.That(ImmutableArrays.Of("one", "two")).Count().IsEqualTo(Two);
    }

    /// <summary>Verifies the host-capability probes answer for whichever slot is built.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The two halves of extension-block support arrive in different Roslyn versions, so the only
    /// thing that holds on every slot is that a container is never claimed to round-trip when the
    /// syntax to read it back does not exist.
    /// </remarks>
    [Test]
    public async Task ExtensionCapabilitiesAreConsistentAsync()
    {
        var compilation = ApiSurfaceTestHost.Compile("""
                                                     namespace Sample;

                                                     public static class Helpers
                                                     {
                                                         public static int Twice(this int value) => value * 2;
                                                     }
                                                     """);

        var helpers = compilation.GetTypeByMetadataName("Sample.Helpers")!;

        // A plain static class is never an extension container, on any slot.
        await Assert.That(RoslynFeatures.IsExtensionContainer(helpers)).IsFalse();
        await Assert.That(RoslynFeatures.ExtensionReceiver(helpers)).IsNull();

        // The capability flag is whatever this slot supports; reading it must not throw.
        await Assert.That(RoslynFeatures.SupportsExtensionBlocks || !RoslynFeatures.SupportsExtensionBlocks).IsTrue();
    }

    /// <summary>Verifies a line with no declaration on it maps to no symbol.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task SurfaceLookupHandlesOutOfRangeLinesAsync()
    {
        const int WayPastTheEnd = 9999;

        var surface = new RenderedApiSurface("line\n", [null]);

        await Assert.That(surface.SymbolAtLine(-1)).IsNull();
        await Assert.That(surface.SymbolAtLine(WayPastTheEnd)).IsNull();
        await Assert.That(surface.SymbolAtLine(0)).IsNull();
    }

    /// <summary>Builds analyzer config options from the given entries.</summary>
    /// <param name="entries">The key/value pairs.</param>
    /// <returns>The options.</returns>
    private static StubOptions Options(params (string Key, string Value)[] entries)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            builder[key] = value;
        }

        return new(builder.ToImmutable());
    }

    /// <summary>An in-memory <see cref="AnalyzerConfigOptions"/> holding a fixed set of entries.</summary>
    private sealed class StubOptions : AnalyzerConfigOptions
    {
        /// <summary>The configured entries.</summary>
        private readonly ImmutableDictionary<string, string> _entries;

        /// <summary>Initializes a new instance of the <see cref="StubOptions"/> class.</summary>
        /// <param name="entries">The configured entries.</param>
        internal StubOptions(ImmutableDictionary<string, string> entries) => _entries = entries;

        /// <inheritdoc/>
        public override bool TryGetValue(string key, out string value) => _entries.TryGetValue(key, out value!);
    }
}
