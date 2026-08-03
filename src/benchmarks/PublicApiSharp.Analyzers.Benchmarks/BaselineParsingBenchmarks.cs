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

/// <summary>Measures reading surface text back into the declarations the comparison keys on.</summary>
/// <remarks>
/// This runs over the checked-in baseline and over the freshly rendered surface, so every cost here
/// is paid twice per compilation. The identity helpers underneath it run once per declaration.
/// </remarks>
[ShortRunJob]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class BaselineParsingBenchmarks
{
    /// <summary>Surface text that stops parsing part way, for the unreadable-baseline path.</summary>
    private const string Malformed = "namespace Sample;\n\npublic class Thing\n{\n    public int Value { get; set\n";

    /// <summary>Declaration text carrying the indentation of where it sits in a file.</summary>
    private const string Indented = "        public int Value { get; set; }\n            // trailing\n";

    /// <summary>A type reference with the spacing symbol display leaves in.</summary>
    private const string SpacedTypeReference = "System.Collections.Generic.IReadOnlyList<System.String>";

    /// <summary>A rendered surface, as both sides of the comparison see it.</summary>
    private SourceText _surface = null!;

    /// <summary>A method declaration, for the identity helpers.</summary>
    private MethodDeclarationSyntax _method = null!;

    /// <summary>A generic type declaration carrying constraint clauses.</summary>
    private TypeDeclarationSyntax _generic = null!;

    /// <summary>An extension block declaration, whose identity is its whole header.</summary>
    private MemberDeclarationSyntax _extension = null!;

    /// <summary>A single parameter, for the per-parameter identity path.</summary>
    private ParameterSyntax _parameter = null!;

    /// <summary>Renders a surface once and parses it into the nodes the helpers are given.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var rendered = ApiSurfaceRenderer.Render(BenchmarkWorkload.Broad(), ApiRenderOptions.Default, CancellationToken.None);
        _surface = SourceText.From(rendered.Text);

        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        var root = CSharpSyntaxTree.ParseText(_surface, parseOptions).GetRoot();

        _method = First<MethodDeclarationSyntax>(root, static node => node.ParameterList.Parameters.Count > 1);
        _generic = First<TypeDeclarationSyntax>(root, static node => node.ConstraintClauses.Count > 0);
        _extension = First<MemberDeclarationSyntax>(
            root,
            static node => node.ToString().StartsWith("extension", StringComparison.Ordinal));
        _parameter = _method.ParameterList.Parameters[0];
    }

    /// <summary>Parses a whole surface into declarations, as the comparison does twice per build.</summary>
    /// <returns>The declaration count, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int ParseSurface() => ApiTextParser.Parse(_surface, CancellationToken.None).Declarations.Length;

    /// <summary>Runs only Roslyn's own parse, to say how much of the round trip is ours to improve.</summary>
    /// <returns>The node count, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int SyntaxParseOnly()
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        return CSharpSyntaxTree.ParseText(_surface, parseOptions).GetRoot().FullSpan.Length;
    }

    /// <summary>Parses text that is not valid C#, the path that reports an unreadable baseline.</summary>
    /// <returns>Whether the parse succeeded, so the work cannot be optimized away.</returns>
    [Benchmark]
    public bool ParseMalformed() => ApiTextParser.Parse(SourceText.From(Malformed), CancellationToken.None).Success;

    /// <summary>Visits one member declaration, building its identity.</summary>
    /// <returns>The number of declarations recorded.</returns>
    [Benchmark]
    public int VisitMember()
    {
        var builder = ImmutableArray.CreateBuilder<ApiDeclaration>();
        ApiTextParser.VisitMember(_generic, "Sample", builder, _surface, CancellationToken.None);
        return builder.Count;
    }

    /// <summary>Recognises an extension block and records the members it declares.</summary>
    /// <returns>Whether the member was an extension block.</returns>
    [Benchmark]
    public bool TryVisitExtensionBlock()
    {
        var builder = ImmutableArray.CreateBuilder<ApiDeclaration>();
        return ApiTextParser.TryVisitExtensionBlock(_extension, "Sample.Helpers", builder, _surface, CancellationToken.None);
    }

    /// <summary>Renders a parameter list the way overload identity sees it.</summary>
    /// <returns>The rendered list.</returns>
    [Benchmark]
    public string Parameters() => ApiTextParser.Parameters(_method.ParameterList);

    /// <summary>Appends one parameter's contribution to an overload identity.</summary>
    /// <returns>The rendered fragment.</returns>
    [Benchmark]
    public string AppendParameterIdentity()
    {
        var builder = new PooledStringBuilder();
        ApiTextParser.AppendParameterIdentity(builder, _parameter);
        return builder.ToString();
    }

    /// <summary>Renders constraint clauses as the part of an identity that separates two blocks.</summary>
    /// <returns>The rendered clauses.</returns>
    [Benchmark]
    public string Constraints() => ApiTextParser.Constraints(_generic.ConstraintClauses);

    /// <summary>Strips the indentation a declaration carries from where it sits in the file.</summary>
    /// <returns>The normalized text.</returns>
    [Benchmark]
    public string NormalizeText() => ApiTextParser.NormalizeText(Indented);

    /// <summary>Removes every whitespace character from a type reference.</summary>
    /// <returns>The stripped text.</returns>
    [Benchmark]
    public string RemoveWhitespace() => ApiTextParser.RemoveWhitespace(SpacedTypeReference);

    /// <summary>Computes the span a type declaration's own header occupies.</summary>
    /// <returns>The span length, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int HeaderSpan() => ApiTextParser.HeaderSpan(_generic, _generic.OpenBraceToken).Length;

    /// <summary>Finds the first node of a kind that satisfies a predicate.</summary>
    /// <typeparam name="T">The node kind.</typeparam>
    /// <param name="root">The tree to search.</param>
    /// <param name="predicate">The condition the node must meet.</param>
    /// <returns>The node.</returns>
    /// <exception cref="InvalidOperationException">The rendered surface has no such node.</exception>
    private static T First<T>(SyntaxNode root, Func<T, bool> predicate)
        where T : SyntaxNode
    {
        foreach (var node in root.DescendantNodes())
        {
            if (node is T typed && predicate(typed))
            {
                return typed;
            }
        }

        throw new InvalidOperationException($"The rendered surface has no matching {nameof(T)}.");
    }
}
