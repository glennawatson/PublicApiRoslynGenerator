// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

namespace PublicApiSharp.Analyzers.Benchmarks;

/// <summary>Shared benchmark parameter values used by the benchmark suites.</summary>
internal static class BenchmarkParameterValues
{
    /// <summary>The smaller public-surface size, standing for an ordinary library.</summary>
    internal const int SmallTypeCount = 10;

    /// <summary>The larger public-surface size, where a per-declaration cost becomes visible.</summary>
    internal const int LargeTypeCount = 100;

    /// <summary>A single text fragment, which isolates the cost of one append.</summary>
    internal const int SmallFragmentCount = 1;

    /// <summary>Enough fragments to force the builder past its default capacity.</summary>
    internal const int LargeFragmentCount = 32;
}
