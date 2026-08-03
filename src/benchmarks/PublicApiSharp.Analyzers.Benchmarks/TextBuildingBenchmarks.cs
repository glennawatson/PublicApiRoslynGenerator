// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;

namespace PublicApiSharp.Analyzers.Benchmarks;

/// <summary>Measures the buffer every fragment of the surface is accumulated through.</summary>
/// <remarks>
/// Rendering a surface builds one large document out of a great many small fragments, so this type
/// is on the hottest path in the package. It is single-use by design — <c>ToString</c> hands the
/// buffer back to the pool — which is why each benchmark constructs its own.
/// </remarks>
[ShortRunJob]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class TextBuildingBenchmarks
{
    /// <summary>A fragment of about the size a rendered declaration reaches.</summary>
    private const string Declaration = "    public System.Collections.Generic.IReadOnlyList<int> Items { get; }";

    /// <summary>One level of indentation, in characters.</summary>
    private const int IndentWidth = 4;

    /// <summary>The length of the prefix the partial-append benchmark copies.</summary>
    private const int PrefixLength = 20;

    /// <summary>The capacity each fragment asks for when forcing the builder to grow.</summary>
    private const int GrowthPerFragment = 512;

    /// <summary>The length of the ", " separator a rewind drops.</summary>
    private const int SeparatorLength = 2;

    /// <summary>A buffer size typical of one declaration.</summary>
    private const int TypicalBufferSize = 256;

    /// <summary>Gets or sets how many fragments are appended, so growth can be told from appending.</summary>
    [Params(BenchmarkParameterValues.SmallFragmentCount, BenchmarkParameterValues.LargeFragmentCount)]
    public int Fragments { get; set; }

    /// <summary>Appends string fragments and materializes the result.</summary>
    /// <returns>The accumulated length, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int AppendStrings()
    {
        var builder = new PooledStringBuilder();
        for (var i = 0; i < Fragments; i++)
        {
            _ = builder.Append(Declaration);
        }

        return builder.ToString().Length;
    }

    /// <summary>Appends single characters, the punctuation path.</summary>
    /// <returns>The accumulated length, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int AppendChars()
    {
        var builder = new PooledStringBuilder();
        for (var i = 0; i < Fragments; i++)
        {
            _ = builder.Append('(').Append(')').Append(';');
        }

        return builder.ToString().Length;
    }

    /// <summary>Appends a prefix of a string without materializing the substring.</summary>
    /// <returns>The accumulated length, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int AppendPrefix()
    {
        var builder = new PooledStringBuilder();
        for (var i = 0; i < Fragments; i++)
        {
            _ = builder.Append(Declaration, PrefixLength);
        }

        return builder.ToString().Length;
    }

    /// <summary>Appends integers, which each render through the invariant culture.</summary>
    /// <returns>The accumulated length, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int AppendIntegers()
    {
        var builder = new PooledStringBuilder();
        for (var i = 0; i < Fragments; i++)
        {
            _ = builder.Append(i);
        }

        return builder.ToString().Length;
    }

    /// <summary>Appends a repeated character, the indentation path.</summary>
    /// <returns>The accumulated length, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int AppendRepeatedChar()
    {
        var builder = new PooledStringBuilder();
        for (var i = 0; i < Fragments; i++)
        {
            _ = builder.Append(' ', IndentWidth);
        }

        return builder.ToString().Length;
    }

    /// <summary>Appends lines, as the surface writer does per declaration.</summary>
    /// <returns>The accumulated length, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int AppendLines()
    {
        var builder = new PooledStringBuilder();
        for (var i = 0; i < Fragments; i++)
        {
            _ = builder.AppendLine(Declaration);
        }

        return builder.AppendLine().ToString().Length;
    }

    /// <summary>Drains a nested fragment builder into an outer one, buffer to buffer.</summary>
    /// <returns>The accumulated length, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int AppendNestedBuilder()
    {
        var outer = new PooledStringBuilder();
        for (var i = 0; i < Fragments; i++)
        {
            var inner = new PooledStringBuilder();
            _ = inner.Append(Declaration);
            _ = outer.Append(inner);
        }

        return outer.ToString().Length;
    }

    /// <summary>Grows past the default capacity, which rents a larger buffer and copies.</summary>
    /// <returns>The accumulated length, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int GrowBeyondDefaultCapacity()
    {
        var builder = new PooledStringBuilder();
        builder.EnsureCapacity(Fragments * GrowthPerFragment);
        _ = builder.Append(Declaration);
        return builder.ToString().Length;
    }

    /// <summary>Drops trailing characters, as a separator rewind does.</summary>
    /// <returns>The accumulated length, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int TrimTrailing()
    {
        var builder = new PooledStringBuilder();
        for (var i = 0; i < Fragments; i++)
        {
            _ = builder.Append(Declaration).Append(", ");
        }

        return builder.Trim(SeparatorLength).ToString().Length;
    }

    /// <summary>Rents and returns a buffer, the pooling path underneath every builder.</summary>
    /// <returns>The buffer length, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int RentAndReturnBuffer()
    {
        var buffer = PooledStringBuilder.RentBuffer(TypicalBufferSize);
        var length = buffer.Length;
        PooledStringBuilder.ReturnBuffer(buffer);
        return length;
    }
}
