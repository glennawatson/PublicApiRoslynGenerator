// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;

using Microsoft.CodeAnalysis.Diagnostics;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Unit tests for the editorconfig options that decide what reaches the baseline.</summary>
public class ApiRenderOptionsTests
{
    /// <summary>The editorconfig key listing attribute patterns to leave out.</summary>
    private const string ExcludedAttributesKey = "publicapisharp.excluded_attributes";

    /// <summary>An attribute used across these tests as a stand-in for a real one.</summary>
    private const string ObsoleteAttributeName = "System.ObsoleteAttribute";

    /// <summary>Verifies an exact attribute name is excluded.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ExactAttributeNameIsExcludedAsync()
    {
        var options = Read((ExcludedAttributesKey, ObsoleteAttributeName));

        await Assert.That(options.IsAttributeExcluded(ObsoleteAttributeName)).IsTrue();
        await Assert.That(options.IsAttributeExcluded("System.SerializableAttribute")).IsFalse();
    }

    /// <summary>Verifies a trailing wildcard excludes a whole family.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>Listing a family one attribute at a time is the case this exists to avoid.</remarks>
    [Test]
    public async Task WildcardExcludesAFamilyAsync()
    {
        var options = Read((ExcludedAttributesKey, "System.Diagnostics.CodeAnalysis.*"));

        await Assert.That(options.IsAttributeExcluded("System.Diagnostics.CodeAnalysis.NotNullWhenAttribute")).IsTrue();
        await Assert.That(options.IsAttributeExcluded("System.Diagnostics.CodeAnalysis.MaybeNullAttribute")).IsTrue();
        await Assert.That(options.IsAttributeExcluded("System.Diagnostics.DebuggerDisplayAttribute")).IsFalse();
    }

    /// <summary>Verifies a leading wildcard excludes by naming convention.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task WildcardExcludesByNamingConventionAsync()
    {
        var options = Read((ExcludedAttributesKey, "*.InternalUseAttribute"));

        await Assert.That(options.IsAttributeExcluded("Contoso.Widgets.InternalUseAttribute")).IsTrue();
        await Assert.That(options.IsAttributeExcluded("Contoso.InternalUseAttribute")).IsTrue();
        await Assert.That(options.IsAttributeExcluded("Contoso.PublicUseAttribute")).IsFalse();
    }

    /// <summary>Verifies several comma-separated patterns are all honoured.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task MultiplePatternsAreHonouredAsync()
    {
        var options = Read((ExcludedAttributesKey, $" {ObsoleteAttributeName} , Contoso.* "));

        await Assert.That(options.IsAttributeExcluded(ObsoleteAttributeName)).IsTrue();
        await Assert.That(options.IsAttributeExcluded("Contoso.AnythingAttribute")).IsTrue();
        await Assert.That(options.IsAttributeExcluded("System.SerializableAttribute")).IsFalse();
    }

    /// <summary>Verifies a project can ask for an attribute the built-in list drops.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// A library that genuinely tracks its assembly version needs it back without giving up the rest
    /// of the defaults.
    /// </remarks>
    [Test]
    public async Task IncludedAttributeOverridesTheBuiltInListAsync()
    {
        const string Source = """
                              using System.Reflection;

                              [assembly: AssemblyVersion("2.1.0.0")]

                              namespace Sample;

                              public class Thing
                              {
                              }
                              """;

        const string Expected = """
                                [assembly: System.Reflection.AssemblyVersion("2.1.0.0")]
                                namespace Sample;

                                public class Thing
                                {
                                    public Thing() { }
                                }

                                """;

        var options = Read(("publicapisharp.included_attributes", "System.Reflection.AssemblyVersionAttribute"));
        var rendered = ApiSurfaceTestHost.Render(Source, options);

        await Assert.That(rendered).IsEqualTo(Expected.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>Verifies an explicit exclusion beats an explicit inclusion.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>Contradictory configuration has to resolve one way; excluding is the safe direction.</remarks>
    [Test]
    public async Task ExclusionBeatsInclusionAsync()
    {
        var options = Read(
            (ExcludedAttributesKey, "Contoso.ThingAttribute"),
            ("publicapisharp.included_attributes", "Contoso.*"));

        await Assert.That(options.IsAttributeExcluded("Contoso.ThingAttribute")).IsTrue();
    }

    /// <summary>Verifies excluding a namespace keeps its types out of the surface entirely.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ExcludedNamespaceIsNotRenderedAsync()
    {
        const string Source = """
                              namespace Sample.Internals
                              {
                                  public class Hidden
                                  {
                                  }
                              }

                              namespace Sample.Public
                              {
                                  public class Shown
                                  {
                                  }
                              }
                              """;

        const string Expected = """
                                namespace Sample.Public;

                                public class Shown
                                {
                                    public Shown() { }
                                }

                                """;

        var options = Read(("publicapisharp.excluded_namespace_prefixes", "Sample.Internals"));
        var rendered = ApiSurfaceTestHost.Render(Source, options);

        await Assert.That(rendered).IsEqualTo(Expected.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>Verifies a prefix only matches on a namespace boundary.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>Excluding <c>Sample.Int</c> must not take <c>Sample.Internals</c> with it.</remarks>
    [Test]
    public async Task NamespacePrefixMatchesOnBoundariesAsync()
    {
        var options = Read(("publicapisharp.excluded_namespace_prefixes", "Sample.Int"));

        await Assert.That(options.IsNamespaceExcluded("Sample.Int")).IsTrue();
        await Assert.That(options.IsNamespaceExcluded("Sample.Int.Nested")).IsTrue();
        await Assert.That(options.IsNamespaceExcluded("Sample.Internals")).IsFalse();
    }

    /// <summary>Verifies assembly attributes can be turned off wholesale.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task AssemblyAttributesCanBeSuppressedAsync()
    {
        const string Source = """
                              using System.Runtime.CompilerServices;

                              [assembly: InternalsVisibleTo("Sample.Tests")]

                              namespace Sample;

                              public class Thing
                              {
                              }
                              """;

        const string Expected = """
                                namespace Sample;

                                public class Thing
                                {
                                    public Thing() { }
                                }

                                """;

        var options = Read(("publicapisharp.include_assembly_attributes", "false"));
        var rendered = ApiSurfaceTestHost.Render(Source, options);

        await Assert.That(rendered).IsEqualTo(Expected.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>Verifies the defaults exclude nothing and keep assembly attributes.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task DefaultsExcludeNothingAsync()
    {
        var options = Read();

        await Assert.That(options.IncludeAssemblyAttributes).IsTrue();
        await Assert.That(options.IsAttributeExcluded(ObsoleteAttributeName)).IsFalse();
        await Assert.That(options.IsAttributeIncluded(ObsoleteAttributeName)).IsFalse();
        await Assert.That(options.IsNamespaceExcluded("Sample")).IsFalse();
    }

    /// <summary>Builds options from the given editorconfig entries.</summary>
    /// <param name="entries">The key/value pairs to configure.</param>
    /// <returns>The options.</returns>
    private static ApiRenderOptions Read(params (string Key, string Value)[] entries)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            builder[key] = value;
        }

        return ApiRenderOptions.Read(new StubAnalyzerConfigOptions(builder.ToImmutable()));
    }

    /// <summary>An in-memory <see cref="AnalyzerConfigOptions"/> holding a fixed set of entries.</summary>
    private sealed class StubAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        /// <summary>The configured entries.</summary>
        private readonly ImmutableDictionary<string, string> _entries;

        /// <summary>Initializes a new instance of the <see cref="StubAnalyzerConfigOptions"/> class.</summary>
        /// <param name="entries">The configured entries.</param>
        internal StubAnalyzerConfigOptions(ImmutableDictionary<string, string> entries) => _entries = entries;

        /// <inheritdoc/>
        public override bool TryGetValue(string key, out string value) => _entries.TryGetValue(key, out value!);
    }
}
