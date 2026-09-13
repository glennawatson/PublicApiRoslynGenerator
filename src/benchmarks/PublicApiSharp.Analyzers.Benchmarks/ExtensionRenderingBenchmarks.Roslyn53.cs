// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;

using Microsoft.CodeAnalysis;

namespace PublicApiSharp.Analyzers.Benchmarks;

/// <summary>Measures extension rendering only where the syntax can round-trip.</summary>
[ShortRunJob]
[MemoryDiagnoser]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class ExtensionRenderingBenchmarks
{
    /// <summary>An extension container, whose header is composed by hand.</summary>
    private INamedTypeSymbol _extension = null!;

    /// <summary>Resolves the first extension container in the workload.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var helpers = BenchmarkWorkload.Type(BenchmarkWorkload.Broad(), "Sample.Helpers");
        foreach (var nested in helpers.GetTypeMembers())
        {
            if (RoslynFeatures.IsExtensionContainer(nested))
            {
                _extension = nested;
                break;
            }
        }
    }

    /// <summary>Renders an extension block header, which is composed rather than displayed.</summary>
    /// <returns>The rendered text.</returns>
    [Benchmark]
    public string AppendExtensionHeader()
    {
        var builder = new PooledStringBuilder();
        ApiSurfaceRenderer.AppendExtensionHeader(builder, _extension);
        return builder.ToString();
    }

    /// <summary>Builds the key an extension container orders under.</summary>
    /// <returns>The sort key.</returns>
    [Benchmark]
    public string TypeSortKey() => ApiSurfaceRenderer.TypeSortKey(_extension);
}
