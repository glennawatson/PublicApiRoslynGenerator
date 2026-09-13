// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PublicApiSharp.Analyzers.Benchmarks;

/// <summary>Measures extension-block parsing on the slots that expose its syntax.</summary>
[ShortRunJob]
[MemoryDiagnoser]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class ExtensionParsingBenchmarks
{
    /// <summary>The rendered surface containing extension blocks.</summary>
    private SourceText _surface = null!;

    /// <summary>A generic type carrying a constraint clause.</summary>
    private TypeDeclarationSyntax _generic = null!;

    /// <summary>An extension block declaration, whose identity is its whole header.</summary>
    private MemberDeclarationSyntax _extension = null!;

    /// <summary>Renders and locates constrained syntax before measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _surface = SourceText.From(ApiSurfaceRenderer.Render(BenchmarkWorkload.Broad(), ApiRenderOptions.Default, CancellationToken.None).Text);
        var root = CSharpSyntaxTree.ParseText(_surface, new(LanguageVersion.Preview)).GetRoot();
        foreach (var node in root.DescendantNodes())
        {
            if (node is TypeDeclarationSyntax { ConstraintClauses.Count: > 0 } generic)
            {
                _generic = generic;
            }

            if (node is ExtensionBlockDeclarationSyntax extension)
            {
                _extension = extension;
            }
        }
    }

    /// <summary>Recognises an extension block and records the members it declares.</summary>
    /// <returns>Whether the member was an extension block.</returns>
    [Benchmark]
    public bool TryVisitExtensionBlock()
    {
        var builder = ImmutableArray.CreateBuilder<ApiDeclaration>();
        return ApiTextParser.TryVisitExtensionBlock(_extension, "Sample.Helpers", builder, _surface, CancellationToken.None);
    }

    /// <summary>Renders constraint clauses as the part of an identity that separates two blocks.</summary>
    /// <returns>The rendered clauses.</returns>
    [Benchmark]
    public string Constraints() => ApiTextParser.Constraints(_generic.ConstraintClauses);
}
