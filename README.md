# PublicApiSharp.Analyzers

A Roslyn analyzer that keeps a library's public API surface in a checked-in text file, and reports an
analyzer error when the two disagree.

The baseline is ordinary C#, one file per project **per target framework**:

```csharp
namespace Refit;

public sealed class ApiResponse<T> : Refit.IApiResponse, System.IDisposable
{
    public ApiResponse(System.Net.Http.HttpResponseMessage response, T? content, Refit.RefitSettings settings) { }
    public T Content { get; }
    public Refit.ApiExceptionBase? Error { get; }
    public bool IsSuccessful { get; }
    public void Dispose() { }
}
```

That shape is the point. A reviewer reads the diff the way they read code and can tell at a glance
whether a change is additive — which a flat list of one-line-per-member entries does not give you.

## How it differs from the alternatives

|  | this package | shipped/unshipped analyzer | assembly-reflection approval |
| --- | --- | --- | --- |
| Feedback | analyzer error, on the declaration | analyzer error | test failure |
| Format | nested C#, reads like source | one flat line per member | nested C#, reads like source |
| Promotion step | none — one file, always current | move entries unshipped → shipped | none |
| Multi-targeting | one baseline per TFM, wired up automatically | manual | manual |
| New C# features | inherited from the host compiler | inherited from the host compiler | waits on the tool to add support |

There is no shipped/unshipped split. The baseline states what the assembly exposes *right now*, so an
API change is accepted by updating the file in the same commit that makes the change. The diff a
reviewer reads is the API change itself, not a later promotion.

## Getting started

```xml
<PackageReference Include="PublicApiSharp.Analyzers" Version="1.0.0" PrivateAssets="all" />
```

Create an empty baseline for each target framework:

```bash
mkdir -p src/MyProject/PublicAPI/net10.0
touch     src/MyProject/PublicAPI/net10.0/PublicAPI.txt
```

Build. Every member is reported as [PAS0001](docs/rules/PAS0001.md); apply the
**Update the public API baseline** fix once and the file is written. From then on the build fails
whenever the surface drifts from the file, and the same fix accepts the change.

From the command line:

```bash
dotnet format analyzers MyProject.csproj --diagnostics PAS0001 PAS0003 --severity info
```

On a multi-targeting project `dotnet format` writes **one baseline per run** — it applies the fix for
a single inner build at a time — so run it once per target framework, or simply repeat it until the
build is clean.

Until a baseline file exists the analyzer does nothing at all, so adding the package cannot break a
build before you have adopted it. Enable [PAS0004](docs/rules/PAS0004.md) to be told which target
frameworks are still untracked.

## Rules

| Id | Rule | Severity | Fix |
| --- | --- | --- | --- |
| [PAS0001](docs/rules/PAS0001.md) | Public API is not in the baseline | Error | Yes |
| [PAS0002](docs/rules/PAS0002.md) | Public API in the baseline no longer exists | Error | See the page |
| [PAS0003](docs/rules/PAS0003.md) | Public API differs from the baseline | Error | Yes |
| [PAS0004](docs/rules/PAS0004.md) | No public API baseline for this target framework | Warning, off | No |
| [PAS0005](docs/rules/PAS0005.md) | Public API baseline could not be read | Error | No |

Additions and changes are reported on the declaration in your source. Removals are reported on the
baseline line, because there is nothing left in the source to point at.

## Multi-targeting

A multi-targeting project genuinely has a different surface per target — conditional compilation,
framework-only types, nullability that differs by target — so one shared baseline can only ever
describe one of them. The package's MSBuild targets resolve the path per inner build and add it to
`@(AdditionalFiles)` for you:

```text
src/MyProject/
  PublicAPI/
    net472/PublicAPI.txt
    net8.0/PublicAPI.txt
    net10.0/PublicAPI.txt
  MyProject.csproj
```

| Property | Default | Purpose |
| --- | --- | --- |
| `EnablePublicApiBaseline` | `true` | Turns tracking off for a project. |
| `PublicApiBaselineDirectory` | `$(MSBuildProjectDirectory)\PublicAPI` | Where the per-TFM folders live. |
| `PublicApiBaselineFileName` | `PublicAPI.txt` | The file name inside each folder. |
| `PublicApiBaselineFile` | *(resolved)* | Set it to place one baseline explicitly. |

## Configuration

Options are read from `.editorconfig`:

| Key | Default | Purpose |
| --- | --- | --- |
| `publicapisharp.include_assembly_attributes` | `true` | Record assembly-level attributes at all. |
| `publicapisharp.excluded_attributes` | *(empty)* | Attribute patterns to leave out. |
| `publicapisharp.included_attributes` | *(empty)* | Attribute patterns to keep despite the built-in list. |
| `publicapisharp.excluded_namespace_prefixes` | *(empty)* | Namespace prefixes to leave out. |

### Stripping attributes

The attribute lists are comma-separated patterns of fully qualified names, and `*` matches any run
of characters, so a whole family or a naming convention is one entry rather than a list:

```ini
[*.cs]
publicapisharp.excluded_attributes = System.Diagnostics.CodeAnalysis.*, *.InternalUseAttribute
```

Some attributes are dropped without being asked for, because they are the build's own bookkeeping
rather than API. The version and target-framework stamps matter most: the SDK writes them into every
assembly, so keeping them would rewrite every baseline in the repository on each release — a version
bump is not an API change — and would restate the target framework the baseline's own folder already
names.

| dropped by default | why |
| --- | --- |
| `AssemblyVersion`, `AssemblyFileVersion`, `AssemblyInformationalVersion`, `AssemblyMetadata` | churn on every release |
| `TargetFramework`, `TargetPlatform` | already stated by the baseline's folder |
| `AssemblyCompany`, `AssemblyProduct`, `AssemblyTitle`, `AssemblyCopyright`, `AssemblyTrademark`, `AssemblyConfiguration`, `AssemblyDescription` | packaging metadata |
| `AssemblyKeyFile`, `AssemblyKeyName`, `AssemblyDelaySign`, `AssemblySignatureKey`, `AssemblyCulture` | signing and build configuration |
| `CompilerGenerated`, `Nullable`, `NullableContext`, `IsReadOnly`, `IsByRefLike`, `RequiredMember`, `Extension`, and the rest of the compiler's own markers | described by the declaration itself |
| `Debuggable`, `DebuggerStepThrough`, `DebuggerNonUserCode`, `GeneratedCode`, `DefaultMember` | build and tooling detail |

To keep one of them, name it in `included_attributes`:

```ini
publicapisharp.included_attributes = System.Reflection.AssemblyVersionAttribute
```

An explicit exclusion always wins over an explicit inclusion, and neither can bring back an attribute
whose own type is not visible outside the assembly.

## Keeping up with C#

The surface is rendered from Roslyn's own symbol model rather than from compiled metadata, so a
language feature the host compiler understands renders correctly without a change here — `required`,
`scoped`, params spans, static abstract interface members, ref-struct constraints, and whatever comes
next. That is the whole reason this reads the compilation instead of the built assembly.

The package ships a slot per Roslyn line and the SDK loads the highest one your compiler supports:

| Slot | Roslyn | Host |
| --- | --- | --- |
| `roslyn4.8` | 4.8 | .NET 8 SDK / VS 17.8 (C# 12) and .NET 9 |
| `roslyn4.14` | 4.14 | .NET 10 SDK / VS 17.14 (C# 14) |
| `roslyn5.3` | 5.3 | .NET 11 line (C# 15) |
| `roslyn5.6` | 5.6 | current Roslyn |

A construct can only appear in source the host compiler can parse, so a project that builds on the
4.8 floor cannot contain C# 15 syntax and renders identically on every slot. That is what keeps a
baseline stable across a mixed developer/CI SDK estate.

The output uses a file-scoped namespace when the assembly exposes a single namespace, which is the
common case; C# permits only one file-scoped namespace per file, so an assembly with two or more
falls back to the block form.

**Known limits.** C# 14 extension blocks round-trip from `roslyn5.3` onward; on `roslyn4.14` the
symbol model exposes them but the parser cannot read the syntax back, so they are left out of the
baseline rather than written in a form that could not be re-read. Roslyn 5.6 parses `union`
declarations but exposes no public API for them yet, so unions are recorded as ordinary types until
it does.

## License

MIT. See [LICENSE](LICENSE).
