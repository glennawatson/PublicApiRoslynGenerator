// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Unit tests for <see cref="ApiLiterals"/>, which spells values the way C# source does.</summary>
/// <remarks>
/// The mappings a real declaration reaches are covered by the rendering tests. What is pinned here
/// is what happens off the end of those mappings, which no source snippet can produce: a compiler
/// that grows a new operator, or a constant of a shape the language does not currently allow.
/// </remarks>
public class ApiLiteralsTests
{
    /// <summary>Verifies an operator the mapping does not know falls back to its metadata name.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task UnmappedOperatorFallsBackToItsMetadataNameAsync() =>
        await Assert.That(ApiLiterals.OperatorToken("op_SomethingNewer")).IsEqualTo("op_SomethingNewer");

    /// <summary>Verifies the operators a declaration can actually use map to their tokens.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task MappedOperatorsUseTheirTokensAsync()
    {
        await Assert.That(ApiLiterals.OperatorToken("op_Addition")).IsEqualTo("+");
        await Assert.That(ApiLiterals.OperatorToken("op_UnsignedRightShift")).IsEqualTo(">>>");
        await Assert.That(ApiLiterals.OperatorToken("op_False")).IsEqualTo("false");
    }

    /// <summary>Verifies each constant shape the language allows renders as its literal.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task ConstantsRenderAsTheirLiteralsAsync()
    {
        const int Number = 42;

        await Assert.That(ApiLiterals.FormatConstant(null)).IsEqualTo("null");
        await Assert.That(ApiLiterals.FormatConstant("text")).IsEqualTo("\"text\"");
        await Assert.That(ApiLiterals.FormatConstant('c')).IsEqualTo("'c'");
        await Assert.That(ApiLiterals.FormatConstant(true)).IsEqualTo("true");
        await Assert.That(ApiLiterals.FormatConstant(false)).IsEqualTo("false");
        await Assert.That(ApiLiterals.FormatConstant(Number)).IsEqualTo("42");
    }

    /// <summary>Verifies a value outside those shapes falls back to its own rendering.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// A C# constant is always null, a string, a char, a bool or a number, so nothing a compilation
    /// holds reaches this. It is what keeps an unexpected value from becoming an exception in an
    /// analyzer, whether the value renders itself or renders as nothing.
    /// </remarks>
    [Test]
    public async Task ValueOutsideTheConstantShapesUsesItsOwnRenderingAsync()
    {
        await Assert.That(ApiLiterals.FormatConstant(new Version(1, 2))).IsEqualTo("1.2");
        await Assert.That(ApiLiterals.FormatConstant(DBNull.Value)).IsEmpty();
    }
}
