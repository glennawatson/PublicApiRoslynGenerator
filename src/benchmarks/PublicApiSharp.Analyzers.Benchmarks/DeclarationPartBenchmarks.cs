// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;

using BenchmarkDotNet.Attributes;

using BenchmarkDotNet.Diagnosers;

using Microsoft.CodeAnalysis;

namespace PublicApiSharp.Analyzers.Benchmarks;

/// <summary>Measures the pieces a declaration is assembled from: modifiers, constraints and literals.</summary>
/// <remarks>
/// Each of these runs once per declaration, and the modifier prefix runs for every member of every
/// type, so it is the most frequently executed composition in the renderer.
/// </remarks>
[ShortRunJob]
[EventPipeProfiler(EventPipeProfile.GcVerbose)]
public class DeclarationPartBenchmarks
{
    /// <summary>A decimal constant, the kind a currency type declares.</summary>
    private const decimal SampleAmount = 12.5M;

    /// <summary>A type carrying several modifiers.</summary>
    private INamedTypeSymbol _abstractClass = null!;

    /// <summary>A record, whose keyword suppresses the class keyword.</summary>
    private INamedTypeSymbol _record = null!;

    /// <summary>A readonly struct.</summary>
    private INamedTypeSymbol _readonlyStruct = null!;

    /// <summary>A required property.</summary>
    private IPropertySymbol _required = null!;

    /// <summary>A virtual method.</summary>
    private IMethodSymbol _virtual = null!;

    /// <summary>An explicit interface implementation, which carries no modifiers at all.</summary>
    private IMethodSymbol _explicitImpl = null!;

    /// <summary>A constant field.</summary>
    private IFieldSymbol _constant = null!;

    /// <summary>Type parameters carrying every constraint kind.</summary>
    private ImmutableArray<ITypeParameterSymbol> _constrained;

    /// <summary>One constrained type parameter.</summary>
    private ITypeParameterSymbol _typeParameter = null!;

    /// <summary>Resolves the symbols each benchmark composes from.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var compilation = BenchmarkWorkload.Broad();
        _abstractClass = BenchmarkWorkload.Type(compilation, "Sample.Currency");
        _record = BenchmarkWorkload.Type(compilation, "Sample.Money");
        _readonlyStruct = BenchmarkWorkload.Type(compilation, "Sample.Ratio");
        _required = (IPropertySymbol)BenchmarkWorkload.Member(_abstractClass, "Code");
        _virtual = (IMethodSymbol)BenchmarkWorkload.Member(_abstractClass, "CompareTo");
        _constant = (IFieldSymbol)BenchmarkWorkload.Member(_abstractClass, "Limit");

        var reshape = (IMethodSymbol)BenchmarkWorkload.Member(_abstractClass, "Reshape");
        _constrained = reshape.TypeParameters;
        _typeParameter = _constrained[0];

        var register = BenchmarkWorkload.Type(compilation, "Sample.Register");
        foreach (var member in register.GetMembers())
        {
            if (member is IMethodSymbol { ExplicitInterfaceImplementations.IsEmpty: false } impl)
            {
                _explicitImpl = impl;
                break;
            }
        }
    }

    /// <summary>Composes the modifier prefix of an ordinary member.</summary>
    /// <returns>The prefix.</returns>
    [Benchmark]
    public string MemberModifiers() => Compose(builder => ApiModifiers.AppendMember(builder, _virtual));

    /// <summary>Composes the prefix of an explicit interface implementation, the early-out path.</summary>
    /// <returns>The prefix.</returns>
    [Benchmark]
    public string ExplicitImplementationModifiers() =>
        Compose(builder => ApiModifiers.AppendMember(builder, _explicitImpl));

    /// <summary>Composes an abstract class's modifier prefix and type keyword.</summary>
    /// <returns>The prefix.</returns>
    [Benchmark]
    public string ClassModifiers() => Compose(builder => ApiModifiers.AppendType(builder, _abstractClass));

    /// <summary>Composes a record's prefix, where the class keyword is deliberately dropped.</summary>
    /// <returns>The prefix.</returns>
    [Benchmark]
    public string RecordModifiers() => Compose(builder => ApiModifiers.AppendType(builder, _record));

    /// <summary>Composes a readonly struct's prefix.</summary>
    /// <returns>The prefix.</returns>
    [Benchmark]
    public string StructModifiers() => Compose(builder => ApiModifiers.AppendType(builder, _readonlyStruct));

    /// <summary>Writes the const and static keywords.</summary>
    /// <returns>The prefix.</returns>
    [Benchmark]
    public string StorageModifiers() => Compose(builder => ApiModifiers.AppendStorageModifiers(builder, _constant));

    /// <summary>Writes the abstract, virtual, sealed and override keywords.</summary>
    /// <returns>The prefix.</returns>
    [Benchmark]
    public string InheritanceModifiers() =>
        Compose(builder => ApiModifiers.AppendInheritanceModifiers(builder, _virtual, inInterface: false));

    /// <summary>Writes the readonly and required keywords.</summary>
    /// <returns>The prefix.</returns>
    [Benchmark]
    public string StateModifiers() => Compose(builder => ApiModifiers.AppendStateModifiers(builder, _required));

    /// <summary>Writes the modifiers that only apply to a class.</summary>
    /// <returns>The prefix.</returns>
    [Benchmark]
    public string ClassOnlyModifiers() =>
        Compose(builder => ApiModifiers.AppendClassModifiers(builder, _abstractClass));

    /// <summary>Gets the keyword introducing a type declaration.</summary>
    /// <returns>The keyword.</returns>
    [Benchmark]
    public string TypeKeyword() => ApiModifiers.TypeKeyword(_record);

    /// <summary>Determines whether a member explicitly implements an interface member.</summary>
    /// <returns>The decision.</returns>
    [Benchmark]
    public bool IsExplicitInterfaceImplementation() => ApiModifiers.IsExplicitInterfaceImplementation(_explicitImpl);

    /// <summary>Writes every constraint clause of a generic method.</summary>
    /// <returns>The clauses.</returns>
    [Benchmark]
    public string ConstraintClauses() => Compose(builder => ApiConstraints.Append(builder, _constrained));

    /// <summary>Writes one type parameter's clause.</summary>
    /// <returns>The clause.</returns>
    [Benchmark]
    public string ConstraintClause() => Compose(builder => ApiConstraints.AppendClause(builder, _typeParameter));

    /// <summary>Collects one type parameter's constraints in the order C# requires.</summary>
    /// <returns>The number of parts, so the work cannot be optimized away.</returns>
    [Benchmark]
    public int ConstraintParts() => ApiConstraints.Parts(_typeParameter).Count;

    /// <summary>Escapes an identifier that collides with a keyword.</summary>
    /// <returns>The escaped identifier.</returns>
    [Benchmark]
    public string EscapedIdentifier() => ApiLiterals.Identifier("class");

    /// <summary>Passes an ordinary identifier through, the overwhelmingly common case.</summary>
    /// <returns>The identifier.</returns>
    [Benchmark]
    public string PlainIdentifier() => ApiLiterals.Identifier("CompareTo");

    /// <summary>Maps an operator's metadata name back to its token.</summary>
    /// <returns>The token.</returns>
    [Benchmark]
    public string OperatorToken() => ApiLiterals.OperatorToken("op_CheckedAddition");

    /// <summary>Determines whether an operator name is that of its checked form.</summary>
    /// <returns>The decision.</returns>
    [Benchmark]
    public bool IsCheckedOperator() => ApiLiterals.IsCheckedOperator("op_CheckedAddition");

    /// <summary>Formats a string constant, which has to be escaped.</summary>
    /// <returns>The literal.</returns>
    [Benchmark]
    public string FormatStringConstant() => ApiLiterals.FormatConstant("with \"quotes\"");

    /// <summary>Formats a numeric constant.</summary>
    /// <returns>The literal.</returns>
    [Benchmark]
    public string FormatNumericConstant() => ApiLiterals.FormatConstant(SampleAmount);

    /// <summary>Runs one composition through a fresh builder.</summary>
    /// <param name="append">The composition to measure.</param>
    /// <returns>The composed text.</returns>
    private static string Compose(Action<PooledStringBuilder> append)
    {
        var builder = new PooledStringBuilder();
        append(builder);
        return builder.ToString();
    }
}
