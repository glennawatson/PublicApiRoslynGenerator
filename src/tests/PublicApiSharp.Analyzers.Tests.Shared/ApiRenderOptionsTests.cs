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

    /// <summary>The configuration key for namespace subtrees omitted from the baseline.</summary>
    private const string ExcludedNamespacePrefixesKey = "publicapisharp.excluded_namespace_prefixes";

    /// <summary>The configuration key for attributes explicitly included in the surface.</summary>
    private const string IncludedAttributesKey = "publicapisharp.included_attributes";

    /// <summary>The configuration key controlling assembly attributes.</summary>
    private const string IncludeAssemblyAttributesKey = "publicapisharp.include_assembly_attributes";

    /// <summary>The configuration key controlling generated declarations.</summary>
    private const string IncludeGeneratedCodeKey = "publicapisharp.include_generated_code";

    /// <summary>A name matched by the configured option tests.</summary>
    private const string MarkerName = "Sample.Marker";

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

        var options = Read((IncludedAttributesKey, "System.Reflection.AssemblyVersionAttribute"));
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
            (IncludedAttributesKey, "Contoso.*"));

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

        var options = Read((IncludeAssemblyAttributesKey, "false"));
        var rendered = ApiSurfaceTestHost.Render(Source, options);

        await Assert.That(rendered).IsEqualTo(Expected.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>Verifies a type a build tool generated stays out of the surface.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The shape here is WPF's: a public helper the XAML build task writes into the assembly only
    /// while some XAML file happens to reference an internal type. It is public, so nothing about
    /// accessibility keeps it out, and it appears and disappears for reasons that have nothing to do
    /// with the library's API — which is exactly what a baseline must not record.
    /// </remarks>
    [Test]
    public async Task GeneratedTypeIsNotRenderedAsync()
    {
        const string Source = """
                              namespace XamlGeneratedNamespace
                              {
                                  [System.CodeDom.Compiler.GeneratedCode("PresentationBuildTasks", "4.0.0.0")]
                                  [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
                                  public sealed class GeneratedInternalTypeHelper
                                  {
                                      public void Help() { }
                                  }
                              }

                              namespace Sample
                              {
                                  public class Thing
                                  {
                                  }
                              }
                              """;

        const string Expected = """
                                namespace Sample;

                                public class Thing
                                {
                                    public Thing() { }
                                }

                                """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).IsEqualTo(Expected.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>Verifies a generated member of a hand-written type stays out of the surface.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// A tool that adds to a partial type marks the members it wrote rather than the type, so the
    /// exclusion has to be decided per declaration and not only at the top.
    /// </remarks>
    [Test]
    public async Task GeneratedMemberIsNotRenderedAsync()
    {
        const string Source = """
                              namespace Sample;

                              public partial class Thing
                              {
                                  public int Written { get; set; }

                                  [System.CodeDom.Compiler.GeneratedCode("Tool", "1.0")]
                                  public int Emitted { get; set; }
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).Contains("Written");
        await Assert.That(rendered).DoesNotContain("Emitted");
    }

    /// <summary>Verifies a project whose generator emits real API can ask for it back.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// A source generator's output can be API a consumer calls directly, and a library built that
    /// way needs it tracked. The default suits the far more common case, where what is generated is
    /// the build's own plumbing.
    /// </remarks>
    [Test]
    public async Task GeneratedCodeCanBeIncludedAsync()
    {
        const string Source = """
                              namespace Sample;

                              [System.CodeDom.Compiler.GeneratedCode("Tool", "1.0")]
                              public class Generated
                              {
                                  public void Go() { }
                              }
                              """;

        var options = Read((IncludeGeneratedCodeKey, "true"));
        var rendered = ApiSurfaceTestHost.Render(Source, options);

        await Assert.That(rendered).Contains("public class Generated");
        await Assert.That(rendered).Contains("public void Go()");
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

    /// <summary>Verifies options set in an ordinary <c>.editorconfig</c> section take effect.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// This is where a reader will put them, and what the documentation shows. A global config is
    /// the other route; both have to work, or configuration silently does nothing.
    /// </remarks>
    [Test]
    public Task OptionsFromASectionedEditorConfigApplyAsync()
    {
        const string Source = """
                              namespace Sample;

                              [Sample.Marker]
                              public class Thing
                              {
                              }

                              public class MarkerAttribute : System.Attribute
                              {
                              }
                              """;

        const string Baseline = """
                                namespace Sample;

                                public class MarkerAttribute : System.Attribute
                                {
                                    public MarkerAttribute() { }
                                }

                                public class Thing
                                {
                                    public Thing() { }
                                }

                                """;

        const string EditorConfig = """
                                    root = true

                                    [*.cs]
                                    publicapisharp.excluded_attributes = Sample.MarkerAttribute
                                    """;

        return PublicApiVerifier.AnalyzeWithEditorConfigAsync(Source, Baseline, EditorConfig);
    }

    /// <summary>Verifies only namespaces with retained public declarations decide the namespace syntax.</summary>
    /// <param name="source">The namespace arrangement, including declarations excluded from the surface.</param>
    /// <param name="names">The retained namespace names, separated by commas.</param>
    /// <param name="fileScoped">Whether the sole retained namespace can be file-scoped.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("namespace Hidden { internal class C { } } namespace Visible { public interface I { } }", "Visible", true)]
    [Arguments("namespace A { namespace B { namespace C { namespace D { public interface I { } } } } }", "A.B.C.D", true)]
    [Arguments("namespace Left.Leaf { public interface I { } } namespace Right.Leaf { public interface I { } }", "Left.Leaf,Right.Leaf", false)]
    [Arguments("namespace @class.@namespace { public interface @interface { } }", "@class.@namespace", true)]
    [Arguments("public interface Global { } namespace Visible { public interface I { } }", "Visible", false)]
    [Arguments("namespace Hidden.Generated { [System.CodeDom.Compiler.GeneratedCode(\"tool\", \"1\")] public interface G { } } namespace Visible { public interface I { } }", "Visible", true)]
    [Arguments("namespace Hidden { internal class C { } namespace Nested { internal class D { } } }", "", false)]
    public async Task RetainedNamespacesDetermineFileScopedRenderingAsync(string source, string names, bool fileScoped)
    {
        var rendered = ApiSurfaceTestHost.Render(source);
        var root = await Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(rendered).GetRootAsync();
        var namespaces = new List<string>();
        var fileScopedCount = 0;
        foreach (var node in root.DescendantNodes())
        {
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.BaseNamespaceDeclarationSyntax declaration)
            {
                namespaces.Add(declaration.Name.ToString());
                if (declaration is Microsoft.CodeAnalysis.CSharp.Syntax.FileScopedNamespaceDeclarationSyntax)
                {
                    fileScopedCount++;
                }
            }
        }

        await Assert.That(namespaces).IsEquivalentTo(names.Split(',', StringSplitOptions.RemoveEmptyEntries));
        await Assert.That(fileScopedCount).IsEqualTo(fileScoped ? 1 : 0);
        await Assert.That(root.GetDiagnostics()).IsEmpty();
        await PublicApiVerifier.AnalyzeAsync(source, rendered);
    }

    /// <summary>Verifies excluding an ancestor prunes deep descendants while retaining a similar namespace name.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task ExcludedNamespacePrunesNestedDescendantsBeforeChoosingFileScopeAsync()
    {
        const string Source = """
            namespace Removed
            {
                public interface Root { }
                namespace Deep.Nested { public interface Child { } }
            }
            namespace RemovedNeighbor { public interface Kept { } }
            """;
        const string Remaining = "namespace RemovedNeighbor { public interface Kept { } }";
        var options = Read((ExcludedNamespacePrefixesKey, "Removed"));
        var rendered = ApiSurfaceTestHost.Render(Source, options);

        await Assert.That(rendered).IsEqualTo(ApiSurfaceTestHost.Render(Remaining));
        await Assert.That(rendered).StartsWith("namespace RemovedNeighbor;");
        await PublicApiVerifier.AnalyzeWithConfigAsync(
            Source,
            rendered,
            PublicApiVerifier.BaselineFileName,
            "publicapisharp.excluded_namespace_prefixes = Removed");
    }

    /// <summary>Verifies absent settings reuse the immutable defaults with either fallback shape.</summary>
    /// <param name="hasFileOptions">Whether an empty file-scoped source is supplied.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnconfiguredSourcesShareDefaultOptionsAsync(bool hasFileOptions)
    {
        var empty = new StubAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);
        var options = ApiRenderOptions.Read(empty, hasFileOptions ? empty : null);

        await Assert.That(options).IsSameReferenceAs(ApiRenderOptions.Default);
        await Assert.That(options.IncludeGeneratedCode).IsFalse();
    }

    /// <summary>Verifies explicit defaults, invalid flags and empty lists retain default behavior.</summary>
    /// <param name="key">The configured option suffix.</param>
    /// <param name="value">The default-equivalent value.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("include_assembly_attributes", "true")]
    [Arguments("include_assembly_attributes", "invalid")]
    [Arguments("include_generated_code", "false")]
    [Arguments("include_generated_code", "invalid")]
    [Arguments("excluded_attributes", " , , ")]
    [Arguments("included_attributes", "")]
    [Arguments("excluded_namespace_prefixes", " , ")]
    public async Task DefaultEquivalentSettingsShareDefaultOptionsAsync(string key, string value)
    {
        var empty = new StubAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);
        var configured = new StubAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty.Add($"publicapisharp.{key}", value));

        await Assert.That(ApiRenderOptions.Read(configured, empty)).IsSameReferenceAs(ApiRenderOptions.Default);
        await Assert.That(ApiRenderOptions.Read(empty, configured)).IsSameReferenceAs(ApiRenderOptions.Default);
    }

    /// <summary>Verifies every nondefault setting survives global and file-scoped reads.</summary>
    /// <param name="key">The configured option suffix.</param>
    /// <param name="value">The nondefault value.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("include_assembly_attributes", "false")]
    [Arguments("include_generated_code", "true")]
    [Arguments("excluded_attributes", "Sample.Marker")]
    [Arguments("included_attributes", "Sample.Marker")]
    [Arguments("excluded_namespace_prefixes", "Sample.Marker")]
    public async Task ConfiguredSourcesKeepEveryNondefaultSettingAsync(string key, string value)
    {
        var empty = new StubAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);
        var configured = new StubAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty.Add($"publicapisharp.{key}", value));
        var global = ApiRenderOptions.Read(configured, empty);
        var fileScoped = ApiRenderOptions.Read(empty, configured);

        foreach (var options in new[] { global, fileScoped })
        {
            await Assert.That(ReferenceEquals(options, ApiRenderOptions.Default)).IsFalse();
            await Assert.That(options.IncludeAssemblyAttributes).IsEqualTo(key != "include_assembly_attributes");
            await Assert.That(options.IncludeGeneratedCode).IsEqualTo(key == "include_generated_code");
            await Assert.That(options.IsAttributeExcluded(MarkerName)).IsEqualTo(key == "excluded_attributes");
            await Assert.That(options.IsAttributeIncluded(MarkerName)).IsEqualTo(key == "included_attributes");
            await Assert.That(options.IsNamespaceExcluded(MarkerName)).IsEqualTo(key == "excluded_namespace_prefixes");
        }
    }

    /// <summary>Verifies a present global value masks its file-scoped fallback even when it parses to a default.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task GlobalDefaultsAndInvalidFlagsOverrideFileSettingsAsync()
    {
        var global = new StubAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty
            .Add(IncludeAssemblyAttributesKey, "invalid")
            .Add(IncludeGeneratedCodeKey, bool.FalseString)
            .Add(ExcludedAttributesKey, string.Empty)
            .Add(IncludedAttributesKey, " , ")
            .Add(ExcludedNamespacePrefixesKey, string.Empty));
        var fileScoped = new StubAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty
            .Add(IncludeAssemblyAttributesKey, bool.FalseString)
            .Add(IncludeGeneratedCodeKey, bool.TrueString)
            .Add(ExcludedAttributesKey, "*")
            .Add(IncludedAttributesKey, "*")
            .Add(ExcludedNamespacePrefixesKey, "Sample"));

        await Assert.That(ApiRenderOptions.Read(global, fileScoped)).IsSameReferenceAs(ApiRenderOptions.Default);
        await Assert.That(ApiRenderOptions.Default.IsAttributeExcluded(ObsoleteAttributeName)).IsFalse();
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
