// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>
/// Unit tests for <see cref="PublicApiBaselineAnalyzer"/>, which reports PAS0001 (public API not in
/// the baseline), PAS0002 (baseline API that no longer exists), PAS0003 (a declaration that differs
/// from the baseline), PAS0004 (no baseline for the target framework) and PAS0005 (a baseline that
/// cannot be read).
/// </summary>
public class PublicApiBaselineAnalyzerTests
{
    /// <summary>The surface a matching baseline describes, shared by most of these tests.</summary>
    private const string Baseline = """
                                    namespace Sample;

                                    public class Thing
                                    {
                                        public Thing() { }
                                        public int Value { get; set; }
                                    }

                                    """;

    /// <summary>Verifies a surface matching its baseline reports nothing.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task MatchingSurfaceIsSilentAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Thing
                              {
                                  public int Value { get; set; }
                              }
                              """;

        await PublicApiVerifier.AnalyzeAsync(Source, Baseline);
    }

    /// <summary>Verifies a member the baseline does not mention is reported on its own declaration.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task AddedMemberIsReportedOnTheDeclarationAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Thing
                              {
                                  public int Value { get; set; }
                                  public string {|PAS0001:Extra|} { get; set; } = "";
                              }
                              """;

        await PublicApiVerifier.AnalyzeAsync(Source, Baseline);
    }

    /// <summary>Verifies a whole added type is reported on its declaration.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task AddedTypeIsReportedAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Thing
                              {
                                  public int Value { get; set; }
                              }

                              public class {|PAS0001:Second|}
                              {
                                  public {|PAS0001:Second|}() { }
                              }
                              """;

        await PublicApiVerifier.AnalyzeAsync(Source, Baseline);
    }

    /// <summary>Verifies a changed declaration is reported as a change, not a removal plus an addition.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The member's identity — container, kind, name, arity and parameter types — is unchanged, so
    /// the useful report names the one member and shows both forms.
    /// </remarks>
    [Test]
    public async Task ChangedReturnTypeIsReportedAsAChangeAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Thing
                              {
                                  public long {|PAS0003:Value|} { get; set; }
                              }
                              """;

        await PublicApiVerifier.AnalyzeAsync(Source, Baseline);
    }

    /// <summary>Verifies a nullability change alone is reported, because consumers can see it.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ChangedNullabilityIsReportedAsync()
    {
        const string NullableBaseline = """
                                        namespace Sample;

                                        public class Thing
                                        {
                                            public Thing() { }
                                            public string Name { get; set; }
                                        }

                                        """;

        const string Source = """
                              namespace Sample;

                              public class Thing
                              {
                                  public string? {|PAS0003:Name|} { get; set; }
                              }
                              """;

        await PublicApiVerifier.AnalyzeAsync(Source, NullableBaseline);
    }

    /// <summary>Verifies a baseline entry whose member is gone is reported against the baseline file.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The diagnostic belongs on the baseline line, because that is the text a reviewer has to agree
    /// to delete; there is no longer anything in the source to point at.
    /// </remarks>
    [Test]
    public async Task RemovedMemberIsReportedInTheBaselineAsync()
    {
        const string RemovedBaseline = """
                                       namespace Sample;

                                       public class Thing
                                       {
                                           public Thing() { }
                                           public int Value { get; set; }
                                           public int Gone { get; set; }
                                       }

                                       """;

        const string Source = """
                              namespace Sample;

                              public class Thing
                              {
                                  public int Value { get; set; }
                              }
                              """;

        // The baseline still lists 'public int Gone' on line 7.
        const int Line = 7;
        const int StartColumn = 5;
        const int EndColumn = 34;

        var expected = PublicApiVerifier.Diagnostic(PublicApiRules.Removed)
            .WithSpan(PublicApiVerifier.BaselineFileName, Line, StartColumn, Line, EndColumn)
            .WithArguments("public int Gone { get; set; }");

        await PublicApiVerifier.AnalyzeAsync(Source, RemovedBaseline, expected);
    }

    /// <summary>Verifies reducing a member's accessibility reads as a removal.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task MemberMadeInternalIsReportedAsRemovedAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Thing
                              {
                                  internal int Value { get; set; }
                              }
                              """;

        // The baseline still lists 'public int Value' on line 6.
        const int Line = 6;
        const int StartColumn = 5;
        const int EndColumn = 35;

        var expected = PublicApiVerifier.Diagnostic(PublicApiRules.Removed)
            .WithSpan(PublicApiVerifier.BaselineFileName, Line, StartColumn, Line, EndColumn)
            .WithArguments("public int Value { get; set; }");

        await PublicApiVerifier.AnalyzeAsync(Source, Baseline, expected);
    }

    /// <summary>Verifies a baseline that cannot be parsed is reported as such.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// Without this the file would look empty and every member in the assembly would be reported as
    /// newly added, burying the hand-edit that actually broke it.
    /// </remarks>
    [Test]
    public async Task MalformedBaselineIsReportedAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Thing
                              {
                                  public int Value { get; set; }
                              }
                              """;

        const string Malformed = """
                                 namespace Sample;

                                 public class Thing
                                 {
                                     public int Value { get; set

                                 """;

        // The parser gives up where the accessor list is left unterminated, on line 5.
        const int Line = 5;
        const int Column = 32;

        var expected = PublicApiVerifier.Diagnostic(PublicApiRules.UnreadableBaseline)
            .WithSpan(PublicApiVerifier.BaselineFileName, Line, Column, Line, Column)
            .WithArguments("{ or ; or => expected");

        await PublicApiVerifier.AnalyzeAsync(Source, Malformed, expected);
    }

    /// <summary>Verifies a project with no baseline and no configured path stays silent.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// Installing the package must not break a build before a baseline has been adopted, so with
    /// nothing configured the analyzer does nothing at all.
    /// </remarks>
    [Test]
    public async Task MissingBaselineWithoutConfigurationIsSilentAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Thing
                              {
                                  public int Value { get; set; }
                              }
                              """;

        await PublicApiVerifier.AnalyzeWithoutBaselineAsync(Source);
    }

    /// <summary>Verifies extension blocks separated only by a constraint each match their own entry.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// <para>
    /// The baseline here is what this package renders for the source, so silence is the whole
    /// contract: whatever the surface says about an assembly has to match that assembly. When two
    /// blocks over one receiver could not be told apart, one of them matched the other's entry and
    /// reported a difference that accepting into the baseline reproduced on the next build — no
    /// sequence of regenerating the file ever reached a clean state.
    /// </para>
    /// <para>
    /// Both blocks are generic over the receiver, which is what makes a constraint expressible on
    /// one and not the other.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ExtensionBlocksSeparatedByAConstraintMatchTheirOwnBaselineEntryAsync()
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

        await PublicApiVerifier.AnalyzeAsync(Source, ApiSurfaceTestHost.Render(Source));
    }

    /// <summary>Verifies a nullable receiver is matched by the constraint that gives it its meaning.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// Both blocks spell the receiver <c>TSender?</c>, but the annotation means a nullable reference
    /// under the class constraint and a defaulted value otherwise. The receiver text is therefore
    /// identical while the APIs are not, which is exactly the case a match on the receiver alone
    /// cannot see.
    /// </remarks>
    [Test]
    public async Task NullableReceiverBlocksAreMatchedByTheirConstraintAsync()
    {
        if (!RoslynFeatures.SupportsExtensionBlocks)
        {
            return;
        }

        const string Source = """
                              namespace Sample;

                              public static class Mixins
                              {
                                  extension<TSender>(TSender? item)
                                      where TSender : class
                                  {
                                      public bool HasSender => item is not null;
                                  }

                                  extension<TSender>(TSender? item)
                                  {
                                      public bool HasValue => item is not null;
                                  }
                              }
                              """;

        await PublicApiVerifier.AnalyzeAsync(Source, ApiSurfaceTestHost.Render(Source));
    }
}
