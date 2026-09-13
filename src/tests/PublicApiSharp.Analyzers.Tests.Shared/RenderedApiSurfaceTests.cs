// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Tests the normalized declarations retained from rendered text.</summary>
public class RenderedApiSurfaceTests
{
    /// <summary>The type whose symbol the writer records.</summary>
    private const string TypeSource = "public class Thing { }";

    /// <summary>The type's identity and metadata name.</summary>
    private const string TypeName = "Thing";

    /// <summary>The declaration text retained by the writer.</summary>
    private const string TypeHeader = "public class Thing";

    /// <summary>Verifies transferred storage retains only written entries through empty and growing buffers.</summary>
    /// <param name="count">The number of declarations to write.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(257)]
    public async Task CompletedSurfaceRetainsWrittenEntriesAsync(int count)
    {
        var symbol = ApiSurfaceTestHost.Compile(TypeSource).GetTypeByMetadataName(TypeName)!;
        var writer = new ApiSurfaceRenderer.SurfaceWriter();
        for (var index = 0; index < count; index++)
        {
            writer.Pending = symbol;
            writer.Line(string.Empty, TypeHeader, symbol);
        }

        var surface = writer.Complete();

        await Assert.That(surface.Declarations.Length).IsEqualTo(count);
        await Assert.That(surface.SymbolAtLine(-1)).IsNull();
        await Assert.That(surface.SymbolAtLine(count)).IsNull();
        for (var index = 0; index < count; index++)
        {
            await Assert.That(surface.SymbolAtLine(index)).IsEqualTo(symbol);
            await Assert.That(surface.Declarations[index].Text).IsEqualTo(TypeHeader);
            await Assert.That(surface.Declarations[index].StartLine).IsEqualTo(index);
        }
    }

    /// <summary>Verifies the next line owns the pending symbol and consumes that pending state.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task WritingALineConsumesItsPendingSymbolAsync()
    {
        var symbol = ApiSurfaceTestHost.Compile(TypeSource).GetTypeByMetadataName(TypeName)!;
        var writer = new ApiSurfaceRenderer.SurfaceWriter { Pending = symbol };

        await Assert.That(writer.Pending).IsEqualTo(symbol);
        writer.Line(string.Empty, TypeHeader, symbol);

        await Assert.That(writer.Pending).IsNull();
        var surface = writer.Complete();
        await Assert.That(surface.SymbolAtLine(0)).IsEqualTo(symbol);
        await Assert.That(surface.Declarations.Length).IsEqualTo(1);
    }

    /// <summary>Verifies clearing the next line's symbol retains the declaration already opened.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task ClearingPendingSymbolPreservesTheOpenDeclarationAsync()
    {
        var symbol = ApiSurfaceTestHost.Compile(TypeSource).GetTypeByMetadataName(TypeName)!;
        var writer = new ApiSurfaceRenderer.SurfaceWriter { Pending = symbol };

        writer.Pending = null;
        await Assert.That(writer.Pending).IsNull();
        writer.Line(string.Empty, TypeHeader, symbol);
        var surface = writer.Complete();

        await Assert.That(surface.Declarations.Length).IsEqualTo(1);
        await Assert.That(surface.Declarations[0].Identity).IsEqualTo(TypeName);
        await Assert.That(surface.Declarations[0].Text).IsEqualTo(TypeHeader);
        await Assert.That(surface.SymbolAtLine(0)).IsEqualTo(symbol);
    }

    /// <summary>Verifies closing a declaration twice does not record an extra entry.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task EndingAnAlreadyClosedDeclarationDoesNotDuplicateItAsync()
    {
        var symbol = ApiSurfaceTestHost.Compile(TypeSource).GetTypeByMetadataName(TypeName)!;
        var writer = new ApiSurfaceRenderer.SurfaceWriter { Pending = symbol };
        writer.BeginLine(string.Empty);
        _ = writer.Builder.Append(TypeHeader);

        writer.EndDeclaration();
        writer.EndDeclaration();
        writer.EndLine(symbol);
        var surface = writer.Complete();

        await Assert.That(surface.Declarations.Length).IsEqualTo(1);
        await Assert.That(surface.Declarations[0].Text).IsEqualTo(TypeHeader);
    }

    /// <summary>Verifies normalization reads only the recorded span and preserves significant whitespace.</summary>
    /// <param name="raw">The declaration as written.</param>
    /// <param name="expected">The declaration text the comparison must retain.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    [Arguments("", "")]
    [Arguments(" \t\r\n \n", "")]
    [Arguments("public class Thing", "public class Thing")]
    [Arguments(" \tpublic class Thing \r", "public class Thing")]
    [Arguments("\u2003public class Thing\u00a0", "public class Thing")]
    [Arguments("\n \tpublic class Thing\r\n \n", "public class Thing")]
    [Arguments("    [Example]\n    public class Thing", "[Example]\npublic class Thing")]
    [Arguments("  [Example] \r\n \t\r\n\n public class Thing \r\n", "[Example]\npublic class Thing")]
    [Arguments("\u2003[Example]\u00a0\n\u2003public class Thing\u00a0", "[Example]\npublic class Thing")]
    [Arguments("  [Example(\"a  b\")]\n  public  class Thing", "[Example(\"a  b\")]\npublic  class Thing")]
    public async Task DeclarationTextIsNormalizedWithinItsSpanAsync(string raw, string expected)
    {
        const string prefix = "preceding\n";
        var text = $"{prefix}{raw}following\n";
        var surface = new RenderedApiSurface(
            text,
            [new(null, "ExampleAttribute", prefix.Length, prefix.Length + raw.Length, 1, 1)]);

        var declarations = surface.Declarations;

        await Assert.That(declarations.Length).IsEqualTo(1);
        await Assert.That(declarations[0].Text).IsEqualTo(expected);
        await Assert.That(declarations[0].Text).IsEqualTo(ApiTextParser.NormalizeText(raw));
        await Assert.That(declarations[0].Span).IsEqualTo(new(prefix.Length, raw.Length));
        await Assert.That(declarations[0].StartLine).IsEqualTo(1);
        await Assert.That(surface.Text).IsEqualTo(text);
        await Assert.That(surface.Declarations == declarations).IsTrue();
    }

    /// <summary>Verifies an empty surface retains an initialized empty declaration array.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task EmptySurfaceHasNoDeclarationsAsync()
    {
        var surface = new RenderedApiSurface(string.Empty, []);

        await Assert.That(surface.Declarations.IsDefault).IsFalse();
        await Assert.That(surface.Declarations).IsEmpty();
    }

    /// <summary>Verifies attributes map only their first line, while the signature still maps to its symbol.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task AttributedDeclarationMapsItsFirstAndSignatureLinesAsync()
    {
        var symbol = ApiSurfaceTestHost.Compile(TypeSource).GetTypeByMetadataName(TypeName)!;
        var writer = new ApiSurfaceRenderer.SurfaceWriter();
        writer.Line(string.Empty, "namespace Example;", null);
        writer.Line(string.Empty, string.Empty, null);
        writer.Pending = symbol;
        _ = writer.Builder.Append("[First]\n");
        writer.CountLine("First");
        _ = writer.Builder.Append("[Second]\n");
        writer.CountLine("Second");
        writer.Line(string.Empty, TypeHeader, symbol);
        writer.Line(string.Empty, "{", null);
        writer.Line(string.Empty, "}", null);
        var surface = writer.Complete();

        ISymbol?[] expected = [null, null, symbol, null, symbol, null, null, null];
        for (var line = 0; line < expected.Length; line++)
        {
            await Assert.That(surface.SymbolAtLine(line)).IsEqualTo(expected[line]);
        }
    }

    /// <summary>Verifies rendering retains line mappings for attributed types, members and enum fields.</summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Test]
    public async Task RenderedSymbolsMatchAttributeAndSignatureBoundariesAsync()
    {
        const string Source = """
                              [assembly: System.CLSCompliant(true)]
                              namespace Example;
                              [System.Obsolete, System.CLSCompliant(true)]
                              public enum Values
                              {
                                  [System.Obsolete, System.CLSCompliant(true)]
                                  One
                              }
                              [System.Obsolete, System.CLSCompliant(true)]
                              public interface IContract
                              {
                                  [System.Obsolete, System.CLSCompliant(true)]
                                  void Call();
                              }
                              """;
        var compilation = ApiSurfaceTestHost.Compile(Source);
        var surface = ApiSurfaceRenderer.Render(compilation, ApiRenderOptions.Default, CancellationToken.None);
        var text = SourceText.From(surface.Text);
        var parsed = ApiTextParser.Parse(text, CancellationToken.None);
        var values = compilation.GetTypeByMetadataName("Example.Values")!;
        var contract = compilation.GetTypeByMetadataName("Example.IContract")!;
        ISymbol[] symbols = [contract, contract.GetMembers("Call")[0], values, values.GetMembers("One")[0]];

        await Assert.That(parsed.Success).IsTrue();
        await Assert.That(parsed.Declarations.Length).IsEqualTo(symbols.Length + 1);
        await Assert.That(surface.SymbolAtLine(0)).IsNull();
        for (var index = 0; index < symbols.Length; index++)
        {
            var declaration = parsed.Declarations[index + 1];
            var lastLine = text.Lines.GetLineFromPosition(declaration.Span.End - 1).LineNumber;
            await Assert.That(surface.SymbolAtLine(declaration.StartLine)).IsEqualTo(symbols[index]);
            await Assert.That(surface.SymbolAtLine(declaration.StartLine + 1)).IsNull();
            await Assert.That(surface.SymbolAtLine(lastLine)).IsEqualTo(symbols[index]);
            await Assert.That(surface.SymbolAtLine(lastLine + 1)).IsNull();
        }
    }
}
