// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Rendering cases that only a wide spread of declarations reaches.</summary>
/// <remarks>
/// The common shapes are covered by <see cref="ApiSurfaceRenderingTests"/>. These pin the long tail:
/// every operator token, every constant literal kind, generic constraints, explicit interface
/// implementations and the full modifier set.
/// </remarks>
public class ApiSurfaceRenderingEdgeCaseTests
{
    /// <summary>Verifies every overloadable operator renders as its C# token.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task OperatorsRenderAsTheirTokensAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Vector
                              {
                                  public static Vector operator +(Vector a, Vector b) => a;
                                  public static Vector operator -(Vector a, Vector b) => a;
                                  public static Vector operator *(Vector a, Vector b) => a;
                                  public static Vector operator /(Vector a, Vector b) => a;
                                  public static Vector operator %(Vector a, Vector b) => a;
                                  public static Vector operator &(Vector a, Vector b) => a;
                                  public static Vector operator |(Vector a, Vector b) => a;
                                  public static Vector operator ^(Vector a, Vector b) => a;
                                  public static Vector operator <<(Vector a, int b) => a;
                                  public static Vector operator >>(Vector a, int b) => a;
                                  public static Vector operator >>>(Vector a, int b) => a;
                                  public static bool operator ==(Vector a, Vector b) => true;
                                  public static bool operator !=(Vector a, Vector b) => false;
                                  public static bool operator <(Vector a, Vector b) => true;
                                  public static bool operator >(Vector a, Vector b) => true;
                                  public static bool operator <=(Vector a, Vector b) => true;
                                  public static bool operator >=(Vector a, Vector b) => true;
                                  public static Vector operator +(Vector a) => a;
                                  public static Vector operator -(Vector a) => a;
                                  public static Vector operator !(Vector a) => a;
                                  public static Vector operator ~(Vector a) => a;
                                  public static Vector operator ++(Vector a) => a;
                                  public static Vector operator --(Vector a) => a;
                                  public static bool operator true(Vector a) => true;
                                  public static bool operator false(Vector a) => false;
                                  public static implicit operator int(Vector a) => 0;
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        foreach (var token in new[]
        {
            "operator +(", "operator -(", "operator *(", "operator /(", "operator %(",
            "operator &(", "operator |(", "operator ^(", "operator <<(", "operator >>(",
            "operator >>>(", "operator ==(", "operator !=(", "operator <(", "operator >(",
            "operator <=(", "operator >=(", "operator !(", "operator ~(", "operator ++(",
            "operator --(", "operator true(", "operator false(", "implicit operator int(",
        })
        {
            await Assert.That(rendered).Contains(token);
        }

        // No metadata name should survive into the surface.
        await Assert.That(rendered).DoesNotContain("op_");
    }

    /// <summary>Verifies each kind of constant renders as the literal a developer would write.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ConstantsRenderAsLiteralsAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Limits
                              {
                                  public const string Name = "with \"quotes\"";
                                  public const char Separator = '\n';
                                  public const bool Enabled = true;
                                  public const bool Disabled = false;
                                  public const string? Missing = null;
                                  public const int Count = -7;
                                  public const double Ratio = 1.5;
                                  public const long Big = 9000000000;
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).Contains("public const string Name = \"with \\\"quotes\\\"\";");
        await Assert.That(rendered).Contains("public const char Separator = '\\n';");
        await Assert.That(rendered).Contains("public const bool Enabled = true;");
        await Assert.That(rendered).Contains("public const bool Disabled = false;");
        await Assert.That(rendered).Contains("public const string? Missing = null;");
        await Assert.That(rendered).Contains("public const int Count = -7;");
        await Assert.That(rendered).Contains("public const double Ratio = 1.5;");
        await Assert.That(rendered).Contains("public const long Big = 9000000000;");
    }

    /// <summary>Verifies a generic method keeps its type parameters and constraints.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task GenericMethodKeepsConstraintsAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Factory
                              {
                                  public TResult Build<TInput, TResult>(TInput input)
                                      where TInput : notnull
                                      where TResult : class, new() => new();
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).Contains("public TResult Build<TInput, TResult>(TInput input)");
        await Assert.That(rendered).Contains("where TInput : notnull");
        await Assert.That(rendered).Contains("where TResult : class, new()");
    }

    /// <summary>Verifies explicit interface implementations keep their qualification.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// An explicit implementation carries no accessibility or modifiers, and the interface name is
    /// what distinguishes it from the ordinary member of the same name.
    /// </remarks>
    [Test]
    public async Task ExplicitInterfaceImplementationsKeepQualificationAsync()
    {
        const string Source = """
                              using System;

                              namespace Sample;

                              public interface IThing
                              {
                                  int Value { get; }

                                  event EventHandler Changed;

                                  void Go();
                              }

                              public class Thing : IThing
                              {
                                  int IThing.Value => 0;

                                  event EventHandler IThing.Changed { add { } remove { } }

                                  void IThing.Go() { }
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).Contains("int Sample.IThing.Value { get; }");
        await Assert.That(rendered).Contains("event System.EventHandler Sample.IThing.Changed;");
        await Assert.That(rendered).Contains("void Sample.IThing.Go() { }");
    }

    /// <summary>Verifies the inheritance and state modifiers all reach the surface.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ModifiersReachTheSurfaceAsync()
    {
        const string Source = """
                              namespace Sample;

                              public abstract class Base
                              {
                                  public abstract void Abstract();

                                  public virtual void Virtual() { }

                                  public static extern void External();
                              }

                              public class Derived : Base
                              {
                                  public override void Abstract() { }

                                  public sealed override void Virtual() { }

                                  public required int Required { get; set; }

                                  public readonly int ReadOnlyField;
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).Contains("public abstract void Abstract() { }");
        await Assert.That(rendered).Contains("public virtual void Virtual() { }");
        await Assert.That(rendered).Contains("public static extern void External() { }");
        await Assert.That(rendered).Contains("public override void Abstract() { }");
        await Assert.That(rendered).Contains("public sealed override void Virtual() { }");
        await Assert.That(rendered).Contains("public required int Required { get; set; }");
        await Assert.That(rendered).Contains("public readonly int ReadOnlyField;");
    }

    /// <summary>Verifies each type kind renders with the right keywords.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task TypeKindsRenderWithTheirKeywordsAsync()
    {
        const string Source = """
                              namespace Sample;

                              public static class StaticHolder
                              {
                              }

                              public sealed class SealedThing
                              {
                              }

                              public readonly struct ReadOnlyPoint
                              {
                              }

                              public ref struct RefWindow
                              {
                              }

                              public record class RecordThing(int Value);

                              public class Outer
                              {
                                  public class Nested
                                  {
                                  }
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).Contains("public static class StaticHolder");
        await Assert.That(rendered).Contains("public sealed class SealedThing");
        await Assert.That(rendered).Contains("public readonly struct ReadOnlyPoint");
        await Assert.That(rendered).Contains("public ref struct RefWindow");
        await Assert.That(rendered).Contains("public record RecordThing");
        await Assert.That(rendered).Contains("public class Nested");
    }

    /// <summary>Verifies events and a protected accessor render.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task EventsAndProtectedAccessorsRenderAsync()
    {
        const string Source = """
                              using System;

                              namespace Sample;

                              public class Publisher
                              {
                                  public event EventHandler? Simple;

                                  public event EventHandler? Custom { add { } remove { } }

                                  public int Value { get; protected set; }

                                  protected int Guarded { get; set; }
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).Contains("public event System.EventHandler? Simple;");
        await Assert.That(rendered).Contains("public event System.EventHandler? Custom;");
        await Assert.That(rendered).Contains("public int Value { get; protected set; }");
        await Assert.That(rendered).Contains("protected int Guarded { get; set; }");
    }

    /// <summary>Verifies every constraint kind renders in the order C# requires.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// Constraints decide what a caller may substitute, so tightening one is a breaking change that
    /// has to show up in the diff.
    /// </remarks>
    [Test]
    public async Task ConstraintKindsRenderAsync()
    {
        const string Source = """
                              using System;

                              namespace Sample;

                              public class Constrained
                              {
                                  public void Unmanaged<T>() where T : unmanaged { }

                                  public void Struct<T>() where T : struct { }

                                  public void Class<T>() where T : class { }

                                  public void NullableClass<T>() where T : class? { }

                                  public void NotNull<T>() where T : notnull { }

                                  public void Interface<T>() where T : IDisposable, IFormattable { }

                                  public void Constructible<T>() where T : class, new() { }

                                  public void Unconstrained<T>() { }
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).Contains("Unmanaged<T>() where T : unmanaged");
        await Assert.That(rendered).Contains("Struct<T>() where T : struct");
        await Assert.That(rendered).Contains("Class<T>() where T : class");
        await Assert.That(rendered).Contains("NullableClass<T>() where T : class?");
        await Assert.That(rendered).Contains("NotNull<T>() where T : notnull");
        await Assert.That(rendered).Contains("Interface<T>() where T : System.IDisposable, System.IFormattable");
        await Assert.That(rendered).Contains("Constructible<T>() where T : class, new()");
        await Assert.That(rendered).Contains("public void Unconstrained<T>() { }");
    }

    /// <summary>Verifies attribute arguments render, with named ones sorted after positional ones.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>Named arguments are unordered in source, so sorting keeps the baseline stable.</remarks>
    [Test]
    public async Task AttributeArgumentsRenderAsync()
    {
        const string Source = """
                              using System;

                              namespace Sample;

                              [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
                              public sealed class MarkerAttribute : Attribute
                              {
                                  public MarkerAttribute(string name, int order) { }

                                  public bool Enabled { get; set; }
                              }

                              [Marker("first", 1, Enabled = true)]
                              public class Decorated
                              {
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).Contains("[Sample.Marker(\"first\", 1, Enabled=true)]");
        await Assert.That(rendered).Contains("AllowMultiple=true");
        await Assert.That(rendered).Contains("Inherited=false");
    }

    /// <summary>Verifies a type with no attributes and no members still renders its braces.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task EmptyInterfaceRendersAsync()
    {
        const string Source = """
                              namespace Sample;

                              public interface IMarker
                              {
                              }
                              """;

        const string Expected = """
                                namespace Sample;

                                public interface IMarker
                                {
                                }

                                """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).IsEqualTo(Expected.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>Verifies a type at global scope renders without a namespace block.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task GlobalNamespaceTypeRendersUnwrappedAsync()
    {
        const string Source = """
                              public class Rootless
                              {
                              }
                              """;

        const string Expected = """
                                public class Rootless
                                {
                                    public Rootless() { }
                                }

                                """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).IsEqualTo(Expected.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>Verifies a base list with several entries is comma separated and sorted.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// Interface order in source carries no meaning, so sorting keeps the baseline from churning
    /// when the list is rearranged; the base class stays first because it is not interchangeable.
    /// </remarks>
    [Test]
    public async Task BaseListIsSeparatedAndSortedAsync()
    {
        const string Source = """
                              using System;

                              namespace Sample;

                              public abstract class Origin
                              {
                              }

                              public class Derived : Origin, IFormattable, IDisposable
                              {
                                  public void Dispose() { }

                                  public string ToString(string? format, IFormatProvider? provider) => "";
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered)
            .Contains("public class Derived : Sample.Origin, System.IDisposable, System.IFormattable");
    }

    /// <summary>Verifies a C# 14 extension block renders and reads back on a host that supports it.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The two halves of the feature arrive in different Roslyn versions: the symbol model exposes
    /// extension containers from 4.14, but the syntax to read one back only exists from 5.3. The
    /// assertion is therefore gated on the host actually having both, because a container that
    /// cannot be re-read is deliberately left out of the baseline rather than written in a form the
    /// parser would choke on.
    /// </remarks>
    [Test]
    public async Task ExtensionBlockRoundTripsWhereSupportedAsync()
    {
        if (!RoslynFeatures.SupportsExtensionBlocks)
        {
            return;
        }

        const string Source = """
                              namespace Sample;

                              public static class Helpers
                              {
                                  extension(string text)
                                  {
                                      public bool IsLong => text.Length > 10;

                                      public string Twice() => text + text;
                                  }
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).Contains("extension(string text)");
        await Assert.That(rendered).Contains("Twice()");

        // Whatever is written must be readable again, or the comparison would see it as removed.
        var parsed = ApiTextParser.Parse(Microsoft.CodeAnalysis.Text.SourceText.From(rendered), CancellationToken.None);

        await Assert.That(parsed.Success).IsTrue();
    }

    /// <summary>Verifies a symbol of a kind the member switch does not name renders by display string.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// Only fields, properties, events and methods survive the member filter, so a compilation cannot
    /// reach this arm. It is what stops an unfamiliar symbol kind from rendering as nothing at all.
    /// </remarks>
    [Test]
    public async Task MemberOfAnUnnamedKindRendersByDisplayStringAsync()
    {
        var compilation = ApiSurfaceTestHost.Compile("namespace Sample { public class Thing { } }");
        var builder = new PooledStringBuilder();

        ApiSurfaceRenderer.AppendMember(builder, compilation.GlobalNamespace);

        await Assert.That(builder.ToString()).IsNotEmpty();
    }

    /// <summary>Verifies a delegate with no invoke method renders as its bare name.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// A delegate declared in source always has an invoke method, so only an erroneous symbol lands
    /// here. Rendering the name keeps the declaration in the surface instead of dropping it.
    /// </remarks>
    [Test]
    public async Task DelegateWithoutAnInvokeMethodRendersItsNameAsync()
    {
        var compilation = ApiSurfaceTestHost.Compile("namespace Sample { public class Thing { } }");
        var thing = compilation.GetTypeByMetadataName("Sample.Thing");
        var builder = new PooledStringBuilder();

        await Assert.That(thing).IsNotNull();
        ApiSurfaceRenderer.AppendDelegate(builder, thing!);

        await Assert.That(builder.ToString()).EndsWith("Thing;");
    }

    /// <summary>Verifies a type named with a keyword renders in a form that reads back.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task KeywordNamedTypeRoundTripsAsync()
    {
        const string Source = """
                              namespace @class;

                              public class @struct
                              {
                                  public int @int { get; set; }

                                  public void @for(int @if) { }

                                  public event System.EventHandler? @event;

                                  public const int @null = 1;
                              }

                              public enum @void
                              {
                                  @true,
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);
        var parsed = ApiTextParser.Parse(Microsoft.CodeAnalysis.Text.SourceText.From(rendered), CancellationToken.None);

        await Assert.That(parsed.Success).IsTrue();
    }

    /// <summary>Verifies extension blocks order by the receiver they extend.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// An extension container's own name is compiler-generated, so ordering by it would shuffle the
    /// baseline whenever the compiler changed how it names one. Two containers in one class are what
    /// makes the comparison run at all, and therefore what pins the receiver as the sort key.
    /// </remarks>
    [Test]
    public async Task ExtensionBlocksOrderByReceiverAsync()
    {
        if (!RoslynFeatures.SupportsExtensionBlocks)
        {
            return;
        }

        const string Source = """
                              namespace Sample;

                              public static class Helpers
                              {
                                  extension(string text)
                                  {
                                      public bool IsLong => text.Length > 10;
                                  }

                                  extension(int value)
                                  {
                                      public bool IsBig => value > 10;
                                  }
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        var intReceiver = rendered.IndexOf("extension(int value)", StringComparison.Ordinal);
        var stringReceiver = rendered.IndexOf("extension(string text)", StringComparison.Ordinal);

        await Assert.That(intReceiver).IsGreaterThanOrEqualTo(0);
        await Assert.That(stringReceiver).IsGreaterThanOrEqualTo(0);
        await Assert.That(intReceiver).IsLessThan(stringReceiver);
    }

    /// <summary>Verifies a checked operator renders with the keyword that distinguishes it.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// A checked operator is a separate member from its unchecked form and is what a caller reaches
    /// inside a <c>checked</c> context. Rendering it without the keyword produces a line identical to
    /// the other one, and rendering its metadata name produces text that is not C# at all — which
    /// stops the whole surface reading back and silently ends enforcement for the assembly.
    /// </remarks>
    [Test]
    public async Task CheckedOperatorsRenderWithTheirKeywordAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Money
                              {
                                  public static Money operator +(Money a, Money b) => a;

                                  public static Money operator checked +(Money a, Money b) => a;

                                  public static explicit operator int(Money value) => 0;

                                  public static explicit operator checked int(Money value) => 0;
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);
        var parsed = ApiTextParser.Parse(Microsoft.CodeAnalysis.Text.SourceText.From(rendered), CancellationToken.None);

        await Assert.That(rendered).Contains("operator checked +(Sample.Money a, Sample.Money b)");
        await Assert.That(rendered).Contains("explicit operator checked int(Sample.Money value)");
        await Assert.That(parsed.Success).IsTrue();
    }

    /// <summary>Verifies a user-defined compound assignment operator renders as its token.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The operator token comes from the host compiler's own table, so an operator family C# gains
    /// later needs no change here. This is the family that arrived most recently, and on a host that
    /// cannot parse it there is nothing to render.
    /// </remarks>
    [Test]
    public async Task CompoundAssignmentOperatorRendersItsTokenAsync()
    {
        if (!RoslynFeatures.SupportsUserDefinedCompoundAssignment)
        {
            return;
        }

        const string Source = """
                              namespace Sample;

                              public class Counter
                              {
                                  public void operator +=(int amount) { }
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);
        var parsed = ApiTextParser.Parse(Microsoft.CodeAnalysis.Text.SourceText.From(rendered), CancellationToken.None);

        await Assert.That(rendered).Contains("operator +=(int amount)");
        await Assert.That(parsed.Success).IsTrue();
    }

    /// <summary>Verifies overloaded indexers order the same however they are written.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// Every indexer of a type shares one name, so the name cannot separate two of them and the
    /// parameters have to. Without that they tie, and a tie is settled by declaration order — making
    /// a swap in the source read as an API change.
    /// </remarks>
    [Test]
    public async Task IndexerOverloadsOrderIndependentlyOfSourceAsync()
    {
        const string IntFirst = """
                                namespace Sample;

                                public class Bag
                                {
                                    public int this[int index] => 0;

                                    public int this[string key] => 0;
                                }
                                """;

        const string StringFirst = """
                                   namespace Sample;

                                   public class Bag
                                   {
                                       public int this[string key] => 0;

                                       public int this[int index] => 0;
                                   }
                                   """;

        await Assert.That(ApiSurfaceTestHost.Render(IntFirst)).IsEqualTo(ApiSurfaceTestHost.Render(StringFirst));
    }

    /// <summary>Verifies overloads separated only by a reference kind order the same either way.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// How a parameter is passed does not show in its type, so two such overloads look alike to any
    /// ordering that compares types alone.
    /// </remarks>
    [Test]
    public async Task ReferenceKindOverloadsOrderIndependentlyOfSourceAsync()
    {
        const string ValueFirst = """
                                  namespace Sample;

                                  public class Runner
                                  {
                                      public void Go(int value) { }

                                      public void Go(ref int value) { }
                                  }
                                  """;

        const string RefFirst = """
                                namespace Sample;

                                public class Runner
                                {
                                    public void Go(ref int value) { }

                                    public void Go(int value) { }
                                }
                                """;

        await Assert.That(ApiSurfaceTestHost.Render(ValueFirst)).IsEqualTo(ApiSurfaceTestHost.Render(RefFirst));
    }

    /// <summary>Verifies a generic extension block declares the type parameters its receiver uses.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The receiver of a generic block names a type parameter, so a header written without the list
    /// declaring it refers to a name nothing introduces. That is not the surface the assembly has,
    /// and a compiler reading the baseline back would bind the name to whatever else it can find.
    /// </remarks>
    [Test]
    public async Task GenericExtensionBlockDeclaresItsTypeParametersAsync()
    {
        if (!RoslynFeatures.SupportsExtensionBlocks)
        {
            return;
        }

        const string Source = """
                              namespace Sample;

                              public interface IBuilder;

                              public static class Helpers
                              {
                                  extension<TBuilder>(TBuilder builder)
                                      where TBuilder : IBuilder
                                  {
                                      public TBuilder Configured() => builder;
                                  }
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).Contains("extension<TBuilder>(TBuilder builder) where TBuilder : Sample.IBuilder");
    }

    /// <summary>Verifies two blocks on one receiver that differ only by a constraint stay apart.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// A constraint decides which callers reach the members inside the block, so two blocks over the
    /// same receiver under different constraints expose different APIs. Writing them identically
    /// would leave the surface unable to say which members a caller actually gets.
    /// </remarks>
    [Test]
    public async Task ExtensionBlocksDifferingOnlyByConstraintRenderApartAsync()
    {
        if (!RoslynFeatures.SupportsExtensionBlocks)
        {
            return;
        }

        const string Source = """
                              namespace Sample;

                              public interface IBuilder;

                              public static class Helpers
                              {
                                  extension<TBuilder>(TBuilder builder)
                                      where TBuilder : IBuilder
                                  {
                                      public TBuilder Constrained() => builder;
                                  }

                                  extension<TBuilder>(TBuilder builder)
                                  {
                                      public TBuilder Unconstrained() => builder;
                                  }
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).Contains("extension<TBuilder>(TBuilder builder) where TBuilder : Sample.IBuilder");
        await Assert.That(rendered).Contains("extension<TBuilder>(TBuilder builder)\n");
    }

    /// <summary>Verifies the order two same-receiver blocks are written in does not reach the surface.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// Blocks are ordered by the receiver they extend, which ties whenever two of them share one.
    /// A tie broken by declaration order would make moving a block down a file read as an API change,
    /// so the constraints that tell the two apart have to take part in the ordering as well.
    /// </remarks>
    [Test]
    public async Task SameReceiverExtensionBlocksOrderIndependentlyOfSourceAsync()
    {
        if (!RoslynFeatures.SupportsExtensionBlocks)
        {
            return;
        }

        const string ConstrainedFirst = """
                                        namespace Sample;

                                        public interface IBuilder;

                                        public static class Helpers
                                        {
                                            extension<TBuilder>(TBuilder builder)
                                                where TBuilder : IBuilder
                                            {
                                                public TBuilder Constrained() => builder;
                                            }

                                            extension<TBuilder>(TBuilder builder)
                                            {
                                                public TBuilder Unconstrained() => builder;
                                            }
                                        }
                                        """;

        const string UnconstrainedFirst = """
                                          namespace Sample;

                                          public interface IBuilder;

                                          public static class Helpers
                                          {
                                              extension<TBuilder>(TBuilder builder)
                                              {
                                                  public TBuilder Unconstrained() => builder;
                                              }

                                              extension<TBuilder>(TBuilder builder)
                                                  where TBuilder : IBuilder
                                              {
                                                  public TBuilder Constrained() => builder;
                                              }
                                          }
                                          """;

        var constrainedFirst = ApiSurfaceTestHost.Render(ConstrainedFirst);
        var unconstrainedFirst = ApiSurfaceTestHost.Render(UnconstrainedFirst);

        await Assert.That(constrainedFirst).IsEqualTo(unconstrainedFirst);
    }

    /// <summary>Verifies a ref-struct permission renders where the host understands it.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// <c>allows ref struct</c> widens what a caller may substitute, so it belongs in the surface.
    /// The symbol API for it arrives in Roslyn 4.14, hence the gate.
    /// </remarks>
    [Test]
    public async Task RefStructPermissionRendersWhereSupportedAsync()
    {
        // The floor compiler cannot even parse the constraint, so there is nothing to render.
        if (!RoslynFeatures.SupportsRefStructConstraints)
        {
            return;
        }

        const string Source = """
                              namespace Sample;

                              public class Runner
                              {
                                  public void Run<T>(T value) where T : allows ref struct { }
                              }
                              """;

        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).Contains("where T : allows ref struct");
    }
}
