// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Verifies array attribute constants render as expressions that can be read back.</summary>
public class ApiAttributeRendererTests
{
    /// <summary>The array type used by the numeric and equivalent-constant cases.</summary>
    private const string IntArray = "int[]";

    /// <summary>Supplies array constants in constructor arguments and named properties, including object slots.</summary>
    /// <returns>Source and expected attribute text for each constant and argument position.</returns>
    public static IEnumerable<(string Source, string Attribute)> ArrayArguments()
    {
        const string ObjectArray = "object[]";
        (string Type, string Expression, string Expected)[] values =
        [
            (IntArray, "new int[0]", "new int[] { }"),
            (IntArray, "new int[] { 1 }", "new int[] { 1 }"),
            (IntArray, "new int[] { 1, 2, 3 }", "new int[] { 1, 2, 3 }"),
            (IntArray, "(int[])null!", "null"),
            ("string[]", "new string[0]", "new string[] { }"),
            ("string[]", "new string[] { null!, \"a\\\"b\", \"c\" }", "new string[] { null, \"a\\\"b\", \"c\" }"),
            ("bool[]", "new bool[] { true, false }", "new bool[] { true, false }"),
            ("char[]", "new char[] { 'a', '\\n' }", "new char[] { 'a', '\\n' }"),
            ("byte[]", "new byte[] { 1, 2 }", "new byte[] { 1, 2 }"),
            ("sbyte[]", "new sbyte[] { -1, 2 }", "new sbyte[] { -1, 2 }"),
            ("short[]", "new short[] { -1, 2 }", "new short[] { -1, 2 }"),
            ("ushort[]", "new ushort[] { 1, 2 }", "new ushort[] { 1, 2 }"),
            ("uint[]", "new uint[] { 1, 2 }", "new uint[] { 1, 2 }"),
            ("long[]", "new long[] { -1, 2 }", "new long[] { -1, 2 }"),
            ("ulong[]", "new ulong[] { 1, 2 }", "new ulong[] { 1, 2 }"),
            ("float[]", "new float[] { 1.5F, 2.5F }", "new float[] { 1.5, 2.5 }"),
            ("double[]", "new double[] { 1.5, 2.5 }", "new double[] { 1.5, 2.5 }"),
            ("System.DayOfWeek[]", "new System.DayOfWeek[] { System.DayOfWeek.Monday, (System.DayOfWeek)42 }", "new System.DayOfWeek[] { System.DayOfWeek.Monday, 42 }"),
            ("System.Type[]", "new System.Type[] { typeof(int), typeof(string[]), null! }", "new System.Type[] { typeof(int), typeof(string[]), null }"),
            (ObjectArray, "new object[0]", "new object[] { }"),
            (ObjectArray, "new object[] { 1, \"two\", null!, typeof(int) }", "new object[] { 1, \"two\", null, typeof(int) }"),
            (ObjectArray, "new object[] { new int[0] }", "new object[] { new int[] { } }"),
            (ObjectArray, "new object[] { new int[] { 1 }, new string[] { \"a\", \"b\" } }", "new object[] { new int[] { 1 }, new string[] { \"a\", \"b\" } }"),
            (ObjectArray, "new object[] { new object[] { new int[0], new int[] { 1, 2 } } }", "new object[] { new object[] { new int[] { }, new int[] { 1, 2 } } }"),
        ];

        foreach (var (type, expression, expected) in values)
        {
            yield return (Source(type, expression, named: false), $"[Some({expected})]");
            yield return (Source(type, expression, named: true), $"[Some(Values={expected})]");
            yield return (Source("object", expression, named: false), $"[Some({expected})]");
            yield return (Source("object", expression, named: true), $"[Some(Values={expected})]");
        }
    }

    /// <summary>Verifies exact array text and both C# and baseline parsing.</summary>
    /// <param name="source">The declaration carrying the array argument.</param>
    /// <param name="attribute">The expected rendered attribute line.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    [MethodDataSource(nameof(ArrayArguments))]
    public async Task ArrayArgumentRendersAsAParseableExpressionAsync(string source, string attribute)
    {
        var rendered = ApiSurfaceTestHost.Render(source);
        await Assert.That(rendered).Contains(attribute);
        await Assert.That(CSharpSyntaxTree.ParseText(rendered).GetDiagnostics()).IsEmpty();
        var parsed = ApiTextParser.Parse(SourceText.From(rendered), CancellationToken.None);
        await Assert.That(parsed.Success).IsTrue();
        await Assert.That(parsed.Error).IsNull();
    }

    /// <summary>Verifies every array form is silent against its freshly rendered baseline.</summary>
    /// <param name="source">The declaration carrying the array argument.</param>
    /// <param name="attribute">The expected rendered attribute line.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    [MethodDataSource(nameof(ArrayArguments))]
    public async Task ArrayArgumentMatchesItsRenderedBaselineAsync(string source, string attribute)
    {
        var rendered = ApiSurfaceTestHost.Render(source);
        await PublicApiVerifier.AnalyzeAsync(source, rendered);
        await Assert.That(rendered).Contains(attribute);
    }

    /// <summary>Verifies equivalent source spellings depend only on the array constant.</summary>
    /// <param name="first">One spelling of an array constant.</param>
    /// <param name="second">An equivalent spelling.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    [Arguments("new int[0]", "new int[] { }")]
    [Arguments("new[] { 1, 2 }", "new int[] { 1, 1 + 1 }")]
    [Arguments("new int[] { 1, 2 }", "values: new int[] { 1, 2 }")]
    public async Task EquivalentArrayConstantsRenderIdenticallyAsync(string first, string second)
    {
        var rendered = ApiSurfaceTestHost.Render(Source(IntArray, first, named: false));
        await Assert.That(ApiSurfaceTestHost.Render(Source(IntArray, second, named: false))).IsEqualTo(rendered);
    }

    /// <summary>Builds an attribute application with either a constructor or a named property argument.</summary>
    /// <param name="type">The declared argument type.</param>
    /// <param name="expression">The constant expression.</param>
    /// <param name="named">Whether the argument assigns a property.</param>
    /// <returns>The source to compile.</returns>
    private static string Source(string type, string expression, bool named)
    {
        var argument = named ? $"Values = {expression}" : expression;
        var member = named ? $"public {type} Values {{ get; set; }} = null!;" : $"public SomeAttribute({type} values) {{ }}";
        return $$"""
                 [Some({{argument}})]
                 public class C { }
                 public class SomeAttribute : System.Attribute
                 {
                     {{member}}
                 }
                 """;
    }
}
