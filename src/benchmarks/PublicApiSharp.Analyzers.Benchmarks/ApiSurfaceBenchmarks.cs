// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Text;

using BenchmarkDotNet.Attributes;

using BenchmarkDotNet.Diagnosers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Benchmarks;

/// <summary>Measures the two compilation-wide steps: rendering the surface, and reading it back.</summary>
/// <remarks>
/// These run once per compilation rather than per keystroke, but they scale with the size of the
/// public surface, so a large library is where any regression shows up. The type count is a
/// parameter so a change can be judged on how it scales rather than on one data point.
/// </remarks>
[ShortRunJob]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class ApiSurfaceBenchmarks
{
    /// <summary>The compilation whose surface is rendered.</summary>
    private CSharpCompilation _compilation = null!;

    /// <summary>That compilation's surface, already rendered, for the parse benchmark.</summary>
    private SourceText _rendered = null!;

    /// <summary>Gets or sets the number of public types the benchmarked assembly declares.</summary>
    [Params(BenchmarkParameterValues.SmallTypeCount, BenchmarkParameterValues.LargeTypeCount)]
    public int TypeCount { get; set; }

    /// <summary>Builds the compilation and its rendered surface once per parameter set.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _compilation = CreateCompilation(TypeCount);
        _rendered = SourceText.From(
            ApiSurfaceRenderer.Render(_compilation, ApiRenderOptions.Default, CancellationToken.None).Text);
    }

    /// <summary>Renders the compilation's public surface.</summary>
    /// <returns>The rendered text, returned so the work cannot be optimized away.</returns>
    [Benchmark]
    public string Render() =>
        ApiSurfaceRenderer.Render(_compilation, ApiRenderOptions.Default, CancellationToken.None).Text;

    /// <summary>Reads a rendered surface back into declarations, as the baseline comparison does.</summary>
    /// <returns>The number of declarations, returned so the work cannot be optimized away.</returns>
    [Benchmark]
    public int Parse() => ApiTextParser.Parse(_rendered, CancellationToken.None).Declarations.Length;

    /// <summary>Builds a compilation with the requested number of public types.</summary>
    /// <param name="typeCount">How many types to declare.</param>
    /// <returns>The compilation.</returns>
    private static CSharpCompilation CreateCompilation(int typeCount)
    {
        var source = new StringBuilder("namespace Sample;\n\n");
        for (var i = 0; i < typeCount; i++)
        {
            _ = source.Append("public class Thing").Append(i).Append("\n{\n")
                .Append("    public int Value { get; set; }\n")
                .Append("    public string? Name { get; init; }\n")
                .Append("    public System.Collections.Generic.IReadOnlyList<int> Items { get; } = [];\n")
                .Append("    public void Go(int value, string name = \"\", System.Threading.CancellationToken token = default) { }\n")
                .Append("    public T? Find<T>(T seed) where T : class => null;\n")
                .Append("}\n\n");
        }

        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (var path in trusted.Split(Path.PathSeparator))
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return CSharpCompilation.Create(
            "BenchmarkAssembly",
            [CSharpSyntaxTree.ParseText(source.ToString(), new(LanguageVersion.Preview))],
            references.ToImmutable(),
            new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }
}
