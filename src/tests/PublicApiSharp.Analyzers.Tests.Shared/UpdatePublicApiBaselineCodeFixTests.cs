// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

using PublicApiSharp.Analyzers.CodeFixes;

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

    /// <summary>Verifies varied compiled surfaces parse, compare cleanly and are written exactly by the code fix.</summary>
    /// <param name="source">The compilable source exercising a rendering boundary.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("")]
    [Arguments("internal class Hidden { }")]
    [Arguments("public static class C { public const int Value = 1; }")]
    [Arguments("public static class C\r\n{\npublic const int Value = 1;\r}")]
    [Arguments("public static class C\t{\tpublic const int Value = 1;\t}")]
    [Arguments("public static class C { public const int\u00a0Value = 1;\u2003}")]
    [Arguments("public static class C { public const int\u0085Value = 1;\u2028}")]
    [Arguments("public static class C { public const int\u2029Value = 1; }")]
    [Arguments("\uFEFFpublic interface I { }")]
    [Arguments("namespace Hidden { internal class C { } } namespace Visible { public interface I { } }")]
    [Arguments("namespace A { namespace B { namespace C { namespace D { public interface I { } } } } }")]
    [Arguments("namespace Left.Leaf { public interface I { } } namespace Right.Leaf { public interface I { } }")]
    [Arguments("namespace @class.@namespace { public interface @interface { } }")]
    [Arguments("public interface Global { } namespace Visible { public interface I { } }")]
    [Arguments("namespace Hidden { [System.CodeDom.Compiler.GeneratedCode(\"tool\", \"1\")] public class G { } } namespace Visible { public interface I { } }")]
    [Arguments("""
        [assembly: System.CLSCompliant(true)]
        [System.Obsolete, System.CLSCompliant(true)] public class Outer
        {
            [System.Obsolete] public class Inner
            {
                [System.Obsolete] public enum Values { [System.Obsolete] First, Last }
            }
        }
        """)]
    [Arguments("public static class A { [System.Obsolete] public const int Value = 1; public class Inner { public int Field; } } public static class B { public static int Other() => 0; }")]
    [Arguments("public class C { public int Value; public int value; public void M() { } public void M<T>() { } public void M<T, U>() { } }")]
    [Arguments("public class C { public void M(int value) { } public void M(ref int value) { } public void Read(in int value) { } public void Write(out int value) { value = 0; } }")]
    [Arguments("public class C { public string? Value { get; set; } public void M(string? value) { } }")]
    [Arguments(@"public class \u0043 { public int \u0056alue; }")]
    [Arguments("public class C<T> { public class Nested<U> { public U? Value { get; set; } public TResult M<TResult>(T first, U second) => default!; } }")]
    [Arguments("public struct S { public int Value; } public interface I { void M(); } public enum E { One, Two } public delegate void D(int value);")]
    [Arguments("public record C<T>(T Value); public record struct S<T>(T Value);")]
    [Arguments("""
        public class C
        {
            public C(int value) { }
            public int this[int key] => key;
            public event System.Action Changed { add { } remove { } }
            public static C operator +(C a, C b) => a;
            public static implicit operator int(C value) => 0;
        }
        """)]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public Task VariedSurfacesRoundTripThroughAnalyzerAndCodeFixAsync(string source) => AssertRenderedRoundTripAsync(source, "public interface Stale { }");

    /// <summary>Verifies rewriting a baseline canonicalizes every supported line-ending and whitespace boundary.</summary>
    /// <param name="whitespace">The baseline whitespace representation.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("\r\n")]
    [Arguments("\r")]
    [Arguments("\t\n")]
    [Arguments("\u00a0\n")]
    [Arguments("\u2003\n")]
    [Arguments("\u0085\n")]
    [Arguments("\u2028\n")]
    [Arguments("\u2029\n")]
    public async Task CodeFixWritesCanonicalSurfaceOverBaselineWhitespaceAsync(string whitespace)
    {
        const string Source = "public static class C { public const int Value = 1; }";
        var baseline = $"﻿{ApiSurfaceTestHost.Render(Source).Replace("\n", whitespace, StringComparison.Ordinal).TrimEnd()}";
        await AssertRenderedRoundTripAsync(Source, baseline);
    }

    /// <summary>Verifies multiple extension headers with one receiver survive parsing, analysis and regeneration.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task SharedReceiverExtensionsRoundTripThroughTheCodeFixAsync()
    {
        if (!RoslynFeatures.SupportsExtensionBlocks)
        {
            return;
        }

        const string Source = """
            public static class Extensions
            {
                extension(string first) { [System.Obsolete] public int First => first.Length; }
                extension(string second) { public int Second => second.Length; }
                extension<T>(T receiver) where T : class { public T Echo() => receiver; }
            }
            """;
        await AssertRenderedRoundTripAsync(Source, ApiSurfaceTestHost.Render(Source) + ApiSurfaceTestHost.Render(Source));
    }

    /// <summary>Verifies regeneration and parsing preserve a surface beyond the writer's initial capacities.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task ThousandsOfDeclarationsRoundTripThroughTheCodeFixAsync()
    {
        const int DeclarationCount = 3000;
        var builder = new PooledStringBuilder();
        for (var index = 0; index < DeclarationCount; index++)
        {
            _ = builder.Append("public delegate void D").Append(index).Append("();\n");
        }

        await AssertRenderedRoundTripAsync(builder.ToString(), string.Empty);
    }

    /// <summary>Checks the parse, analyzer and baseline rewrite against the same rendered bytes.</summary>
    /// <param name="source">The compilable C# source.</param>
    /// <param name="baseline">The previous baseline to overwrite.</param>
    /// <returns>A task representing the asynchronous verification.</returns>
    private static async Task AssertRenderedRoundTripAsync(string source, string baseline)
    {
        var compilation = ApiSurfaceTestHost.Compile(source);
        var surface = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None);
        var parse = ApiTextParser.Parse(SourceText.From(surface.Text), CancellationToken.None);
        await Assert.That(parse.Success).IsTrue();
        await Assert.That(CSharpSyntaxTree.ParseText(surface.Text, new(LanguageVersion.Preview)).GetDiagnostics()).IsEmpty();
        await ApiTextComparisonTests.AssertFullComparisonAsync(compilation, SourceText.From(surface.Text), 0);

        using var workspace = await PublicApiVerifier.CreateWorkspaceAsync();
        var projectId = ProjectId.CreateNewId();
        var sourceId = DocumentId.CreateNewId(projectId);
        var baselineId = DocumentId.CreateNewId(projectId);
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Default,
                "RoundTrip",
                "RoundTrip",
                LanguageNames.CSharp,
                compilationOptions: compilation.Options,
                parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
                metadataReferences: compilation.References))
            .AddDocument(sourceId, "Source.cs", SourceText.From(source))
            .AddAdditionalDocument(baselineId, PublicApiVerifier.BaselineFileName, SourceText.From(baseline));
        var result = await UpdatePublicApiBaselineCodeFixProvider.UpdateBaselineAsync(solution.GetProject(projectId)!, CancellationToken.None);
        var written = await result.GetAdditionalDocument(baselineId)!.GetTextAsync();
        var retainedSource = await result.GetDocument(sourceId)!.GetTextAsync();

        await Assert.That(written.ToString()).IsEqualTo(surface.Text);
        await Assert.That(retainedSource.ToString()).IsEqualTo(source);
        var fixedCompilation = await result.GetProject(projectId)!.GetCompilationAsync();
        await ApiTextComparisonTests.AssertFullComparisonAsync(fixedCompilation!, written, 0);
    }
}
