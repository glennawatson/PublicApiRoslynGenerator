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
