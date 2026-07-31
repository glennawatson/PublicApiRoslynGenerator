// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Unit tests for <see cref="ApiComparisonState"/>, which pairs the surface with the baseline.</summary>
/// <remarks>
/// Both sides of the comparison are read by the same parser, so they cannot drift apart through two
/// notions of what a declaration is. What that leaves is the case where the surface itself cannot be
/// read: a defect in this package rather than anything a consumer wrote.
/// </remarks>
public class ApiComparisonStateTests
{
    /// <summary>A line map for surfaces whose symbols are irrelevant to the case under test.</summary>
    private static readonly ISymbol?[] NoSymbols = [null];

    /// <summary>Verifies a surface that cannot be read back produces no comparison at all.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// Reporting from a half-parsed surface would blame the consumer for this package's own bug, so
    /// the comparison is abandoned instead and no diagnostic is raised.
    /// </remarks>
    [Test]
    public async Task SurfaceThatCannotBeReadBackProducesNoComparisonAsync()
    {
        var baseline = ApiTextParser.Parse(SourceText.From("namespace Sample;"), CancellationToken.None);
        var unreadable = new RenderedApiSurface("public class Broken { <<< }", NoSymbols);

        var state = ApiComparisonState.Create(unreadable, baseline, CancellationToken.None);

        await Assert.That(state).IsNull();
    }

    /// <summary>Verifies a surface that reads back gives a comparison holding both sides.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task SurfaceThatReadsBackProducesAComparisonAsync()
    {
        var baseline = ApiTextParser.Parse(SourceText.From("namespace Sample;"), CancellationToken.None);
        var surface = new RenderedApiSurface("namespace Sample;\n\npublic class Thing\n{\n}\n", NoSymbols);

        var state = ApiComparisonState.Create(surface, baseline, CancellationToken.None);

        await Assert.That(state).IsNotNull();
        await Assert.That(state!.CurrentByIdentity).IsNotEmpty();
    }
}
