// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Unit tests for <see cref="PooledStringBuilder"/>, which backs all rendered text.</summary>
/// <remarks>
/// The buffer is rented from a thread-local pool and handed back on <see cref="object.ToString"/>,
/// so the cases worth pinning are the ones where a buffer changes hands: growth, draining another
/// builder, and reuse after a return.
/// </remarks>
public class PooledStringBuilderTests
{
    /// <summary>Content reused across the reuse-after-return cases.</summary>
    private const string First = "first";

    /// <summary>Verifies an empty builder renders as an empty string.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task EmptyBuilderRendersEmptyAsync()
    {
        var builder = new PooledStringBuilder();

        await Assert.That(builder.Length).IsEqualTo(0);
        await Assert.That(builder.ToString()).IsEmpty();
    }

    /// <summary>Verifies appending a null or empty string changes nothing.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task NullAndEmptyAppendsAreIgnoredAsync()
    {
        var builder = new PooledStringBuilder();
        _ = builder.Append((string?)null).Append(string.Empty);

        await Assert.That(builder.Length).IsEqualTo(0);
    }

    /// <summary>Verifies appending a prefix copies only the leading characters asked for.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task PrefixAppendCopiesOnlyTheLeadingCharactersAsync()
    {
        const int Prefix = 3;
        var builder = new PooledStringBuilder();

        _ = builder.Append("abcdef", Prefix);

        await Assert.That(builder.ToString()).IsEqualTo("abc");
    }

    /// <summary>Verifies a prefix longer than the string appends the whole string.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task PrefixAppendClampsToTheStringLengthAsync()
    {
        const int MoreThanAvailable = 99;
        var builder = new PooledStringBuilder();

        _ = builder.Append("ab", MoreThanAvailable);

        await Assert.That(builder.ToString()).IsEqualTo("ab");
    }

    /// <summary>Verifies a prefix append with nothing to copy changes nothing.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task PrefixAppendWithNothingToCopyIsIgnoredAsync()
    {
        const int Some = 4;
        const int None = 0;
        const int Negative = -1;
        var builder = new PooledStringBuilder();

        _ = builder.Append(null, Some).Append(string.Empty, Some).Append("abc", None).Append("abc", Negative);

        await Assert.That(builder.Length).IsEqualTo(0);
    }

    /// <summary>Verifies the character, integer and string overloads all append.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task AppendOverloadsWriteInOrderAsync()
    {
        const int Answer = 42;

        var builder = new PooledStringBuilder();
        _ = builder.Append("value").Append('=').Append(Answer);

        await Assert.That(builder.ToString()).IsEqualTo("value=42");
    }

    /// <summary>Verifies a character can be repeated.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task RepeatedCharacterIsAppendedAsync()
    {
        const int Count = 4;

        var builder = new PooledStringBuilder();
        _ = builder.Append(' ', Count).Append('x');

        await Assert.That(builder.ToString()).IsEqualTo("    x");
    }

    /// <summary>Verifies repeating a character a non-positive number of times does nothing.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task NonPositiveRepeatIsIgnoredAsync()
    {
        var builder = new PooledStringBuilder();
        _ = builder.Append('x', 0).Append('y', -1);

        await Assert.That(builder.Length).IsEqualTo(0);
    }

    /// <summary>Verifies the line overloads append the fixed terminator.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The terminator is always a line feed so a baseline reads identically on every operating
    /// system; a file committed on one platform must not look changed on another.
    /// </remarks>
    [Test]
    public async Task LineTerminatorIsAlwaysLineFeedAsync()
    {
        var builder = new PooledStringBuilder();
        _ = builder.AppendLine(First).AppendLine();

        await Assert.That(builder.ToString()).IsEqualTo($"{First}\n\n");
    }

    /// <summary>Verifies one builder can be drained into another.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task AppendingAnotherBuilderDrainsItAsync()
    {
        var inner = new PooledStringBuilder();
        _ = inner.Append("inner");

        var outer = new PooledStringBuilder();
        _ = outer.Append("outer:").Append(inner);

        await Assert.That(outer.ToString()).IsEqualTo("outer:inner");
        await Assert.That(inner.Length).IsEqualTo(0);
    }

    /// <summary>Verifies draining an empty builder is harmless.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task AppendingAnEmptyBuilderIsHarmlessAsync()
    {
        var outer = new PooledStringBuilder();
        _ = outer.Append("kept").Append(new PooledStringBuilder());

        await Assert.That(outer.ToString()).IsEqualTo("kept");
    }

    /// <summary>Verifies trailing characters can be dropped.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task TrimDropsTrailingCharactersAsync()
    {
        const int Two = 2;

        var builder = new PooledStringBuilder();
        _ = builder.Append("abcde").Trim(Two);

        await Assert.That(builder.ToString()).IsEqualTo("abc");
    }

    /// <summary>Verifies trimming more than is present empties the builder.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task TrimBeyondTheContentEmptiesItAsync()
    {
        const int TooMany = 99;

        var builder = new PooledStringBuilder();
        _ = builder.Append("abc").Trim(TooMany);

        await Assert.That(builder.Length).IsEqualTo(0);
    }

    /// <summary>Verifies the buffer grows past its initial capacity without losing content.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>Growth swaps the backing array and returns the old one to the pool.</remarks>
    [Test]
    public async Task BufferGrowsBeyondInitialCapacityAsync()
    {
        const int Repeats = 400;
        const string Chunk = "0123456789";

        var builder = new PooledStringBuilder();
        for (var i = 0; i < Repeats; i++)
        {
            _ = builder.Append(Chunk);
        }

        var result = builder.ToString();

        await Assert.That(result).Length().IsEqualTo(Repeats * Chunk.Length);
        await Assert.That(result.StartsWith(Chunk, StringComparison.Ordinal)).IsTrue();
        await Assert.That(result.EndsWith(Chunk, StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>Verifies a requested capacity below the floor still works.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task SmallRequestedCapacityIsRaisedToTheFloorAsync()
    {
        const int Tiny = 1;

        var builder = new PooledStringBuilder(Tiny);
        _ = builder.Append("more than one character");

        await Assert.That(builder.ToString()).IsEqualTo("more than one character");
    }

    /// <summary>Verifies capacity can be reserved up front.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Test]
    public async Task CapacityCanBeReservedAsync()
    {
        const int Large = 5000;

        var builder = new PooledStringBuilder();
        builder.EnsureCapacity(Large);
        _ = builder.Append("kept");

        await Assert.That(builder.ToString()).IsEqualTo("kept");
    }

    /// <summary>Verifies a builder is reusable after its buffer has been returned.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// An instance is single-use by contract, but returning the buffer must leave it in a state
    /// that still behaves rather than throwing.
    /// </remarks>
    [Test]
    public async Task BuilderIsUsableAfterToStringAsync()
    {
        var builder = new PooledStringBuilder();
        _ = builder.Append(First);

        await Assert.That(builder.ToString()).IsEqualTo(First);

        _ = builder.Append("second");

        await Assert.That(builder.ToString()).IsEqualTo("second");
    }

    /// <summary>Verifies returning more buffers than the pool holds is harmless.</summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    /// <remarks>
    /// The free list is a fixed size, so past that point a returned buffer is simply dropped for the
    /// garbage collector rather than growing the pool without bound.
    /// </remarks>
    [Test]
    public async Task ReturningMoreBuffersThanThePoolHoldsIsHarmlessAsync()
    {
        const int MoreThanThePoolHolds = 40;

        // Hold them all open first: returning one at a time would just hand the same buffer back
        // and forth, and the free list would never actually fill.
        var builders = new List<PooledStringBuilder>(MoreThanThePoolHolds);
        for (var i = 0; i < MoreThanThePoolHolds; i++)
        {
            var builder = new PooledStringBuilder();
            _ = builder.Append("content");
            builders.Add(builder);
        }

        foreach (var builder in builders)
        {
            _ = builder.ToString();
        }

        var reused = new PooledStringBuilder();
        _ = reused.Append("still works");

        await Assert.That(reused.ToString()).IsEqualTo("still works");
    }
}
