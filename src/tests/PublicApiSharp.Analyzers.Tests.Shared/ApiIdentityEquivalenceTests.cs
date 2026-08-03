// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Holds together the two ways an identity is derived: from a symbol, and from the text.</summary>
/// <remarks>
/// The baseline is text, so its identities can only come from syntax. The surface has symbols behind
/// it, so its identities need not be read back out of text it just wrote. Both are worth having — the
/// second is what lets a comparison skip parsing what it just rendered — but only while they agree.
/// Where they disagree a member is filed under two keys and reads as removed and added at once, which
/// is a false report no regeneration can settle.
/// </remarks>
public class ApiIdentityEquivalenceTests
{
    /// <summary>A surface exercising every shape whose identity the two derivations must agree on.</summary>
    private const string Source = """
                                  using System;

                                  [assembly: CLSCompliant(false)]

                                  namespace Sample;

                                  public interface IThing
                                  {
                                      int Value { get; }

                                      void Go();
                                  }

                                  public interface IThing<T> : IThing
                                      where T : class
                                  {
                                      new T? Value { get; }
                                  }

                                  public delegate TResult Transform<in TInput, out TResult>(TInput input);

                                  public enum Severity : byte
                                  {
                                      Low = 1,
                                  }

                                  public abstract class Money : IThing, IComparable<Money>
                                  {
                                      public const decimal Limit = 12.5M;

                                      public int Field;

                                      public event EventHandler? Changed;

                                      public int Value { get; }

                                      public int this[int index] => index;

                                      public int this[string key] => key.Length;

                                      protected Money() { }

                                      protected Money(int seed) { }

                                      public abstract void Go();

                                      public int CompareTo(Money? other) => 0;

                                      public void Pass(ref int byRef, out int byOut, in int byIn, params int[] rest)
                                      {
                                          byOut = byRef + byIn + rest.Length;
                                      }

                                      public TResult Reshape<TSource, TResult>(TSource source, TResult fallback)
                                          where TSource : struct
                                          where TResult : class, new() => fallback;

                                      public static Money operator +(Money a, Money b) => a;

                                      public static Money operator checked +(Money a, Money b) => a;

                                      public static explicit operator int(Money value) => 0;

                                      public static explicit operator checked int(Money value) => 0;

                                      public static implicit operator string(Money value) => "";

                                      public sealed class Nested<TNested>
                                      {
                                          public TNested? Held { get; set; }
                                      }
                                  }

                                  public sealed class Register : IThing
                                  {
                                      int IThing.Value => 0;

                                      void IThing.Go() { }
                                  }
                                  """;

    /// <summary>The extension blocks, appended only where the host can render and read one back.</summary>
    private const string ExtensionSource = """

                                           public static class Helpers
                                           {
                                               extension<TShape>(TShape shape)
                                                   where TShape : IThing
                                               {
                                                   public bool Big => shape.Value > 2;

                                                   public TShape Twice() => shape;
                                               }

                                               extension<TShape>(TShape shape)
                                               {
                                                   public string Describe() => "";
                                               }

                                               extension(string text)
                                               {
                                                   public bool IsLong => text.Length > 10;
                                               }
                                           }
                                           """;

    /// <summary>The fewest declarations the fixture is expected to put through both derivations.</summary>
    /// <remarks>
    /// A guard that only asserts it checked something would still pass if the surface stopped mapping
    /// declarations to symbols, and then it would be guarding nothing.
    /// </remarks>
    private const int ExpectedMinimumChecked = 30;

    /// <summary>Verifies every identity derived from a symbol matches the one derived from the text.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task SymbolIdentitiesMatchParsedIdentitiesAsync()
    {
        var source = RoslynFeatures.SupportsExtensionBlocks ? Source + ExtensionSource : Source;
        var compilation = ApiSurfaceTestHost.Compile(source);
        var surface = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None);
        var parsed = ApiTextParser.Parse(SourceText.From(surface.Text), CancellationToken.None);

        await Assert.That(parsed.Success).IsTrue();
        await Assert.That(parsed.Declarations).IsNotEmpty();

        var mismatches = new List<string>();
        var checkedCount = 0;

        foreach (var declaration in parsed.Declarations)
        {
            // An assembly attribute has no symbol of its own; the rest are keyed by their symbol.
            if (surface.SymbolAtLine(declaration.StartLine) is not { } symbol)
            {
                continue;
            }

            checkedCount++;
            var fromSymbol = ApiIdentity.Of(symbol);
            if (!string.Equals(fromSymbol, declaration.Identity, StringComparison.Ordinal))
            {
                mismatches.Add($"{symbol.Kind} {symbol.Name}: text='{declaration.Identity}' symbol='{fromSymbol}'");
            }
        }

        await Assert.That(checkedCount).IsGreaterThanOrEqualTo(ExpectedMinimumChecked);
        await Assert.That(mismatches).IsEmpty();
    }
}
