// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Unit tests for <see cref="ApiTextParser"/>, which reads surface text into declarations.</summary>
/// <remarks>
/// The same parser runs over the freshly rendered surface and over the checked-in baseline, so what
/// it calls a declaration's identity decides whether a difference reads as a change to one member or
/// as one member replacing another.
/// </remarks>
public class ApiTextParserTests
{
    /// <summary>The identity of the single-int overload used across several of these tests.</summary>
    private const string GoIntIdentity = "Sample.Thing.Go(int)";

    /// <summary>Verifies overloads are separate members, because their parameter types differ.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task OverloadsGetDistinctIdentitiesAsync()
    {
        const string Text = """
                            namespace Sample;

                            public class Thing
                            {
                                public void Go(int value) { }
                                public void Go(string value) { }
                            }

                            """;

        var identities = Identities(Text);

        await Assert.That(identities).Contains(GoIntIdentity);
        await Assert.That(identities).Contains("Sample.Thing.Go(string)");
    }

    /// <summary>Verifies a renamed parameter keeps the member's identity.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// A parameter name is part of the API — callers use it for named arguments — but it does not
    /// make a different member, so the difference has to read as a change rather than as a removal
    /// plus an addition.
    /// </remarks>
    [Test]
    public async Task RenamedParameterKeepsTheIdentityAsync()
    {
        const string Before = """
                              namespace Sample;

                              public class Thing
                              {
                                  public void Go(int value) { }
                              }

                              """;

        const string After = """
                             namespace Sample;

                             public class Thing
                             {
                                 public void Go(int count) { }
                             }

                             """;

        var before = Single(Before, GoIntIdentity);
        var after = Single(After, GoIntIdentity);

        await Assert.That(before.Identity).IsEqualTo(after.Identity);
        await Assert.That(before.Text).IsNotEqualTo(after.Text);
    }

    /// <summary>Verifies a reference kind makes a different member, because it changes the signature.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ReferenceKindChangesTheIdentityAsync()
    {
        const string Text = """
                            namespace Sample;

                            public class Thing
                            {
                                public void Go(int value) { }
                                public void Go(ref int value) { }
                            }

                            """;

        var identities = Identities(Text);

        await Assert.That(identities).Contains(GoIntIdentity);
        await Assert.That(identities).Contains("Sample.Thing.Go(ref int)");
    }

    /// <summary>Verifies generic arity separates members that otherwise look alike.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task GenericArityChangesTheIdentityAsync()
    {
        const string Text = """
                            namespace Sample;

                            public class Thing
                            {
                                public void Go() { }
                                public void Go<T>() { }
                            }

                            """;

        var identities = Identities(Text);

        await Assert.That(identities).Contains("Sample.Thing.Go()");
        await Assert.That(identities).Contains("Sample.Thing.Go`1()");
    }

    /// <summary>Verifies a hand-edited multi-declarator field line yields one entry per field.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>The renderer never writes this form, but a person editing the baseline might.</remarks>
    [Test]
    public async Task MultipleDeclaratorsYieldSeparateEntriesAsync()
    {
        const string Text = """
                            namespace Sample;

                            public class Thing
                            {
                                public int First, Second;
                            }

                            """;

        var identities = Identities(Text);

        await Assert.That(identities).Contains("Sample.Thing.First");
        await Assert.That(identities).Contains("Sample.Thing.Second");
    }

    /// <summary>Verifies unreadable text reports the error rather than looking like an empty surface.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task MalformedTextReportsAnErrorAsync()
    {
        const string Text = """
                            namespace Sample;

                            public class Thing
                            {
                                public int Value { get; set

                            """;

        var result = ApiTextParser.Parse(SourceText.From(Text), CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Declarations).IsEmpty();
    }

    /// <summary>Verifies an assembly attribute is identified by its whole application.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>The same attribute type can be applied more than once with different arguments.</remarks>
    [Test]
    public async Task AssemblyAttributesAreIdentifiedByTheirArgumentsAsync()
    {
        const string Text = """
                            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("One")]
                            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Two")]

                            """;

        const int ExpectedCount = 2;

        var identities = Identities(Text);

        await Assert.That(identities).Count().IsEqualTo(ExpectedCount);
    }

    /// <summary>Verifies every declaration kind the renderer can emit is read back.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The parser runs over the rendered surface as well as the baseline, so a kind it cannot read
    /// would make that declaration invisible to the comparison in both directions.
    /// </remarks>
    [Test]
    public async Task EveryDeclarationKindIsReadBackAsync()
    {
        const string Text = """
                            namespace Sample;

                            public delegate void Handler(int value);

                            public enum Colour : byte
                            {
                                Red = 1,
                                Green = 2,
                            }

                            public class Thing
                            {
                                public Thing() { }
                                public const int Limit = 3;
                                public int Field;
                                public int Property { get; set; }
                                public int this[int index] { get; }
                                public event System.EventHandler Simple;
                                public event System.EventHandler Custom { add { } remove { } }
                                public static Sample.Thing operator +(Sample.Thing a, Sample.Thing b) { }
                                public static explicit operator string(Sample.Thing value) { }
                                public class Nested
                                {
                                }
                            }

                            """;

        var identities = Identities(Text);

        await Assert.That(identities).Contains("Sample.Handler");
        await Assert.That(identities).Contains("Sample.Colour");
        await Assert.That(identities).Contains("Sample.Colour.Red");
        await Assert.That(identities).Contains("Sample.Colour.Green");
        await Assert.That(identities).Contains("Sample.Thing..ctor()");
        await Assert.That(identities).Contains("Sample.Thing.Limit");
        await Assert.That(identities).Contains("Sample.Thing.Field");
        await Assert.That(identities).Contains("Sample.Thing.Property");
        await Assert.That(identities).Contains("Sample.Thing.this(int)");
        await Assert.That(identities).Contains("Sample.Thing.Simple");
        await Assert.That(identities).Contains("Sample.Thing.Custom");
        await Assert.That(identities).Contains("Sample.Thing.op+(Sample.Thing,Sample.Thing)");
        await Assert.That(identities).Contains("Sample.Thing.opexplicit(Sample.Thing)->string");
        await Assert.That(identities).Contains("Sample.Thing.Nested");
    }

    /// <summary>Verifies a generic delegate's arity is part of its identity.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task GenericDelegateArityIsPartOfTheIdentityAsync()
    {
        const string Text = """
                            namespace Sample;

                            public delegate void Handler();

                            public delegate void Handler<T>(T value);

                            """;

        var identities = Identities(Text);

        await Assert.That(identities).Contains("Sample.Handler");
        await Assert.That(identities).Contains("Sample.Handler`1");
    }

    /// <summary>Verifies an explicit interface implementation is identified by its qualified name.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ExplicitImplementationsAreIdentifiedSeparatelyAsync()
    {
        const string Text = """
                            namespace Sample;

                            public class Thing
                            {
                                public int Value { get; }
                                int Sample.IThing.Value { get; }
                                void Sample.IThing.Go() { }
                                event System.EventHandler Sample.IThing.Changed;
                            }

                            """;

        var identities = Identities(Text);

        await Assert.That(identities).Contains("Sample.Thing.Value");
        await Assert.That(identities).Contains("Sample.Thing.Sample.IThing.Value");
        await Assert.That(identities).Contains("Sample.Thing.Sample.IThing.Go()");
        await Assert.That(identities).Contains("Sample.Thing.Sample.IThing.Changed");
    }

    /// <summary>Verifies a nested namespace contributes to the container path.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task NestedNamespacesBuildTheContainerPathAsync()
    {
        const string Text = """
                            namespace Outer
                            {
                                namespace Inner
                                {
                                    public class Thing
                                    {
                                    }
                                }
                            }

                            """;

        await Assert.That(Identities(Text)).Contains("Outer.Inner.Thing");
    }

    /// <summary>Verifies a parameterless declaration list renders an empty identity suffix.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ParameterlessMembersGetEmptyParenthesesAsync()
    {
        const string Text = """
                            namespace Sample;

                            public class Thing
                            {
                                public void Go() { }
                            }

                            """;

        await Assert.That(Identities(Text)).Contains("Sample.Thing.Go()");
    }

    /// <summary>Parses the text and returns every declaration's identity.</summary>
    /// <param name="text">The API surface text.</param>
    /// <returns>The identities.</returns>
    private static List<string> Identities(string text)
    {
        var result = ApiTextParser.Parse(SourceText.From(text), CancellationToken.None);
        var identities = new List<string>();
        foreach (var declaration in result.Declarations)
        {
            identities.Add(declaration.Identity);
        }

        return identities;
    }

    /// <summary>Parses the text and returns the one declaration with the given identity.</summary>
    /// <param name="text">The API surface text.</param>
    /// <param name="identity">The identity to find.</param>
    /// <returns>The declaration.</returns>
    private static ApiDeclaration Single(string text, string identity)
    {
        var result = ApiTextParser.Parse(SourceText.From(text), CancellationToken.None);
        foreach (var declaration in result.Declarations)
        {
            if (string.Equals(declaration.Identity, identity, StringComparison.Ordinal))
            {
                return declaration;
            }
        }

        throw new InvalidOperationException($"No declaration with identity '{identity}'.");
    }
}
