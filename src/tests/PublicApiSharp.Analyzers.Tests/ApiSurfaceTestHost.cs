// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PublicApiSharp.Analyzers.Tests;

/// <summary>Compiles a snippet and renders its public API surface, for tests that assert on the text.</summary>
internal static class ApiSurfaceTestHost
{
    /// <summary>The references every test compilation gets: whatever the test host itself runs against.</summary>
    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    /// <summary>Compiles the source and renders the surface the analyzer would compare against a baseline.</summary>
    /// <param name="source">The C# source to compile.</param>
    /// <param name="options">The render options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The rendered surface text.</returns>
    internal static string Render(string source, ApiRenderOptions? options = null)
    {
        var compilation = Compile(source);
        return ApiSurfaceRenderer.Render(compilation, options ?? ApiRenderOptions.Default, CancellationToken.None).Text;
    }

    /// <summary>Compiles the source, asserting it has no errors so a broken snippet fails loudly.</summary>
    /// <param name="source">The C# source to compile.</param>
    /// <returns>The compilation.</returns>
    internal static CSharpCompilation Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [tree],
            References,
            new(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var errors = new List<string>();
        foreach (var diagnostic in compilation.GetDiagnostics())
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                errors.Add(diagnostic.ToString());
            }
        }

        return errors.Count == 0
            ? compilation
            : throw new InvalidOperationException($"Test source did not compile:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
    }

    /// <summary>Builds the metadata references from the assemblies loaded into the test host.</summary>
    /// <returns>The references.</returns>
    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;

        foreach (var path in trusted.Split(Path.PathSeparator))
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                builder.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return builder.ToImmutable();
    }
}
