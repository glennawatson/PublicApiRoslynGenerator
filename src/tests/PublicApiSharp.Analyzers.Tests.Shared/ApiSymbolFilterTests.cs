// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Verifies missing declaration names cannot make the rendered baseline unreadable.</summary>
public class ApiSymbolFilterTests
{
    /// <summary>Verifies parser recovery declarations are omitted without losing the surrounding surface.</summary>
    /// <param name="source">A declaration with a missing required name.</param>
    /// <param name="remaining">The declarations that still have a complete signature.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    [Arguments("public class C { public System.ValueTuple< { } }", "public class C { }")]
    [Arguments("public class C { public int { get; } }", "public class C { }")]
    [Arguments("public class C { public event System.Action { add {} remove {} } }", "public class C { }")]
    [Arguments("public class { }", "")]
    [Arguments("public class C { public class { } }", "public class C { }")]
    [Arguments("public class C { public void M(int) {} }", "public class C { }")]
    [Arguments("public class C { public C(int) {} }", "public class C { private C() {} }")]
    [Arguments("public class C { public int this[int] => 0; }", "public class C { }")]
    [Arguments("public delegate void D(int);", "")]
    [Arguments("public class C<> { }", "")]
    [Arguments("public class C { public void M<>() {} }", "public class C { }")]
    [Arguments("interface I { void M(int value); } public class C : I { void I.M(int) {} }", "public class C { }")]
    [Arguments("interface I { int this[int value] { get; } } public class C : I { int I.this[int] => 0; }", "public class C { }")]
    public async Task MissingDeclarationNameIsOmittedAsync(string source, string remaining)
    {
        const string Neighbor = "public class Kept { public int Value; } ";
        var compilation = CompileBroken(Neighbor + source);
        await Assert.That(compilation.GetDiagnostics().Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).IsTrue();

        var rendered = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None).Text;

        await Assert.That(rendered).IsEqualTo(ApiSurfaceTestHost.Render(Neighbor + remaining));
        await Assert.That(CSharpSyntaxTree.ParseText(rendered).GetDiagnostics()).IsEmpty();
        await Assert.That(ApiTextParser.Parse(SourceText.From(rendered), CancellationToken.None).Success).IsTrue();
    }

    /// <summary>Verifies a named declaration survives even when its type cannot bind.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task NamedErrorTypeRemainsInSurfaceAsync()
    {
        var compilation = CompileBroken("public class C { public Missing Value { get; } }");
        var rendered = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None).Text;

        await Assert.That(rendered).Contains("public Missing Value { get; }");
        await Assert.That(ApiTextParser.Parse(SourceText.From(rendered), CancellationToken.None).Success).IsTrue();
    }

    /// <summary>Verifies callable declarations survive even when they cannot be referenced by name.</summary>
    /// <param name="source">A valid declaration adjacent to a missing-name shape.</param>
    /// <param name="expected">The signature that must remain in the baseline.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    [Arguments("public class C { }", "public C() { }")]
    [Arguments("public class C { public C(int value) {} }", "public C(int value) { }")]
    [Arguments("public class C { public (int, string) Value { get; } }", "public (int, string) Value { get; }")]
    [Arguments("public class C { public int this[int key] => key; }", "public int this[int key] { get; }")]
    [Arguments("public class C { public static C operator +(C a, C b) => a; }", "public static C operator +(C a, C b) { }")]
    [Arguments("public class C { public static implicit operator int(C value) => 0; }", "public static implicit operator int(C value) { }")]
    [Arguments("public interface I { int Value { get; } } public class C : I { int I.Value => 0; }", "int I.Value { get; }")]
    [Arguments("public interface I { void M(); } public class C : I { void I.M() {} }", "void I.M() { }")]
    [Arguments("public interface I { int this[int key] { get; } } public class C : I { int I.this[int key] => key; }", "int this[int key] { get; }")]
    [Arguments("public class C { public event System.Action E { add {} remove {} } }", "public event System.Action E;")]
    [Arguments("public delegate void D(int value);", "public delegate void D(int value);")]
    [Arguments("public class C<T> { public void M<U>(T first, U second) {} }", "public void M<U>(T first, U second) { }")]
    [Arguments("public class @class { public int @event { get; } }", "public int @event { get; }")]
    public async Task ValidDeclarationRemainsInSurfaceAsync(string source, string expected)
    {
        var rendered = ApiSurfaceTestHost.Render(source);

        await Assert.That(rendered).Contains(expected);
        await Assert.That(CSharpSyntaxTree.ParseText(rendered).GetDiagnostics()).IsEmpty();
        await Assert.That(ApiTextParser.Parse(SourceText.From(rendered), CancellationToken.None).Success).IsTrue();
        await PublicApiVerifier.AnalyzeAsync(source, rendered);
    }

    /// <summary>Uses the normal reference set while allowing intentional parser errors in the source.</summary>
    /// <param name="source">The incomplete source.</param>
    /// <returns>The compilation containing the recovery symbols.</returns>
    private static CSharpCompilation CompileBroken(string source) =>
        ApiSurfaceTestHost.Compile(string.Empty).AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, new(LanguageVersion.Preview)));
}
