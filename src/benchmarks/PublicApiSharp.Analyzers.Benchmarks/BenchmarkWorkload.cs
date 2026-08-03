// Copyright (c) 2026 Glenn Watson and Contributors. All rights reserved.
// Glenn Watson and Contributors licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PublicApiSharp.Analyzers.Benchmarks;

/// <summary>Builds the compilations the benchmarks measure against.</summary>
/// <remarks>
/// <para>
/// Two shapes are needed. A <em>scaled</em> compilation of N similar types answers how the
/// compilation-wide passes grow with the size of a library. A <em>broad</em> one declares every
/// construct the renderer knows about — operators including their checked forms, conversions,
/// indexers, events, constraints, extension blocks, records, nested types — so the per-declaration
/// benchmarks measure real symbols rather than the one easy case.
/// </para>
/// <para>
/// Compiling happens in setup because it is the input to the work, not the work. What each benchmark
/// times is the package's own code, given symbols Roslyn has already produced.
/// </para>
/// </remarks>
internal static class BenchmarkWorkload
{
    /// <summary>Source declaring one of everything the renderer can be asked to write.</summary>
    private const string BroadSource = """
        using System;

        [assembly: System.Reflection.AssemblyMetadata("Benchmark", "true")]
        [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Sample.Tests")]
        [assembly: CLSCompliant(false)]

        namespace Sample;

        public interface IShape
        {
            int Sides { get; }

            void Draw();
        }

        public delegate TResult Transform<in TInput, out TResult>(TInput input) where TInput : notnull;

        public enum Severity : byte
        {
            Low = 1,
            High = 2,
        }

        [Obsolete("Use Money instead.", error: false)]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "Benchmark.")]
        public abstract class Currency : IShape, IComparable<Currency>, IFormattable
        {
            public const decimal Limit = 12.5M;

            public const string Marker = "with \"quotes\"";

            public static readonly Currency? None;

            protected int _rate;

            public event EventHandler? Changed;

            public event EventHandler Custom { add { } remove { } }

            public abstract int Sides { get; }

            public required string Code { get; init; }

            public virtual double Rate { get; protected set; }

            public int this[int index] => index;

            public int this[string key] => key.Length;

            protected Currency() { }

            public abstract void Draw();

            public virtual int CompareTo(Currency? other) => 0;

            public string ToString(string? format, IFormatProvider? provider) => "";

            public static Currency? operator +(Currency a, Currency b) => a;

            public static Currency? operator checked +(Currency a, Currency b) => a;

            public static explicit operator int(Currency value) => 0;

            public static explicit operator checked int(Currency value) => 0;

            public static implicit operator string(Currency value) => "";

            public TResult Reshape<TSource, TResult>(TSource source, TResult fallback)
                where TSource : struct, IComparable<TSource>
                where TResult : class, new() => fallback;

            public void Pass(ref int byRef, out int byOut, in int byIn, params int[] rest)
            {
                byOut = byRef + byIn + rest.Length;
            }

            public sealed class Nested
            {
                public int Depth { get; set; }
            }
        }

        public sealed record Money(decimal Amount, string Code) : IComparable<Money>
        {
            public int CompareTo(Money? other) => 0;
        }

        public readonly struct Ratio : IEquatable<Ratio>
        {
            public Ratio(double value) => Value = value;

            public double Value { get; }

            public bool Equals(Ratio other) => Value == other.Value;

            public override bool Equals(object? obj) => obj is Ratio other && Equals(other);

            public override int GetHashCode() => Value.GetHashCode();
        }

        public sealed class Register : IShape
        {
            int IShape.Sides => 4;

            void IShape.Draw() { }
        }

        public static class Helpers
        {
            extension<TShape>(TShape shape)
                where TShape : IShape
            {
                public bool IsPolygon => shape.Sides > 2;

                public TShape Twice() => shape;
            }

            extension<TShape>(TShape shape)
            {
                public string Describe() => "";
            }

            extension(string text)
            {
                public bool IsLong => text.Length > 10;
            }
        }
        """;

    /// <summary>The references every benchmark compilation gets: whatever this host runs against.</summary>
    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    /// <summary>Compiles a library of similar public types, for measuring how a pass scales.</summary>
    /// <param name="typeCount">How many types to declare.</param>
    /// <returns>The compilation.</returns>
    internal static CSharpCompilation Scaled(int typeCount)
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

        return Compile(source.ToString());
    }

    /// <summary>Compiles one assembly declaring every construct the renderer has a path for.</summary>
    /// <returns>The compilation.</returns>
    internal static CSharpCompilation Broad() => Compile(BroadSource);

    /// <summary>Compiles source into a benchmark assembly.</summary>
    /// <param name="source">The C# source.</param>
    /// <returns>The compilation.</returns>
    /// <exception cref="InvalidOperationException">The source did not compile.</exception>
    internal static CSharpCompilation Compile(string source)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        CSharpCompilationOptions compilationOptions =
            new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);

        var compilation = CSharpCompilation.Create(
            "BenchmarkAssembly",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            References,
            compilationOptions);

        foreach (var diagnostic in compilation.GetDiagnostics())
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                throw new InvalidOperationException($"Benchmark source did not compile: {diagnostic}");
            }
        }

        return compilation;
    }

    /// <summary>Gets a named type from a compilation, failing loudly when the source drifted.</summary>
    /// <param name="compilation">The compilation.</param>
    /// <param name="metadataName">The type's metadata name.</param>
    /// <returns>The type.</returns>
    /// <exception cref="InvalidOperationException">The type is not in the compilation.</exception>
    internal static INamedTypeSymbol Type(Compilation compilation, string metadataName) =>
        compilation.GetTypeByMetadataName(metadataName)
            ?? throw new InvalidOperationException($"Benchmark source has no type '{metadataName}'.");

    /// <summary>Gets the first member of a type with the given name.</summary>
    /// <param name="type">The type.</param>
    /// <param name="name">The member name.</param>
    /// <returns>The member.</returns>
    /// <exception cref="InvalidOperationException">The member is not on the type.</exception>
    internal static ISymbol Member(INamedTypeSymbol type, string name)
    {
        var members = type.GetMembers(name);
        return members.IsEmpty
            ? throw new InvalidOperationException($"Benchmark type '{type.Name}' has no member '{name}'.")
            : members[0];
    }

    /// <summary>Builds the metadata references from the assemblies loaded into this process.</summary>
    /// <returns>The references.</returns>
    internal static ImmutableArray<MetadataReference> BuildReferences()
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

    /// <summary>An in-memory <see cref="AnalyzerConfigOptions"/> holding a fixed set of entries.</summary>
    internal sealed class StubConfigOptions : AnalyzerConfigOptions
    {
        /// <summary>The configured entries.</summary>
        private readonly ImmutableDictionary<string, string> _entries;

        /// <summary>Initializes a new instance of the <see cref="StubConfigOptions"/> class.</summary>
        /// <param name="entries">The configured entries.</param>
        internal StubConfigOptions(ImmutableDictionary<string, string> entries) => _entries = entries;

        /// <inheritdoc/>
        public override bool TryGetValue(string key, out string value) => _entries.TryGetValue(key, out value!);

        /// <summary>Builds options from key/value pairs.</summary>
        /// <param name="entries">The pairs.</param>
        /// <returns>The options.</returns>
        internal static StubConfigOptions From(params (string Key, string Value)[] entries)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in entries)
            {
                builder[key] = value;
            }

            return new(builder.ToImmutable());
        }
    }
}
