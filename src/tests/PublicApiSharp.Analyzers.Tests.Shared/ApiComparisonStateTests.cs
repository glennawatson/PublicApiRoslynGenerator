// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Unit tests for <see cref="ApiComparisonState"/>, which pairs the surface with the baseline.</summary>
/// <remarks>
/// The baseline is text and is read by the parser; the surface states its declarations directly,
/// because it was just written and knows what it wrote. Both sides end up keyed the same way, which
/// is what <see cref="ApiIdentityEquivalenceTests"/> holds them to.
/// </remarks>
public class ApiComparisonStateTests
{
    /// <summary>The identity of the single type these tests compare.</summary>
    private const string ThingIdentity = "Sample.Thing";

    /// <summary>A library declaring one type.</summary>
    private const string Source = """
                                  namespace Sample;

                                  public class Thing
                                  {
                                  }
                                  """;

    /// <summary>The same type declared twice, as only a hand-edited baseline could.</summary>
    private const string DuplicatedBaseline = """
                                              namespace Sample;

                                              public class Thing
                                              {
                                              }
                                              public class Thing
                                              {
                                              }

                                              """;

    /// <summary>Verifies the comparison indexes the declarations the surface states.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The surface states its declarations rather than being parsed back out of its own text, so what
    /// the comparison holds is what the renderer recorded as it wrote.
    /// </remarks>
    [Test]
    public async Task SurfaceDeclarationsAreIndexedAsync()
    {
        var surface = Render(Source);
        var baseline = ApiTextParser.Parse(SourceText.From("namespace Sample;"), CancellationToken.None);

        var state = ApiComparisonState.Create(surface, baseline, CancellationToken.None);

        await Assert.That(state.CurrentByIdentity).ContainsKey(ThingIdentity);
        await Assert.That(state.DeclarationsBySymbol).IsNotEmpty();
    }

    /// <summary>Verifies a duplicate identity keeps the first entry, so the comparison stays settled.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task DuplicateIdentityKeepsTheFirstEntryAsync()
    {
        var surface = Render(Source);
        var baseline = ApiTextParser.Parse(SourceText.From(DuplicatedBaseline), CancellationToken.None);

        var state = ApiComparisonState.Create(surface, baseline, CancellationToken.None);

        await Assert.That(state.BaselineByIdentity).ContainsKey(ThingIdentity);
    }

    /// <summary>Compiles and renders a library.</summary>
    /// <param name="source">The C# source.</param>
    /// <returns>The rendered surface.</returns>
    private static RenderedApiSurface Render(string source) =>
        ApiSurfaceRenderer.Render(
            ApiSurfaceTestHost.Compile(source),
            ApiRenderOptions.Default,
            CancellationToken.None);
}
