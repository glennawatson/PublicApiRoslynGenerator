// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Unit tests for the code fix, which accepts an API change by rewriting the baseline.</summary>
public class UpdatePublicApiBaselineCodeFixTests
{
    /// <summary>Verifies accepting an added member writes it into the baseline.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task AddedMemberIsWrittenToTheBaselineAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Thing
                              {
                                  public int Value { get; set; }
                                  public int {|PAS0001:Extra|} { get; set; }
                              }
                              """;

        const string FixedSource = """
                                   namespace Sample;

                                   public class Thing
                                   {
                                       public int Value { get; set; }
                                       public int Extra { get; set; }
                                   }
                                   """;

        const string Baseline = """
                                namespace Sample;

                                public class Thing
                                {
                                    public Thing() { }
                                    public int Value { get; set; }
                                }

                                """;

        const string FixedBaseline = """
                                     namespace Sample;

                                     public class Thing
                                     {
                                         public Thing() { }
                                         public int Extra { get; set; }
                                         public int Value { get; set; }
                                     }

                                     """;

        await PublicApiVerifier.FixAsync(Source, FixedSource, Baseline, FixedBaseline);
    }

    /// <summary>Verifies accepting an addition also drops an entry whose member is gone.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// A removal on its own is reported against the baseline file, which is not a document, so it
    /// never gets a lightbulb of its own. The fix regenerates the whole file rather than editing
    /// lines, so invoking it from any diagnostic that does sit in source clears the stale entry too.
    /// </remarks>
    [Test]
    public async Task AcceptingAnAdditionAlsoDropsARemovedEntryAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Thing
                              {
                                  public int Value { get; set; }
                                  public int {|PAS0001:Extra|} { get; set; }
                              }
                              """;

        const string FixedSource = """
                                   namespace Sample;

                                   public class Thing
                                   {
                                       public int Value { get; set; }
                                       public int Extra { get; set; }
                                   }
                                   """;

        const string Baseline = """
                                namespace Sample;

                                public class Thing
                                {
                                    public Thing() { }
                                    public int Value { get; set; }
                                    public int Gone { get; set; }
                                }

                                """;

        const string FixedBaseline = """
                                     namespace Sample;

                                     public class Thing
                                     {
                                         public Thing() { }
                                         public int Extra { get; set; }
                                         public int Value { get; set; }
                                     }

                                     """;

        // The baseline still lists 'public int Gone' on line 7.
        const int Line = 7;
        const int StartColumn = 5;
        const int EndColumn = 34;

        var removed = PublicApiVerifier.Diagnostic(PublicApiRules.Removed)
            .WithSpan(PublicApiVerifier.BaselineFileName, Line, StartColumn, Line, EndColumn)
            .WithArguments("public int Gone { get; set; }");

        await PublicApiVerifier.FixAsync(Source, FixedSource, Baseline, FixedBaseline, removed);
    }

    /// <summary>Verifies accepting a changed declaration rewrites that one entry.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ChangedMemberIsRewrittenInTheBaselineAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class Thing
                              {
                                  public long {|PAS0003:Value|} { get; set; }
                              }
                              """;

        const string FixedSource = """
                                   namespace Sample;

                                   public class Thing
                                   {
                                       public long Value { get; set; }
                                   }
                                   """;

        const string Baseline = """
                                namespace Sample;

                                public class Thing
                                {
                                    public Thing() { }
                                    public int Value { get; set; }
                                }

                                """;

        const string FixedBaseline = """
                                     namespace Sample;

                                     public class Thing
                                     {
                                         public Thing() { }
                                         public long Value { get; set; }
                                     }

                                     """;

        await PublicApiVerifier.FixAsync(Source, FixedSource, Baseline, FixedBaseline);
    }

    /// <summary>Verifies bootstrapping from an empty baseline writes the whole surface.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// Creating the file and letting the fix fill it in is how a project adopts tracking, so the
    /// empty-file case has to produce a complete baseline rather than a partial one.
    /// </remarks>
    [Test]
    public async Task EmptyBaselineIsFilledInAsync()
    {
        const string Source = """
                              namespace Sample;

                              public class {|PAS0001:Thing|}
                              {
                                  public {|PAS0001:Thing|}() { }
                                  public int {|PAS0001:Value|} { get; set; }
                              }
                              """;

        const string FixedSource = """
                                   namespace Sample;

                                   public class Thing
                                   {
                                       public Thing() { }
                                       public int Value { get; set; }
                                   }
                                   """;

        const string FixedBaseline = """
                                     namespace Sample;

                                     public class Thing
                                     {
                                         public Thing() { }
                                         public int Value { get; set; }
                                     }

                                     """;

        await PublicApiVerifier.FixAsync(Source, FixedSource, string.Empty, FixedBaseline);
    }
}
