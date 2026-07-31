// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Unit tests for which attributes reach the surface.</summary>
/// <remarks>
/// The configured cases are covered through rendering. What this pins is the one input rendering
/// cannot produce: an attribute that never bound to a type, which a compilation only holds while it
/// is in error.
/// </remarks>
public class ApiAttributeFilteringTests
{
    /// <summary>Verifies an attribute that bound to no type is left out of the surface.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task AttributeThatBoundToNoTypeIsExcludedAsync() =>
        await Assert.That(ApiAttributeRenderer.ShouldInclude(new UnboundAttribute(), ApiRenderOptions.Default)).IsFalse();

    /// <summary>Verifies a suppression that never reaches the assembly is left out of the surface.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// <c>SuppressMessageAttribute</c> is conditional on <c>CODE_ANALYSIS</c>, so it is in the symbol
    /// model the renderer reads but not in the assembly that ships, unless a build happens to define
    /// that constant. Recording it would assert surface that is not there, and would make the
    /// baseline depend on a compilation constant it has no way of knowing about.
    /// </remarks>
    [Test]
    public async Task SuppressionThatNeverReachesTheAssemblyIsExcludedAsync()
    {
        const string Source = """
                              using System.Diagnostics.CodeAnalysis;

                              namespace Sample;

                              public static class Calculator
                              {
                                  [SuppressMessage("Design", "CA1000:Justification text", Justification = "why")]
                                  public static int Add(int a, int b) => a + b;
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).DoesNotContain("SuppressMessage");
        await Assert.That(rendered).Contains("public static int Add(int a, int b)");
    }

    /// <summary>Verifies a trimming suppression is left out of the surface.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// This one does reach the assembly, so leaving it out is a judgement rather than a correction.
    /// Its justification is prose aimed at whoever reads the source: rewording it would fail the
    /// build and dirty the baseline over something no consumer can call or observe, which is the
    /// same reason the tooling and build attributes are dropped. Configuration can ask for it back.
    /// </remarks>
    [Test]
    public async Task TrimmingSuppressionIsExcludedAsync()
    {
        const string Source = """
                              using System.Diagnostics.CodeAnalysis;

                              namespace Sample;

                              public static class Loader
                              {
                                  [UnconditionalSuppressMessage("Trimming", "IL2026:Requires", Justification = "why")]
                                  public static string Describe() => "ok";
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).DoesNotContain("UnconditionalSuppressMessage");
        await Assert.That(rendered).Contains("public static string Describe()");
    }

    /// <summary>An attribute whose type never resolved, as a compilation in error would hold it.</summary>
    private sealed class UnboundAttribute : AttributeData
    {
        /// <inheritdoc/>
        protected override INamedTypeSymbol? CommonAttributeClass => null;

        /// <inheritdoc/>
        protected override IMethodSymbol? CommonAttributeConstructor => null;

        /// <inheritdoc/>
        protected override SyntaxReference? CommonApplicationSyntaxReference => null;

        /// <inheritdoc/>
        protected override ImmutableArray<TypedConstant> CommonConstructorArguments => [];

        /// <inheritdoc/>
        protected override ImmutableArray<KeyValuePair<string, TypedConstant>> CommonNamedArguments => [];
    }
}
