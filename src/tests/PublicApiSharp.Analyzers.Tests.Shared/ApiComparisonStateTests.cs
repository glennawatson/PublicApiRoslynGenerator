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
                                              public struct Thing
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

        await Assert.That(state.ContainsCurrentIdentity(ThingIdentity)).IsTrue();
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
        await Assert.That(state.BaselineByIdentity[ThingIdentity].Text).IsEqualTo("public class Thing");
    }

    /// <summary>Verifies membership uses identity order even when fields render before earlier-named methods.</summary>
    /// <param name="identity">The key to find.</param>
    /// <param name="expected">Whether the surface contains the key.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("C", true)]
    [Arguments("C.A()", true)]
    [Arguments("C.Z", true)]
    [Arguments("", false)]
    [Arguments("C.B()", false)]
    [Arguments("Z", false)]
    [Arguments("c.z", false)]
    public async Task MembershipUsesOrdinalIdentityOrderAsync(string identity, bool expected)
    {
        var surface = Render("public static class C { public static void A() { } public static int Z; }");
        var baseline = ApiTextParser.Parse(SourceText.From(string.Empty), CancellationToken.None);

        var state = ApiComparisonState.Create(surface, baseline, CancellationToken.None);

        await Assert.That(state.ContainsCurrentIdentity(identity)).IsEqualTo(expected);
        await Assert.That(state.DeclarationsBySymbol).Count().IsEqualTo(surface.Declarations.Length);
    }

    /// <summary>Verifies an empty surface has neither current keys nor symbol declarations.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task EmptySurfaceHasNoCurrentIdentityAsync()
    {
        var surface = Render("internal class C { }");
        var baseline = ApiTextParser.Parse(SourceText.From(Source), CancellationToken.None);

        var state = ApiComparisonState.Create(surface, baseline, CancellationToken.None);

        await Assert.That(state.ContainsCurrentIdentity(ThingIdentity)).IsFalse();
        await Assert.That(state.DeclarationsBySymbol).IsEmpty();
        await Assert.That(state.BaselineByIdentity).ContainsKey(ThingIdentity);
    }

    /// <summary>Verifies duplicate member identities retain their first baseline declaration in both index and diagnostics.</summary>
    /// <param name="firstValue">The first baseline constant value.</param>
    /// <param name="diagnostics">The expected diagnostic count.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments(1, 0)]
    [Arguments(2, 1)]
    public async Task DuplicateMemberIdentityUsesItsFirstBaselineValueAsync(int firstValue, int diagnostics)
    {
        const int ParsedCount = 3;
        const int IndexedCount = 2;
        const string Snippet = "public static class C { public const int Value = 1; }";
        var surface = Render(Snippet);
        var baseline = $"public static class C\n{{\npublic const int Value = {firstValue};\npublic const int Value = 1;\n}}\n";
        var parsed = ApiTextParser.Parse(SourceText.From(baseline), CancellationToken.None);
        var state = ApiComparisonState.Create(surface, parsed, CancellationToken.None);

        await Assert.That(parsed.Success).IsTrue();
        await Assert.That(parsed.Declarations.Length).IsEqualTo(ParsedCount);
        await Assert.That(state.BaselineByIdentity).Count().IsEqualTo(IndexedCount);
        await Assert.That(state.BaselineByIdentity["C.Value"].Text).IsEqualTo($"public const int Value = {firstValue};");
        await ApiTextComparisonTests.AssertFullComparisonAsync(ApiSurfaceTestHost.Compile(Snippet), SourceText.From(baseline), diagnostics);
    }

    /// <summary>Verifies distinct headers sharing a receiver stay indexed even beside an exact duplicate baseline block.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task SharedReceiverHeadersRetainTheirSeparateComparisonKeysAsync()
    {
        const int BlockCount = 3;
        if (!RoslynFeatures.SupportsExtensionBlocks)
        {
            return;
        }

        const string Snippet = """
            public static class Extensions
            {
                extension(string first) { public int First => first.Length; }
                extension(string second) { public int Second => second.Length; }
                extension(string third) { public int Third => third.Length; }
            }
            """;
        var compilation = ApiSurfaceTestHost.Compile(Snippet);
        var surface = Render(Snippet);
        var baseline = ApiTextParser.Parse(SourceText.From(surface.Text + surface.Text), CancellationToken.None);
        var state = ApiComparisonState.Create(surface, baseline, CancellationToken.None);
        var blocks = new List<ApiDeclaration>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in state.BaselineByIdentity.Values)
        {
            if (declaration.IsExtensionBlock)
            {
                blocks.Add(declaration);
                _ = identities.Add(declaration.Identity);
            }
        }

        await Assert.That(baseline.Success).IsTrue();
        await Assert.That(blocks).Count().IsEqualTo(BlockCount);
        await Assert.That(identities).Count().IsEqualTo(1);
        await Assert.That(state.BaselineByIdentity).Count().IsEqualTo(surface.Declarations.Length);
        foreach (var declaration in state.DeclarationsBySymbol.Values)
        {
            await Assert.That(state.ContainsCurrentIdentity(declaration.Identity)).IsTrue();
            await Assert.That(state.BaselineByIdentity[declaration.Identity].Text).IsEqualTo(declaration.Text);
        }

        await ApiTextComparisonTests.AssertFullComparisonAsync(compilation, SourceText.From(surface.Text + surface.Text), 0);
    }

    /// <summary>Verifies types differing only by case or generic arity occupy distinct comparison entries.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task TypeIdentityKeepsCaseAndGenericArityDistinctAsync()
    {
        const string Snippet = "public interface C { } public interface c { } public interface C<T> { } public interface C<T, U> { }";
        string[] identities = ["C", "c", "C\u00601", "C\u00602"];
        var surface = Render(Snippet);
        var parsed = ApiTextParser.Parse(SourceText.From(surface.Text), CancellationToken.None);
        var comparison = ApiComparisonState.Create(surface, parsed, CancellationToken.None);

        await Assert.That(parsed.Success).IsTrue();
        await Assert.That(comparison.BaselineByIdentity.Keys).IsEquivalentTo(identities);
        foreach (var identity in identities)
        {
            await Assert.That(comparison.ContainsCurrentIdentity(identity)).IsTrue();
        }

        await ApiTextComparisonTests.AssertFullComparisonAsync(ApiSurfaceTestHost.Compile(Snippet), SourceText.From(surface.Text), 0);
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
