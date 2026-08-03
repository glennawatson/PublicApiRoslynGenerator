// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Globalization;

namespace RoslynCommon.Analyzers;

/// <summary>A fluent string builder that grows using thread-local pooled buffers.</summary>
/// <remarks>
/// <para>
/// Rendering an API surface builds one large document out of a great many transient fragments — a
/// modifier prefix, a signature, an attribute application, per declaration. Backing accumulation
/// with a pooled <c>char[]</c> lets the underlying buffer be reused across those fragments instead
/// of allocating fresh <see cref="System.Text.StringBuilder"/> chunks each time.
/// </para>
/// <para>
/// <see cref="ToString"/> returns the buffer to the pool, so an instance is single-use.
/// </para>
/// </remarks>
internal sealed class PooledStringBuilder
{
    /// <summary>The default rented capacity, sized to hold a typical declaration without growing.</summary>
    private const int DefaultCapacity = 256;

    /// <summary>The buffer growth factor applied when the current backing array is exhausted.</summary>
    private const int GrowthFactor = 2;

    /// <summary>
    /// The number of buffers cached per thread, sized to cover the renderer's nesting depth
    /// (document, namespace, type, member and attribute builders alive at once) so nested rents
    /// stay on the lock-free path.
    /// </summary>
    private const int MaxPooledPerThread = 16;

    /// <summary>The line terminator the builder emits.</summary>
    /// <remarks>
    /// Fixed to <c>\n</c> so a baseline renders identically on every operating system; a file
    /// committed on one platform must not read as changed on another. Analyzers must never consult
    /// <see cref="Environment.NewLine"/> for content.
    /// </remarks>
    private const string NewLine = "\n";

    /// <summary>
    /// The per-thread free list of reusable buffers. Thread-local so rent and return never lock;
    /// the renderer nests builders on one thread, which exhausts the single-slot lock-free tier of
    /// the shared array pool.
    /// </summary>
    [ThreadStatic]
    private static char[][]? _pool;

    /// <summary>The number of populated slots in <see cref="_pool"/>.</summary>
    [ThreadStatic]
    private static int _pooledCount;

    /// <summary>The pooled array currently backing the builder.</summary>
    private char[] _buffer;

    /// <summary>The current write position within the buffer.</summary>
    private int _pos;

    /// <summary>Initializes a new instance of the <see cref="PooledStringBuilder"/> class.</summary>
    internal PooledStringBuilder()
        : this(DefaultCapacity)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PooledStringBuilder"/> class with an initial capacity.</summary>
    /// <param name="capacity">The initial buffer capacity to rent.</param>
    internal PooledStringBuilder(int capacity) => _buffer = RentBuffer(Math.Max(capacity, DefaultCapacity));

    /// <summary>Gets the number of characters accumulated so far.</summary>
    internal int Length => _pos;

    /// <summary>Materializes the accumulated content into a string and returns the pooled buffer.</summary>
    /// <returns>The accumulated string.</returns>
    public override string ToString()
    {
        var result = _pos == 0 ? string.Empty : new string(_buffer, 0, _pos);
        var toReturn = _buffer;
        _buffer = [];
        _pos = 0;
        ReturnBuffer(toReturn);
        return result;
    }

    /// <summary>Rents a buffer of at least the requested length from the thread-local free list, or allocates one.</summary>
    /// <param name="minimumLength">The minimum buffer length required.</param>
    /// <returns>A buffer whose length is at least <paramref name="minimumLength"/>.</returns>
    internal static char[] RentBuffer(int minimumLength)
    {
        var pool = _pool;
        if (pool is not null)
        {
            for (var i = _pooledCount - 1; i >= 0; i--)
            {
                var candidate = pool[i];
                if (candidate.Length >= minimumLength)
                {
                    pool[i] = pool[_pooledCount - 1];
                    pool[_pooledCount - 1] = null!;
                    _pooledCount--;
                    return candidate;
                }
            }
        }

        return new char[minimumLength];
    }

    /// <summary>Returns a buffer to the thread-local free list, dropping it when the list is full.</summary>
    /// <param name="buffer">The rented buffer to return.</param>
    internal static void ReturnBuffer(char[] buffer)
    {
        var pool = _pool ??= new char[MaxPooledPerThread][];
        if (_pooledCount >= MaxPooledPerThread)
        {
            return;
        }

        pool[_pooledCount] = buffer;
        _pooledCount++;
    }

    /// <summary>Appends a string.</summary>
    /// <param name="value">The string to append, or null.</param>
    /// <returns>This builder, for chaining.</returns>
    internal PooledStringBuilder Append(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return this;
        }

        EnsureCapacity(_pos + value!.Length);
        value.CopyTo(0, _buffer, _pos, value.Length);
        _pos += value.Length;
        return this;
    }

    /// <summary>Appends the leading part of a string.</summary>
    /// <param name="value">The string to take from, or null.</param>
    /// <param name="count">How many characters to take from its start.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>Copying a prefix straight out of the source avoids materializing the substring.</remarks>
    internal PooledStringBuilder Append(string? value, int count)
    {
        if (string.IsNullOrEmpty(value) || count <= 0)
        {
            return this;
        }

        var length = Math.Min(count, value!.Length);
        EnsureCapacity(_pos + length);
        value.CopyTo(0, _buffer, _pos, length);
        _pos += length;
        return this;
    }

    /// <summary>Appends the invariant decimal rendering of a 32-bit integer.</summary>
    /// <param name="value">The value to append.</param>
    /// <returns>This builder, for chaining.</returns>
    internal PooledStringBuilder Append(int value) => Append(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Appends a single character.</summary>
    /// <param name="value">The character to append.</param>
    /// <returns>This builder, for chaining.</returns>
    internal PooledStringBuilder Append(char value)
    {
        EnsureCapacity(_pos + 1);
        _buffer[_pos] = value;
        _pos++;
        return this;
    }

    /// <summary>Appends the same character a number of times.</summary>
    /// <param name="value">The character to append.</param>
    /// <param name="count">How many times to append it.</param>
    /// <returns>This builder, for chaining.</returns>
    internal PooledStringBuilder Append(char value, int count)
    {
        if (count <= 0)
        {
            return this;
        }

        EnsureCapacity(_pos + count);
        for (var i = 0; i < count; i++)
        {
            _buffer[_pos + i] = value;
        }

        _pos += count;
        return this;
    }

    /// <summary>Appends the accumulated content of another builder, then returns that builder's buffer to the pool.</summary>
    /// <param name="other">The builder whose content is copied in; it is emptied and single-use afterward.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <remarks>
    /// Copies buffer to buffer so a nested fragment builder's content joins this one without
    /// materializing an intermediate string. The source is drained (its buffer returned to the
    /// pool), matching <see cref="ToString"/>.
    /// </remarks>
    internal PooledStringBuilder Append(PooledStringBuilder other)
    {
        if (other._pos != 0)
        {
            EnsureCapacity(_pos + other._pos);
            Array.Copy(other._buffer, 0, _buffer, _pos, other._pos);
            _pos += other._pos;
        }

        var toReturn = other._buffer;
        other._buffer = [];
        other._pos = 0;
        ReturnBuffer(toReturn);
        return this;
    }

    /// <summary>Appends a string followed by a line terminator.</summary>
    /// <param name="value">The string to append, or null.</param>
    /// <returns>This builder, for chaining.</returns>
    internal PooledStringBuilder AppendLine(string? value) => Append(value).Append(NewLine);

    /// <summary>Appends a line terminator.</summary>
    /// <returns>This builder, for chaining.</returns>
    internal PooledStringBuilder AppendLine() => Append(NewLine);

    /// <summary>Removes trailing characters.</summary>
    /// <param name="count">How many characters to drop.</param>
    /// <returns>This builder, for chaining.</returns>
    internal PooledStringBuilder Trim(int count)
    {
        _pos = count >= _pos ? 0 : _pos - count;
        return this;
    }

    /// <summary>Ensures the backing buffer can hold at least the requested number of characters.</summary>
    /// <param name="required">The required total capacity.</param>
    internal void EnsureCapacity(int required)
    {
        if (required <= _buffer.Length)
        {
            return;
        }

        var next = RentBuffer(Math.Max(required, _buffer.Length * GrowthFactor));
        Array.Copy(_buffer, next, _pos);
        var toReturn = _buffer;
        _buffer = next;
        ReturnBuffer(toReturn);
    }
}
