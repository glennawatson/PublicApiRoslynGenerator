// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Running;

namespace PublicApiSharp.Analyzers.Benchmarks;

/// <summary>The benchmark entry point.</summary>
internal static class Program
{
    /// <summary>Runs the benchmarks named on the command line.</summary>
    /// <param name="args">The BenchmarkDotNet arguments.</param>
    internal static void Main(string[] args) =>
        _ = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
