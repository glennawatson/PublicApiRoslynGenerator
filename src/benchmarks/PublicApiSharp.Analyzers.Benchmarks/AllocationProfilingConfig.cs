// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Diagnostics.Tracing;

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;

using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace PublicApiSharp.Analyzers.Benchmarks;

/// <summary>Runs the benchmarks under an EventPipe session that records where allocations come from.</summary>
/// <remarks>
/// Public because BenchmarkDotNet constructs it reflectively from the <c>[Config]</c> attribute.
/// The stock <c>GcVerbose</c> profile describes what the GC did — collections, heap sizes, survival —
/// but emits no event carrying an allocation call stack, so it cannot say which method is responsible.
/// This adds the sampled-allocation and stack keywords on top of it, which is what turns the trace
/// from "how much was collected" into "who allocated it".
/// </remarks>
public sealed class AllocationProfilingConfig : ManualConfig
{
    /// <summary>Initializes a new instance of the <see cref="AllocationProfilingConfig"/> class.</summary>
    public AllocationProfilingConfig()
    {
        _ = AddDiagnoser(MemoryDiagnoser.Default);
        _ = AddDiagnoser(new EventPipeProfiler(providers:
        [
            new EventPipeProvider(
                ClrTraceEventParser.ProviderName,
                EventLevel.Verbose,
                (long)(ClrTraceEventParser.Keywords.GC
                    | ClrTraceEventParser.Keywords.GCHandle
                    | ClrTraceEventParser.Keywords.Type
                    | ClrTraceEventParser.Keywords.GCHeapAndTypeNames
                    | ClrTraceEventParser.Keywords.GCSampledObjectAllocationHigh
                    | ClrTraceEventParser.Keywords.Stack)),
        ]));
    }
}
