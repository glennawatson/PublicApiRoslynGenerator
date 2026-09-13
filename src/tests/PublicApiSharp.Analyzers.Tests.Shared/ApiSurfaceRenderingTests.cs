// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.CSharp;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>
/// Verifies the shape of the rendered surface: it has to read like ordinary C#, because the whole
/// point of the format is that a reviewer can diff it the way they read code.
/// </summary>
public class ApiSurfaceRenderingTests
{
    /// <summary>Verifies filtered type storage exposes only its populated entries in name and arity order.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task VisibleTypesSortOnlyRetainedEntriesAsync()
    {
        const int Expected = 3;
        const string Source = "internal class Hidden { } public class Z { } public class A<T> { } internal class Other { } public class A { }";
        var container = ApiSurfaceTestHost.Compile(Source).Assembly.GlobalNamespace;
        var types = ApiSurfaceRenderer.VisibleTypes(container, ApiRenderOptions.Default);

        await Assert.That(types.Count).IsEqualTo(Expected);
        await Assert.That(types[0].Name).IsEqualTo("A");
        await Assert.That(types[0].Arity).IsEqualTo(0);
        await Assert.That(types[1].Name).IsEqualTo("A");
        await Assert.That(types[1].Arity).IsEqualTo(1);
        await Assert.That(types[2].Name).IsEqualTo("Z");
    }

    /// <summary>Verifies unused array entries sort after visible types and compare equal to each other.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task UnusedTypeEntriesSortLastAsync()
    {
        var type = ApiSurfaceTestHost.Compile("public class Visible { }").GetTypeByMetadataName("Visible")!;

        await Assert.That(ApiSurfaceRenderer.CompareTypes(null, null)).IsEqualTo(0);
        await Assert.That(ApiSurfaceRenderer.CompareTypes(null, type)).IsEqualTo(1);
        await Assert.That(ApiSurfaceRenderer.CompareTypes(type, null)).IsEqualTo(-1);
    }

    /// <summary>Verifies hidden interfaces leave neither empty entries nor separators in a base list.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task BaseListSortsOnlyVisibleInterfacesAsync()
    {
        const string Source = "public interface IZ { } internal interface IHidden { } public interface IA { } public class Contract : IZ, IHidden, IA { }";
        var type = ApiSurfaceTestHost.Compile(Source).GetTypeByMetadataName("Contract")!;
        var builder = new PooledStringBuilder();
        ApiSurfaceRenderer.AppendBaseList(builder, type);

        await Assert.That(builder.ToString()).IsEqualTo(" : IA, IZ");
    }

    /// <summary>Verifies each type writes its own sorted members when sorting storage is reused.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task NestedAndSiblingTypesKeepTheirOwnMembersAsync()
    {
        const string Source = """
                              public interface IAlpha
                              {
                                  void Z();
                                  void A();
                                  public interface Nested { void C(); void B(); }
                              }
                              public interface IBeta { void Y(); }
                              public interface IGamma { }
                              public interface IOmega { void F(); void E(); void D(); void C(); }
                              """;
        const string Expected = """
                                public interface IAlpha
                                {
                                    void A() { }
                                    void Z() { }
                                    public interface Nested
                                    {
                                        void B() { }
                                        void C() { }
                                    }
                                }
                                public interface IBeta
                                {
                                    void Y() { }
                                }
                                public interface IGamma
                                {
                                }
                                public interface IOmega
                                {
                                    void C() { }
                                    void D() { }
                                    void E() { }
                                    void F() { }
                                }

                                """;

        await AssertRendersAsync(Source, Expected);
    }

    /// <summary>Verifies namespace selection uses the same visibility filter as type rendering.</summary>
    /// <param name="source">The types declared at global scope.</param>
    /// <param name="expected">Whether any type belongs in the surface.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("", false)]
    [Arguments("internal class Hidden { }", false)]
    [Arguments("internal class Hidden { } public class Visible { }", true)]
    public async Task NamespaceSelectionFindsOnlyVisibleTypesAsync(string source, bool expected)
    {
        var container = ApiSurfaceTestHost.Compile(source).Assembly.GlobalNamespace;

        await Assert.That(ApiSurfaceRenderer.VisibleTypes(container, ApiRenderOptions.Default).Count > 0).IsEqualTo(expected);
    }

    /// <summary>Verifies a parsed extension container is visible only when its baseline syntax is supported.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task ExtensionContainerVisibilityRequiresSupportedBaselineSyntaxAsync()
    {
        const string Source = "public static class Extensions { extension(string text) { public int Length() => 0; } }";
        var compilation = ApiSurfaceTestHost.Compile("public static class Extensions { }")
            .RemoveAllSyntaxTrees()
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(Source, new(LanguageVersion.Preview)));
        var container = compilation.GetTypeByMetadataName("Extensions")!;

        // The floor cannot parse an extension container; 4.14 exposes its symbol but not baseline syntax.
        await Assert.That(ApiSurfaceRenderer.VisibleTypes(container, ApiRenderOptions.Default).Count).IsEqualTo(RoslynFeatures.SupportsExtensionBlocks ? 1 : 0);
    }

    /// <summary>Verifies repeated leaf names have separate storage and namespace selections within each render.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task NamespaceCollectionRetainsTypesForRepeatedLeafNamesAsync()
    {
        const int NamespaceCount = 7;
        const int VisibleNamespaceCount = 2;
        const string Source = """
                              namespace A.Shared { public interface IFirst { } }
                              namespace B.Shared { public interface ISecond { } }
                              namespace C.Shared { internal interface IHidden { } }
                              """;
        var compilation = ApiSurfaceTestHost.Compile(Source);
        var namespaces = ApiSurfaceRenderer.CollectNamespaces(compilation.Assembly.GlobalNamespace, ApiRenderOptions.Default, CancellationToken.None);
        namespaces.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        var visible = new List<ApiSurfaceRenderer.NamespaceTypes>(VisibleNamespaceCount);
        foreach (var entry in namespaces)
        {
            if (entry.Types.Count > 0)
            {
                visible.Add(entry);
            }
        }

        await Assert.That(namespaces.Count).IsEqualTo(NamespaceCount);
        await Assert.That(visible.Count).IsEqualTo(VisibleNamespaceCount);
        await Assert.That(visible[0].Types[0].Name).IsEqualTo("IFirst");
        await Assert.That(visible[1].Types[0].Name).IsEqualTo("ISecond");
        await Assert.That(ApiSurfaceRenderer.UsesFileScopedNamespace(namespaces)).IsFalse();
    }

    /// <summary>Namespace ordering uses unescaped qualified names, including parent and prefix ties.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task NamespacesSortByUnescapedQualifiedNameAsync()
    {
        const string Source = """
                              namespace @class.Z { public interface ILast { } }
                              namespace A_ { public interface IUnderscore { } }
                              namespace A.Z { public interface INested { } }
                              namespace A { public interface IParent { } }
                              namespace A0 { public interface IDigit { } }
                              namespace @class { public interface IKeyword { } }
                              public interface IGlobal { }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);
        var namespaces = new List<string>();
        foreach (var line in rendered.Split('\n'))
        {
            if (line.StartsWith("namespace ", StringComparison.Ordinal))
            {
                namespaces.Add(line);
            }
        }

        await Assert.That(string.Join('\n', namespaces)).IsEqualTo("namespace A\nnamespace A.Z\nnamespace A0\nnamespace A_\nnamespace @class\nnamespace @class.Z");
        await Assert.That(rendered.StartsWith("public interface IGlobal", StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>Verifies a plain class renders with its implicit constructor and its property.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ClassRendersMembersAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Thing
                              {
                                  public int Value { get; set; }
                              }
                              """;

        const string Expected = """
                                namespace Sample;

                                public class Thing
                                {
                                    public Thing() { }
                                    public int Value { get; set; }
                                }

                                """;

        await AssertRendersAsync(Source, Expected);
    }

    /// <summary>Verifies an interface keeps its static abstract member's modifiers and its constraints.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// Roslyn's symbol display drops modifiers on interface members, so a static abstract method
    /// would otherwise render as a bare signature and lose what makes it a generic-math target.
    /// </remarks>
    [Test]
    public async Task InterfaceRendersStaticAbstractAndConstraintsAsync()
    {
        const string Source = """
                              using System;

                              namespace Sample;

                              public interface IThing<T> where T : class, new()
                              {
                                  static abstract T Create();
                                  T? Value { get; init; }
                                  event EventHandler<T> Changed;
                              }
                              """;

        const string Expected = """
                                namespace Sample;

                                public interface IThing<T> where T : class, new()
                                {
                                    T? Value { get; init; }
                                    event System.EventHandler<T> Changed;
                                    static abstract T Create() { }
                                }

                                """;

        await AssertRendersAsync(Source, Expected);
    }

    /// <summary>Verifies a positional record struct renders without its synthesized equality members.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task RecordStructOmitsSynthesizedMembersAsync()
    {
        const string Source = """
                              namespace Sample;

                              public readonly record struct Point(double X, double Y);
                              """;

        const string Expected = """
                                namespace Sample;

                                public readonly record struct Point : System.IEquatable<Sample.Point>
                                {
                                    public Point(double X, double Y) { }
                                    public double X { get; init; }
                                    public double Y { get; init; }
                                }

                                """;

        await AssertRendersAsync(Source, Expected);
    }

    /// <summary>Verifies an enum keeps its declared order, its values and its underlying type.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task EnumRendersValuesInDeclaredOrderAsync()
    {
        const string Source = """
                              namespace Sample;

                              public enum Color : byte { Red = 1, Green = 2 }
                              """;

        const string Expected = """
                                namespace Sample;

                                public enum Color : byte
                                {
                                    Red = 1,
                                    Green = 2,
                                }

                                """;

        await AssertRendersAsync(Source, Expected);
    }

    /// <summary>Verifies a delegate renders as one declaration rather than its synthesized members.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task DelegateRendersAsSingleDeclarationAsync()
    {
        const string Source = """
                              using System.Threading.Tasks;

                              namespace Sample;

                              public delegate Task Handler<TArg>(TArg arg) where TArg : notnull;
                              """;

        const string Expected = """
                                namespace Sample;

                                public delegate System.Threading.Tasks.Task Handler<TArg>(TArg arg) where TArg : notnull;

                                """;

        await AssertRendersAsync(Source, Expected);
    }

    /// <summary>Verifies operators, an indexer, a conversion and a constant render in C# form.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task OperatorsAndIndexerRenderAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Money
                              {
                                  public const int Scale = 100;
                                  public int this[int index] => index;
                                  public static Money operator +(Money a, Money b) => a;
                                  public static explicit operator string(Money value) => "";
                              }
                              """;

        const string Expected = """
                                namespace Sample;

                                public class Money
                                {
                                    public Money() { }
                                    public const int Scale = 100;
                                    public int this[int index] { get; }
                                    public static Sample.Money operator +(Sample.Money a, Sample.Money b) { }
                                    public static explicit operator string(Sample.Money value) { }
                                }

                                """;

        await AssertRendersAsync(Source, Expected);
    }

    /// <summary>Verifies attributes render on their own lines, sorted, with the suffix trimmed.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task AttributesRenderOnTheirOwnLinesAsync()
    {
        const string Source = """
                              using System;
                              using System.Runtime.CompilerServices;

                              [assembly: InternalsVisibleTo("Sample.Tests")]

                              namespace Sample;

                              [Serializable]
                              [Obsolete("gone")]
                              public class Legacy
                              {
                              }
                              """;

        const string Expected = """
                                [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Sample.Tests")]
                                namespace Sample;

                                [System.Obsolete("gone")]
                                [System.Serializable]
                                public class Legacy
                                {
                                    public Legacy() { }
                                }

                                """;

        await AssertRendersAsync(Source, Expected);
    }

    /// <summary>Verifies nullability, defaults, reference kinds and an extension receiver survive.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task SignatureDetailSurvivesAsync()
    {
        const string Source = """
                              using System.Threading;

                              namespace Sample;

                              public static class Helpers
                              {
                                  public static string? Find(string key, int limit = 3, CancellationToken token = default) => null;
                                  public static void Split(scoped ref int value, in double factor, out bool ok) { ok = true; }
                                  public static int Twice(this int value) => value * 2;
                              }
                              """;

        const string Expected = """
                                namespace Sample;

                                public static class Helpers
                                {
                                    public static string? Find(string key, int limit = 3, System.Threading.CancellationToken token = default) { }
                                    public static void Split(scoped ref int value, in double factor, out bool ok) { }
                                    public static int Twice(this int value) { }
                                }

                                """;

        await AssertRendersAsync(Source, Expected);
    }

    /// <summary>Verifies internal and private members never reach the surface, but protected does.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// A consumer can derive from a public unsealed type and reach a protected member, so changing
    /// one breaks them exactly as a public member would.
    /// </remarks>
    [Test]
    public async Task OnlyExternallyVisibleMembersRenderAsync()
    {
        const string Source = """
                              namespace Sample;

                              internal class Hidden
                              {
                                  public int Nope { get; set; }
                              }

                              public class Visible
                              {
                                  private int _secret;
                                  internal int Internal { get; set; }
                                  protected int Protected { get; set; }
                                  public int Public { get; protected set; }
                              }
                              """;

        const string Expected = """
                                namespace Sample;

                                public class Visible
                                {
                                    public Visible() { }
                                    protected int Protected { get; set; }
                                    public int Public { get; protected set; }
                                }

                                """;

        await AssertRendersAsync(Source, Expected);
    }

    /// <summary>Verifies a second namespace forces the block-scoped form, which C# requires.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// One file may hold a single file-scoped namespace, so an assembly exposing two namespaces has
    /// to fall back to blocks. The fallback is what keeps the surface parseable, so it is worth
    /// pinning rather than leaving to chance.
    /// </remarks>
    [Test]
    public async Task SecondNamespaceFallsBackToBlockScopeAsync()
    {
        const string Source = """
                              namespace Sample.First
                              {
                                  public class One
                                  {
                                  }
                              }

                              namespace Sample.Second
                              {
                                  public class Two
                                  {
                                  }
                              }
                              """;

        const string Expected = """
                                namespace Sample.First
                                {
                                    public class One
                                    {
                                        public One() { }
                                    }
                                }
                                namespace Sample.Second
                                {
                                    public class Two
                                    {
                                        public Two() { }
                                    }
                                }

                                """;

        await AssertRendersAsync(Source, Expected);
    }

    /// <summary>Verifies the SDK's own assembly stamps stay out of the baseline.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The SDK writes these into every assembly it builds. Recording them would rewrite every
    /// baseline in a repository on each release — a version bump is not an API change — and would
    /// restate the target framework that the baseline's own folder already names.
    /// </remarks>
    [Test]
    public async Task BuildStampAttributesAreNotRecordedAsync()
    {
        const string Source = """
                              using System.Reflection;
                              using System.Runtime.Versioning;

                              [assembly: AssemblyVersion("1.0.0.0")]
                              [assembly: AssemblyMetadata("CommitHash", "abc123")]
                              [assembly: TargetFramework(".NETCoreApp,Version=v10.0", FrameworkDisplayName = ".NET 10.0")]
                              [assembly: System.CLSCompliant(true)]

                              namespace Sample;

                              public class Thing
                              {
                              }
                              """;

        const string Expected = """
                                [assembly: System.CLSCompliant(true)]
                                namespace Sample;

                                public class Thing
                                {
                                    public Thing() { }
                                }

                                """;

        await AssertRendersAsync(Source, Expected);
    }

    /// <summary>Verifies the rendered surface can be read back, which is what the diff depends on.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task RenderedSurfaceParsesBackAsync()
    {
        const string Source = """
                              using System;
                              using System.Collections.Generic;

                              namespace Sample;

                              public abstract class Repository<T> : IDisposable where T : class, new()
                              {
                                  public abstract IReadOnlyList<T> All { get; }
                                  public virtual T? Find(string id) => null;
                                  public void Dispose() { }
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);
        var parsed = ApiTextParser.Parse(Microsoft.CodeAnalysis.Text.SourceText.From(rendered), CancellationToken.None);

        await Assert.That(parsed.Success).IsTrue();
        await Assert.That(parsed.Error).IsNull();
    }

    /// <summary>Verifies a block whose members are all internal contributes nothing to the surface.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// A block declares no accessibility of its own and reads as public because its container is. What a
    /// consumer can reach is its members, so a block holding none of them is not surface, and recording
    /// its header commits the baseline to API nothing outside the assembly can call.
    /// </remarks>
    [Test]
    public async Task ExtensionBlockOfInternalMembersIsNotRenderedAsync()
    {
        if (!RoslynFeatures.SupportsExtensionBlocks)
        {
            return;
        }

        const string Source = """
                              namespace Sample;

                              public interface IResolver;

                              public static class Helpers
                              {
                                  extension(IResolver resolver)
                                  {
                                      internal IResolver Hidden() => resolver;
                                  }
                              }
                              """;

        const string Expected = """
                                namespace Sample;

                                public static class Helpers
                                {
                                }
                                public interface IResolver
                                {
                                }

                                """;

        await AssertRendersAsync(Source, Expected);
    }

    /// <summary>Renders the source and compares it to the expected surface.</summary>
    /// <param name="source">The C# source to render.</param>
    /// <param name="expected">The expected surface text.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    private static async Task AssertRendersAsync(string source, string expected)
    {
        var rendered = ApiSurfaceTestHost.Render(source);
        await Assert.That(rendered).IsEqualTo(expected.Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
