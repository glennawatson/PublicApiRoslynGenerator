# CLAUDE.md

Guidance for working in this repository. One NuGet package ships from here:
`PublicApiSharp.Analyzers` (`PAS####`), which records a library's public API surface in a checked-in
C# file and reports an analyzer error when the compilation and the file disagree. The GitHub repo is
`PublicApiRoslynGenerator`.

## Build & test

```bash
# Run from src/

dotnet build PublicApiSharp.Analyzers.slnx -c Release
dotnet test  --solution PublicApiSharp.Analyzers.slnx -c Release   # runs every Roslyn slot

# TUnit / Microsoft.Testing.Platform notes
# - `dotnet test` must be run from src/ so the relative project paths resolve.
# - Runner-specific arguments come after `--`.
# - For focused local runs `dotnet run` is easier than `dotnet test`, because TUnit exposes its
#   CLI flags directly there.
# - TUnit filtering uses tree-node filters, not VSTest `--filter` syntax:
#     dotnet run --project tests/PublicApiSharp.Analyzers.Tests.Roslyn56/PublicApiSharp.Analyzers.Tests.Roslyn56.csproj -c Release -- --treenode-filter "/*/*/ApiSurfaceRenderingTests/*"

# Build a specific Roslyn slot
dotnet build PublicApiSharp.Analyzers.CodeFixes/PublicApiSharp.Analyzers.CodeFixes.csproj -c Release -p:RoslynVersion=roslyn5.6

# Pack (builds every slot, emits one nupkg with all analyzers/dotnet/<slot>/cs folders)
dotnet pack PublicApiSharp.Analyzers.Package/PublicApiSharp.Analyzers.Packages.csproj -c Release
```

**Every slot is the gate, not just the floor.** The `#if` paths differ per slot, so a green
`roslyn4.8` build proves little. Build `roslyn4.8`, `roslyn4.14`, `roslyn5.3` and `roslyn5.6` before
pushing.

The tests enforce this for you. There is one test project per slot — `tests/PublicApiSharp.Analyzers.Tests.Roslyn48`
and friends — and each is a few lines that set `$(RoslynVersion)` and import
`tests/PublicApiSharp.Analyzers.Tests.Shared/PublicApiSharp.Analyzers.Tests.props`. The test *sources*
live once, in that Shared folder, and are linked into all four. So `dotnet test --solution` runs the
whole suite against every slot, and a test whose source the floor compiler cannot even parse fails
there rather than silently passing everywhere else.

Add a test by dropping a `.cs` file in the Shared folder; it is picked up by all four projects. Gate
anything slot-specific on a `RoslynFeatures` capability rather than on `#if` in the test.

## How the thing works

Three pieces, in the order data flows through them:

1. **`Rendering/`** turns the compilation's externally visible symbols into nested C# text. Every
   signature comes from Roslyn's `SymbolDisplay`; what is composed by hand is only what Roslyn has no
   opinion on — nesting, ordering, the modifier prefix, and which members belong in the surface.
2. **`Baseline/ApiTextParser`** reads API surface text back into declarations. It runs over the
   freshly rendered surface *and* over the checked-in baseline. That symmetry is the design: the two
   sides of the comparison cannot drift apart through two different notions of what a declaration is.
3. **`PublicApiBaselineAnalyzer`** compares them and reports.

### Two invariants worth not breaking

- **Additions and changes are reported from a symbol action.** A diagnostic raised by a compilation
  action is not local to a document, and Roslyn refuses to offer a code fix for one. Moving PAS0001
  or PAS0003 to a compilation-end action silently costs the lightbulb. Removals genuinely have no
  symbol left to point at, so they stay at compilation end and get no fix (see `docs/rules/PAS0002.md`).
- **The renderer's output must parse back.** `ApiComparisonState` bails out silently if it does not,
  because that is this package's bug rather than the user's. `RenderedSurfaceParsesBackAsync` guards
  it; anything emitted that C# cannot re-read is a defect.

## Conventions (follow these)

- **No suppressions.** Never `#pragma warning disable`, `<NoWarn>`, `[SuppressMessage]`, or an
  `.editorconfig` severity drop to silence a rule — fix the cause. The repo builds its own source
  under `TreatWarningsAsErrors` with StyleSharp, PerformanceSharp, SecuritySharp, Roslynator and
  SonarAnalyzer.

- **Never touch `.editorconfig`, or add a nested one, without asking.** That includes scoping a rule
  off for a folder. An analyzer complaint is a reason to change the code.

- **Never `Console.WriteLine` in a test.** Assert with TUnit. To pin down an unknown expected value,
  assert what you expect and read the actual from the TUnit assertion diff, or explore in a throwaway
  scratchpad app outside the repo. Never commit a "dump" test.

- **Repo layout:** metadata at the root, build entry points under `src/`. Run `dotnet` from `src/`;
  projects are grouped under `src/`, `tests/` and `benchmarks/`.

- **No LINQ in `src/PublicApiSharp.Analyzers/` or `src/PublicApiSharp.Analyzers.CodeFixes/`.** The
  implicit `System.Linq` global using is removed in both projects (`<Using Remove="System.Linq" />`);
  keep that guardrail so accidental use fails at compile time. Tests may use LINQ.

- **String building goes through `PooledStringBuilder`** (`src/Shared/Analyzers/`), which grows from
  thread-local pooled `char[]` buffers. Rendering a surface builds one large document out of many
  small fragments, which is exactly what it is for. It is single-use: `ToString()` returns the buffer.

- **Version differences live in `RoslynFeatures`.** One file holds every `#if` about what the host
  Roslyn can do, so the rest of the code reads without conditionals. Slots do not gain a feature at
  the same moment on the symbol side and the syntax side — extension blocks are the worked example —
  and this package needs both halves.

- **Rendered output must be a pure function of the symbols.** Never of their order in source. Moving
  a method up a file is not an API change and must not appear in the diff. Sort everything:
  namespaces, types, members, interfaces in a base list, attributes, named attribute arguments.
  Enum members are the one exception — their declared order is kept, because the values are what
  matter and alphabetising would make the baseline read nothing like the source.

- **Configuration is `.editorconfig` only**, read through `AnalyzerConfigOptionsProvider`. The
  analyzer never does its own file I/O: it cannot be cached by the compiler that way and it
  misbehaves in the IDE, where the "file" may be an unsaved buffer.

- **Coverage target is 100%.** Prefer `internal` + `InternalsVisibleTo` over `public` so code is
  reachable from tests.

## Multi-Roslyn targeting

| Slot | Roslyn | Host |
| --- | --- | --- |
| `roslyn4.8` | 4.8.0 (floor) | .NET 8 SDK / VS 17.8 (C# 12) and .NET 9 |
| `roslyn4.14` | 4.14.0 | .NET 10 SDK / VS 17.14 (C# 14) |
| `roslyn5.3` | 5.3.0 | .NET 11 line (C# 15) |
| `roslyn5.6` | 5.6.0 | current Roslyn |

Slot wiring lives in `src/Directory.Build.props` (`RoslynVersion` → package version +
`ROSLYN_*_OR_GREATER` constants + segregated `bin`/`obj`). Keep these assemblies `netstandard2.0`
(RS1041). Funnel `ImmutableArray` creation through `ImmutableArrays.Of(...)` — the 4.8 floor cannot
bind collection expressions for `ImmutableArray` while 4.14+ requires them.

The slot matters more here than in a normal analyzer, because the *output* depends on it. A newer
slot renders newer constructs. What keeps baselines stable across a mixed SDK estate is that a
construct can only appear in source the host compiler could parse, so a project building on the floor
cannot contain newer syntax and renders identically everywhere.

**Feature status, measured rather than assumed:**

| Feature | Symbol API | Syntax | Consequence |
| --- | --- | --- | --- |
| Extension blocks | `ITypeSymbol.IsExtension`, 4.14+ | `ExtensionBlockDeclarationSyntax`, 5.3+ | Rendered from 5.3; skipped on 4.14, where the output could not be re-read |
| Unions | none as of 5.6 | `SyntaxKind.UnionDeclaration`, 5.6 | Recorded as ordinary types; needs structural probing when the API lands |

Before adding support for a new construct, check both halves against the actual assemblies rather
than assuming they arrived together.

## Diagnostic ids

`PAS00xx`, all in the `PublicApi` category. Adding a rule means: a descriptor in
`Rules/PublicApiRules.cs`, the analyzer change, tests, a `docs/rules/<ID>.md` page, and a row in
`AnalyzerReleases.Unshipped.md` (RS2000).

| Id | Rule | Severity | Reported on |
| --- | --- | --- | --- |
| `PAS0001` | Public API is not in the baseline | Error | the declaration |
| `PAS0002` | Public API in the baseline no longer exists | Error | the baseline line |
| `PAS0003` | Public API differs from the baseline | Error | the declaration |
| `PAS0004` | No baseline for this target framework | Warning, disabled | the compilation |
| `PAS0005` | Baseline could not be read | Error | the baseline line |

`PAS0004` is disabled by default on purpose: installing the package must not break a build before a
baseline has been adopted.
