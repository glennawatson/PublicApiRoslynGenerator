// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Tests the normalized declarations retained from rendered text.</summary>
public class RenderedApiSurfaceTests
{
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
            [],
            ImmutableArrays.Of(new RenderedApiSurface.Written(null, "ExampleAttribute", prefix.Length, prefix.Length + raw.Length, 1)));

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
        var surface = new RenderedApiSurface(string.Empty, [], []);

        await Assert.That(surface.Declarations.IsDefault).IsFalse();
        await Assert.That(surface.Declarations).IsEmpty();
    }
}
