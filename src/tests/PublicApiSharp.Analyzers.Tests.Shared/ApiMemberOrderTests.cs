// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Unit tests for <see cref="ApiMemberOrder"/>, which fixes the order members render in.</summary>
/// <remarks>
/// The order has to be a pure function of the symbols. If it ever depended on source order, moving a
/// method up a file would show up in the diff as an API change.
/// </remarks>
public class ApiMemberOrderTests
{
    /// <summary>The metadata name of the type whose members these tests order.</summary>
    private const string ThingMetadataName = "Sample.Thing";

    /// <summary>The type whose members these tests order.</summary>
    private const string Source = """
                                  using System;

                                  namespace Sample;

                                  public class Thing
                                  {
                                      public Thing() { }
                                      public Thing(int value) { }
                                      public int Field;
                                      public static int StaticField;
                                      public int Property { get; set; }
                                      public event EventHandler? Changed;
                                      public void Go() { }
                                      public void Go(int value) { }
                                      public void Go(string value) { }
                                      public void Go<T>(T value) { }
                                      public void Go(int first, int second) { }
                                      public static void StaticGo() { }
                                      public class Nested { }
                                  }
                                  """;

    /// <summary>Verifies members group by kind in the order a reader scans a type.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task MembersGroupByKindAsync()
    {
        var rendered = ApiSurfaceTestHost.Render(Source);

        var constructor = rendered.IndexOf("public Thing()", StringComparison.Ordinal);
        var field = rendered.IndexOf("public int Field;", StringComparison.Ordinal);
        var property = rendered.IndexOf("public int Property", StringComparison.Ordinal);
        var eventMember = rendered.IndexOf("public event", StringComparison.Ordinal);
        var method = rendered.IndexOf("public void Go()", StringComparison.Ordinal);
        var nested = rendered.IndexOf("public class Nested", StringComparison.Ordinal);

        await Assert.That(constructor).IsLessThan(field);
        await Assert.That(field).IsLessThan(property);
        await Assert.That(property).IsLessThan(eventMember);
        await Assert.That(eventMember).IsLessThan(method);
        await Assert.That(method).IsLessThan(nested);
    }

    /// <summary>Verifies instance members precede static ones within a group.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task InstanceMembersPrecedeStaticOnesAsync()
    {
        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered.IndexOf("public int Field;", StringComparison.Ordinal))
            .IsLessThan(rendered.IndexOf("public static int StaticField;", StringComparison.Ordinal));
        await Assert.That(rendered.IndexOf("public void Go()", StringComparison.Ordinal))
            .IsLessThan(rendered.IndexOf("public static void StaticGo()", StringComparison.Ordinal));
    }

    /// <summary>Verifies overloads order by arity, then parameter count, then parameter types.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task OverloadsOrderByShapeAsync()
    {
        var rendered = ApiSurfaceTestHost.Render(Source);

        var none = rendered.IndexOf("public void Go() { }", StringComparison.Ordinal);
        var oneInt = rendered.IndexOf("public void Go(int value)", StringComparison.Ordinal);
        var oneString = rendered.IndexOf("public void Go(string value)", StringComparison.Ordinal);
        var twoInts = rendered.IndexOf("public void Go(int first, int second)", StringComparison.Ordinal);
        var generic = rendered.IndexOf("public void Go<T>(T value)", StringComparison.Ordinal);

        // Fewer parameters first, then by parameter type; the generic overload sorts after the
        // non-generic ones because arity is compared before parameters.
        await Assert.That(none).IsLessThan(oneInt);
        await Assert.That(oneInt).IsLessThan(oneString);
        await Assert.That(oneString).IsLessThan(twoInts);
        await Assert.That(twoInts).IsLessThan(generic);
    }

    /// <summary>Verifies the comparer is total, so sorting can never depend on input order.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ComparerHandlesMissingSymbolsAsync()
    {
        var compilation = ApiSurfaceTestHost.Compile(Source);
        var thing = compilation.GetTypeByMetadataName(ThingMetadataName)!;
        var member = thing.GetMembers("Property")[0];

        await Assert.That(ApiMemberOrder.Instance.Compare(null, null)).IsEqualTo(0);
        await Assert.That(ApiMemberOrder.Instance.Compare(null, member)).IsLessThan(0);
        await Assert.That(ApiMemberOrder.Instance.Compare(member, null)).IsGreaterThan(0);
        await Assert.That(ApiMemberOrder.Instance.Compare(member, member)).IsEqualTo(0);
    }

    /// <summary>Verifies a method compared with itself runs the whole signature comparison.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// Every earlier tie-break returns equal, so this is the path that walks the parameter list to
    /// the end — the case a sort hits whenever it re-examines an element.
    /// </remarks>
    [Test]
    public async Task IdenticalMethodsCompareEqualAsync()
    {
        const int TwoParameters = 2;

        var compilation = ApiSurfaceTestHost.Compile(Source);
        var thing = compilation.GetTypeByMetadataName(ThingMetadataName)!;

        ISymbol? twoParameters = null;
        foreach (var candidate in thing.GetMembers("Go"))
        {
            if (candidate is IMethodSymbol method && method.Parameters.Length == TwoParameters)
            {
                twoParameters = method;
                break;
            }
        }

        await Assert.That(twoParameters).IsNotNull();
        await Assert.That(ApiMemberOrder.Instance.Compare(twoParameters, twoParameters)).IsEqualTo(0);
    }

    /// <summary>Verifies a symbol kind outside the surface still compares without throwing.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The renderer never asks the comparer about a parameter, but a total order must not depend on
    /// that staying true.
    /// </remarks>
    [Test]
    public async Task UnknownKindsSortLastAsync()
    {
        var compilation = ApiSurfaceTestHost.Compile(Source);
        var thing = compilation.GetTypeByMetadataName(ThingMetadataName)!;
        var property = thing.GetMembers("Property")[0];
        ISymbol? parameter = null;
        foreach (var candidate in thing.GetMembers("Go"))
        {
            if (candidate is IMethodSymbol { Parameters.Length: 1 } method)
            {
                parameter = method.Parameters[0];
                break;
            }
        }

        await Assert.That(parameter).IsNotNull();
        await Assert.That(ApiMemberOrder.Instance.Compare(property, parameter)).IsLessThan(0);
        await Assert.That(ApiMemberOrder.Instance.Compare(parameter, property)).IsGreaterThan(0);
    }
}
