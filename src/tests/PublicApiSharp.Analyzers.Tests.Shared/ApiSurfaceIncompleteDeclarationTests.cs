// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Verifies missing declaration names cannot make the rendered baseline unreadable.</summary>
public class ApiSurfaceIncompleteDeclarationTests
{
    /// <summary>Verifies an incomplete container cannot expose its otherwise valid descendants.</summary>
    /// <param name="source">The container and its complete descendants.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    [Arguments("public class C<> { public int Value; public void M() {} public class Nested { } }")]
    [Arguments("public class C<T,> { public T Value; public class Nested { } }")]
    public async Task IncompleteContainerOmitsItsDescendantsAsync(string source)
    {
        const string Neighbor = "public class Kept { public int Value; } ";
        var compilation = CompileBroken(Neighbor + source);
        var rendered = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None).Text;

        await Assert.That(rendered).IsEqualTo(ApiSurfaceTestHost.Render(Neighbor));
        await Assert.That(ApiTextParser.Parse(SourceText.From(rendered), CancellationToken.None).Success).IsTrue();
    }

    /// <summary>Verifies a named nested type is omitted when its containing type has no name.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task UnnamedContainerOmitsItsNestedTypeAsync()
    {
        const string Neighbor = "public class Kept { public int Value; } ";
        var root = CSharpSyntaxTree.ParseText("public class Container { public class Nested { public int Value; } }").GetCompilationUnitRoot();
        var declaration = (ClassDeclarationSyntax)root.Members[0];
        var broken = root.ReplaceToken(declaration.Identifier, SyntaxFactory.MissingToken(default, SyntaxKind.IdentifierToken, default));
        var compilation = ApiSurfaceTestHost.Compile(Neighbor).AddSyntaxTrees(CSharpSyntaxTree.Create(broken, new(LanguageVersion.Preview)));
        var rendered = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None).Text;

        await Assert.That(rendered).IsEqualTo(ApiSurfaceTestHost.Render(Neighbor));
        await Assert.That(ApiTextParser.Parse(SourceText.From(rendered), CancellationToken.None).Success).IsTrue();
    }

    /// <summary>Verifies a later missing signature name also omits the declaration and its attributes.</summary>
    /// <param name="source">An attributed declaration with an incomplete signature.</param>
    /// <param name="remaining">The surviving declarations.</param>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    [Arguments("public class C { [System.Obsolete] public void M(int value, int) {} }", "public class C { }")]
    [Arguments("public class C { [System.Obsolete] public void M<T,>() {} }", "public class C { }")]
    [Arguments("[System.Obsolete] public delegate void D<T,>(int value);", "")]
    [Arguments("[System.Obsolete] public delegate void D(int value, int);", "")]
    public async Task IncompleteSignatureOmitsItsAttributesAsync(string source, string remaining)
    {
        const string Neighbor = "public class Kept { public int Value; } ";
        var compilation = CompileBroken(Neighbor + source);
        var rendered = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None).Text;

        await Assert.That(rendered).IsEqualTo(ApiSurfaceTestHost.Render(Neighbor + remaining));
        await Assert.That(ApiTextParser.Parse(SourceText.From(rendered), CancellationToken.None).Success).IsTrue();
    }

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

    /// <summary>Verifies unnamed type signatures disappear, while record constructor omissions preserve complete type headers.</summary>
    /// <param name="declaration">The declaration whose marked identifier is removed.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("[System.Obsolete] public class MissingName { public int Value; }")]
    [Arguments("[System.Obsolete] public struct MissingName { public int Value; }")]
    [Arguments("[System.Obsolete] public interface MissingName { void M(); }")]
    [Arguments("[System.Obsolete] public enum MissingName { One, Two }")]
    [Arguments("[System.Obsolete] public record MissingName(int Value);")]
    [Arguments("[System.Obsolete] public record struct MissingName(int Value);")]
    [Arguments("[System.Obsolete] public delegate void MissingName(int value);")]
    [Arguments("[System.Obsolete] public class C<MissingName> { public int Value; }")]
    [Arguments("[System.Obsolete] public struct C<T, MissingName> { public int Value; }")]
    [Arguments("[System.Obsolete] public interface C<MissingName> { void M(); }")]
    [Arguments("[System.Obsolete] public record C<MissingName>(int Value);")]
    [Arguments("[System.Obsolete] public record struct C<MissingName>(int Value);")]
    [Arguments("[System.Obsolete] public delegate void D<MissingName>(int value);")]
    [Arguments("[System.Obsolete] public delegate void D(int MissingName);")]
    [Arguments("[System.Obsolete] public record C(int MissingName);")]
    [Arguments("[System.Obsolete] public record struct C(int MissingName);")]
    public async Task MissingTypeNamesAndSignatureNamesOmitTheWholeDeclarationAsync(string declaration)
    {
        var survivingDeclaration = declaration switch
        {
            "[System.Obsolete] public record C(int MissingName);" => "[System.Obsolete] public record C;",
            "[System.Obsolete] public record struct C(int MissingName);" => "[System.Obsolete] public record struct C;",
            _ => string.Empty,
        };
        string[] containers = ["{0}", "public class Outer<T> {{ {0} }}",
            "public class Outer<T> {{ public class Nested<U> {{ {0} }} }}"];
        foreach (var container in containers)
        {
            var source = string.Format(System.Globalization.CultureInfo.InvariantCulture, container, declaration);
            var remaining = string.Format(System.Globalization.CultureInfo.InvariantCulture, container, survivingDeclaration);
            await AssertMissingMarkedNameAsync(source, remaining);
        }
    }

    /// <summary>Verifies every named member and callable parameter shape is omitted without its attributes.</summary>
    /// <param name="declaration">The member whose marked name is missing.</param>
    /// <param name="remaining">Any constructor marker required after omission.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("[System.Obsolete] public int MissingName;", "")]
    [Arguments("[System.Obsolete] public event System.Action MissingName;", "")]
    [Arguments("[System.Obsolete] public event System.Action MissingName { add { } remove { } }", "")]
    [Arguments("[System.Obsolete] public int MissingName { get; }", "")]
    [Arguments("[System.Obsolete] public void MissingName() { }", "")]
    [Arguments("[System.Obsolete] public void M(int MissingName) { }", "")]
    [Arguments("[System.Obsolete] public void M(int first, int MissingName) { }", "")]
    [Arguments("[System.Obsolete] public void M<MissingName>() { }", "")]
    [Arguments("[System.Obsolete] public void M<TMethod, MissingName>() { }", "")]
    [Arguments("[System.Obsolete] public int this[int MissingName] => 0;", "")]
    [Arguments("[System.Obsolete] public C(int MissingName) { }", "private C() { }")]
    [Arguments("[System.Obsolete] public C(int first, int MissingName) { }", "private C() { }")]
    [Arguments("[System.Obsolete] public static C operator +(C first, C MissingName) => first;", "")]
    [Arguments("[System.Obsolete] public static implicit operator int(C MissingName) => 0;", "")]
    [Arguments("[System.Obsolete] public static explicit operator int(C MissingName) => 0;", "")]
    public async Task MissingMemberNamesAndCallableParametersAreOmittedAsync(string declaration, string remaining)
    {
        string[] containers = ["public class C {{ {0} }}", "public class C<T> {{ {0} }}",
            "public class Outer<T> {{ public class C<U> {{ {0} }} }}"];
        foreach (var container in containers)
        {
            await AssertMissingMarkedNameAsync(
                string.Format(System.Globalization.CultureInfo.InvariantCulture, container, declaration),
                string.Format(System.Globalization.CultureInfo.InvariantCulture, container, remaining));
        }
    }

    /// <summary>Verifies missing names in extension members cannot leave an attribute or broken declaration behind.</summary>
    /// <param name="declaration">The extension member with a marked name.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("[System.Obsolete] public int MissingName => 0;")]
    [Arguments("[System.Obsolete] public void MissingName() { }")]
    [Arguments("[System.Obsolete] public void M(int MissingName) { }")]
    [Arguments("[System.Obsolete] public void M<TMethod, MissingName>() { }")]
    public async Task MissingExtensionMemberNamesAreOmittedAsync(string declaration)
    {
        if (!RoslynFeatures.SupportsExtensionBlocks)
        {
            return;
        }

        await AssertMissingMarkedNameAsync(
            $"public static class Extensions {{ extension<T>(T receiver) {{ {declaration} }} }}",
            "public static class Extensions { extension<T>(T receiver) { } }");
    }

    /// <summary>Verifies missing required type-parameter names suppress a block, while an optional receiver name does not.</summary>
    /// <param name="header">The incomplete extension header.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("extension<MissingName>(string receiver)")]
    [Arguments("extension(string MissingName)")]
    public async Task MissingExtensionHeaderNameOmitsItsMembersAsync(string header)
    {
        if (!RoslynFeatures.SupportsExtensionBlocks)
        {
            return;
        }

        var unnamedReceiver = header == "extension(string MissingName)";
        await AssertMissingMarkedNameAsync(
            $"public static class Extensions {{ {header} {{ public int Value => 0; }} }}",
            unnamedReceiver ? "public static class Extensions { extension(string) { public int Value => 0; } }" : "public static class Extensions { }",
            unnamedReceiver);
    }

    /// <summary>Verifies an unnamed enum member does not consume its neighbors or leak its attributes.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public Task UnnamedEnumMemberLeavesNamedNeighborsIntactAsync() => AssertMissingMarkedNameAsync(
            "public enum Values { First = 1, [System.Obsolete] MissingName = 2, Last = 3 }",
            "public enum Values { First = 1, Last = 3 }");

    /// <summary>Verifies an unnamed primary-constructor parameter cannot leave an invalid constructor signature.</summary>
    /// <param name="source">The primary constructor whose marked parameter name is removed.</param>
    /// <param name="remaining">The type and constructor accessibility that remain.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("public class C(int MissingName) { }", "public class C { private C() { } }")]
    [Arguments("public struct C(int MissingName) { }", "public struct C { }")]
    [Arguments("public class C<T>(int MissingName) { }", "public class C<T> { private C() { } }")]
    [Arguments("public struct C<T>(int MissingName) { }", "public struct C<T> { }")]
    [Arguments("public class Outer<T> { public class C<U>(int MissingName) { } }", "public class Outer<T> { public class C<U> { private C() { } } }")]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public Task MissingPrimaryConstructorParameterPreservesOnlyTheTypeAsync(string source, string remaining) =>
        AssertMissingMarkedNameAsync(source, remaining);

    /// <summary>Verifies unnamed variables and explicit implementations do not remove complete neighboring members.</summary>
    /// <param name="source">The source with a marked member name.</param>
    /// <param name="remaining">The complete declarations that survive.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("public class C { public int MissingName, KeptField; }", "public class C { public int KeptField; }")]
    [Arguments("public class C { public event System.Action MissingName, KeptEvent; }", "public class C { public event System.Action KeptEvent; }")]
    [Arguments("interface I { void M(); } public class C : I { void I.MissingName() { } }", "public class C { }")]
    [Arguments("interface I { int P { get; } } public class C : I { int I.MissingName => 0; }", "public class C { }")]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public Task MissingMemberNamePreservesCompleteNeighborsAsync(string source, string remaining) =>
        MissingPrimaryConstructorParameterPreservesOnlyTheTypeAsync(source, remaining);

    /// <summary>Verifies an omitted extension method cannot hide complete members that follow it.</summary>
    /// <param name="member">The extension method with a missing signature name.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("[System.Obsolete] public void M(int MissingName) { }")]
    [Arguments("[System.Obsolete] public void M<TMethod, MissingName>() { }")]
    public async Task IncompleteExtensionSignaturePreservesCompleteNeighborAsync(string member)
    {
        if (!RoslynFeatures.SupportsExtensionBlocks)
        {
            return;
        }

        const string Remaining = "public static class Extensions { extension<T>(T receiver) { public T Kept => receiver; } }";
        await AssertMissingMarkedNameAsync(
            $"public static class Extensions {{ extension<T>(T receiver) {{ {member} public T Kept => receiver; }} }}",
            Remaining);
    }

    /// <summary>Verifies a receiver without a name remains valid for static extension members.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task UnnamedExtensionReceiverRetainsStaticMembersAsync()
    {
        if (!RoslynFeatures.SupportsExtensionBlocks)
        {
            return;
        }

        const string Source = "public static class Extensions { extension(string) { public static int Value => 0; } }";
        const string Expected = """
            public static class Extensions
            {
                extension(string)
                {
                    public static int Value { get; }
                }
            }

            """;
        var rendered = ApiSurfaceTestHost.Render(Source);

        await Assert.That(rendered).IsEqualTo(Expected);
        await PublicApiVerifier.AnalyzeAsync(Source, rendered);
    }

    /// <summary>Creates a recovery tree by removing only the marked identifier and checks the complete surviving surface.</summary>
    /// <param name="source">The source before the marked identifier is removed.</param>
    /// <param name="remaining">The complete surviving source.</param>
    /// <param name="remainingHasCompilerErrors">Whether the expected surface retains a binding error unrelated to required signature names.</param>
    /// <returns>A task representing the asynchronous verification.</returns>
    private static async Task AssertMissingMarkedNameAsync(string source, string remaining, bool remainingHasCompilerErrors = false)
    {
        const string Neighbor = "public interface Kept { void Keep(); } ";
        var root = await CSharpSyntaxTree.ParseText(source, new(LanguageVersion.Preview)).GetRootAsync();
        var tokens = new List<SyntaxToken>();
        foreach (var token in root.DescendantTokens())
        {
            if (token.ValueText == "MissingName")
            {
                tokens.Add(token);
            }
        }

        await Assert.That(tokens).IsNotEmpty();
        var broken = root.ReplaceTokens(tokens, static (_, _) => SyntaxFactory.MissingToken(default, SyntaxKind.IdentifierToken, default));
        var compilation = ApiSurfaceTestHost.Compile(Neighbor).AddSyntaxTrees(CSharpSyntaxTree.Create((CSharpSyntaxNode)broken, new(LanguageVersion.Preview)));
        var rendered = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None).Text;

        var expectedCompilation = remainingHasCompilerErrors ? CompileBroken(Neighbor + remaining) : ApiSurfaceTestHost.Compile(Neighbor + remaining);
        var expected = ApiSurfaceRenderer.Render(expectedCompilation, ApiRenderOptions.Default, CancellationToken.None).Text;
        await Assert.That(rendered).IsEqualTo(expected);
        await Assert.That(CSharpSyntaxTree.ParseText(rendered, new(LanguageVersion.Preview)).GetDiagnostics()).IsEmpty();
        await Assert.That(ApiTextParser.Parse(SourceText.From(rendered), CancellationToken.None).Success).IsTrue();
    }

    /// <summary>Uses the normal reference set while allowing intentional parser errors in the source.</summary>
    /// <param name="source">The incomplete source.</param>
    /// <returns>The compilation containing the recovery symbols.</returns>
    private static CSharpCompilation CompileBroken(string source) =>
        ApiSurfaceTestHost.Compile(string.Empty).AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, new(LanguageVersion.Preview)));
}
