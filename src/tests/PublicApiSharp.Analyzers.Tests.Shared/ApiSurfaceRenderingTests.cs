// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>
/// Verifies the shape of the rendered surface: it has to read like ordinary C#, because the whole
/// point of the format is that a reviewer can diff it the way they read code.
/// </summary>
public class ApiSurfaceRenderingTests
{
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
