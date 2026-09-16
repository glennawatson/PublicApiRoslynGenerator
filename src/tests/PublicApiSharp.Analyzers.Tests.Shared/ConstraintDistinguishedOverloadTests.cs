// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Covers overloads that only a type parameter constraint tells apart.</summary>
/// <remarks>
/// <c>where T : class</c> and <c>where T : struct</c> give <c>T?</c> two different meanings, so a
/// pair of overloads over <c>T?</c> is legal C# whose parameter types print the same way. Only the
/// constraint separates them, which makes it part of what decides which baseline entry a member is
/// compared against.
/// </remarks>
public class ConstraintDistinguishedOverloadTests
{
    /// <summary>A type whose two overloads differ in nothing a signature prints except the constraint.</summary>
    private const string Source = """
                                  using System;

                                  public static class Example
                                  {
                                      public static IDisposable Subscribe<T>(IObservable<T?> source)
                                          where T : class => throw null!;

                                      public static IDisposable Subscribe<T>(IObservable<T?> source)
                                          where T : struct => throw null!;
                                  }
                                  """;

    /// <summary>Verifies each overload matches its own baseline entry and nothing is reported.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task MatchingBaselineReportsNothingAsync()
    {
        var compilation = ApiSurfaceTestHost.Compile(Source);
        var baseline = ApiSurfaceTestHost.Render(Source);

        await Assert.That(baseline).Contains("where T : class");
        await Assert.That(baseline).Contains("where T : struct");
        await ApiTextComparisonTests.AssertFullComparisonAsync(compilation, SourceText.From(baseline), 0);
    }

    /// <summary>Verifies rewriting one overload's constraint reports it as a single change.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task ConstraintChangeIsReportedAsOneChangeAsync()
    {
        var compilation = ApiSurfaceTestHost.Compile(Source);
        var baseline = ApiSurfaceTestHost.Render(Source).Replace("where T : class", "where T : notnull", StringComparison.Ordinal);

        await ApiTextComparisonTests.AssertFullComparisonAsync(compilation, SourceText.From(baseline), 1);
    }

    /// <summary>Verifies dropping a generic method's only constraint reports it as a single change.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task DroppedConstraintIsReportedAsOneChangeAsync()
    {
        const string Unconstrained = "public static class Example { public static T Pick<T>(T value) => value; }";
        var compilation = ApiSurfaceTestHost.Compile(Unconstrained);
        var baseline = ApiSurfaceTestHost.Render(Unconstrained)
            .Replace("Pick<T>(T value)", "Pick<T>(T value) where T : class", StringComparison.Ordinal);

        await ApiTextComparisonTests.AssertFullComparisonAsync(compilation, SourceText.From(baseline), 1);
    }
}
