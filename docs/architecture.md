# Architecture

EZRestAPI is nine Roslyn incremental source generators sharing one set of
providers. Each generator owns one kind of output file.

```
user's classes
      |
      v
ProviderExtensions  -- reads attributes, builds cached records
      |
      +-- GetModels / GetModelsWithRelationships   -> Model
      +-- GetNestedModels                          -> NestedModel
      +-- Get*WithDiagnostics                      -> *Diagnostics (carry locations)
      |
      v
nine generators, each emitting its own .g.cs files
```

## What each generator emits

| Generator | Output |
| --- | --- |
| `AttributesGenerator` | The public attributes and the `Endpoints` enum: `EZRestAPIModel`, `EZRestAPINested`, `EZRestAPIScalar`, `EZRestAPIEndpoints` |
| `ModelGenerator` | `{Model}.g.cs` — the `[Key] public int Id` on the user's partial class |
| `DbContextGenerator` | `CustomDbContext.g.cs` — `DbSet`s, owned-type mapping, foreign keys |
| `NestedGenerator` | `{Nested}Dto.g.cs` — one DTO and mappers per `[Nested]` type |
| `DtoGenerator` | Per model: request and response DTOs. Once per assembly: `PagedResponse`, `WriteResult`, `EZRestAPIValidation`, `EZRestAPIProblems` |
| `RepositoryGenerator` | `{Model}Repository.g.cs` |
| `EndpointsGenerator` | `{Model}Endpoints.g.cs` — the route mappings |
| `BootstrapGenerator` | `EZRestAPIExtensions.g.cs` — `AddEZRestAPI()` and `MapEZRestAPI()` |
| `DiagnosticsGenerator` | No source. Reports `EZR001`–`EZR014` |

Attributes are emitted through post-initialization output, so they exist before
any user code is analysed.

## Rules the generator code follows

- **The generator targets `netstandard2.0`.** No `System.Text.Json`, no file
  I/O, no `Enum.Parse<T>()`. Tests target `net10.0` and have no such limit.
- **Source locations never enter a cached record.** A `Location` is not
  equatable in a way the incremental cache can use, so keeping one in a `Model`
  would defeat caching. Locations live only in the `*Diagnostics` records that
  `DiagnosticsGenerator` consumes.
- **Collections in cached records use `EquatableArray<T>`** (`EZRestAPI/Utils/`),
  because an array compares by reference and would break caching.
- **Generated files carry their own `using` directives.** Never rely on the
  consuming project having `ImplicitUsings` on.
- **The generator cannot reference types it generates.** `EZRestAPI.Endpoints`
  exists only in the user's compilation, so `ProviderExtensions` keeps an
  internal mirror enum and a test guards the two against drift. See
  [specs/endpoints.md](specs/endpoints.md).
- **Never call `flags.HasFlag(...)` in generator code** — it boxes, once per
  model per generator. Use the internal `Has` helper.
- **Emit with `SourceWriter.Create()`** (an `IndentedTextWriter`) and
  `ctx.AddSource("<Name>.g.cs", SourceText.From(text, Encoding.UTF8))`.
- **Every new diagnostic id goes in `AnalyzerReleases.Unshipped.md`.**

## Style

File-scoped namespaces, `using` directives after the namespace declaration,
four-space indent. Match the surrounding file.

## Tests

Three layers, cheapest first.

| Project | What it proves | Needs Docker |
| --- | --- | --- |
| `EZRestAPI.Tests` | The generator emits the right text and diagnostics. Runs the generators in memory through `GeneratorHarness.Run(source)`, about a second for the whole suite | no |
| `Example` build | The generated code actually compiles. This is the gate that catches a dangling type reference, which a string assertion cannot | no |
| `Example.Tests`, `OpenApiDocumentTests` | The OpenAPI document describes the routes correctly. Boots the app with a dummy connection string and calls no endpoint | no |
| `Example.Tests`, `[Collection("MsSql")]` | Real HTTP against a real SQL Server, started by Testcontainers | **yes** |

## Building

```bash
export NUGET_PACKAGES="$PWD/.nuget-cache"
FLAGS="-p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q"

dotnet test  EZRestAPI.Tests $FLAGS                        # fast, ~1s
dotnet build Example/Example.csproj $FLAGS                 # the real codegen gate
dotnet build Example.Tests $FLAGS -m:1
dotnet test  Example.Tests --filter OpenApiDocumentTests -m:1 $FLAGS
dotnet test  Example.Tests $FLAGS -m:1                     # needs Docker
```

`Example.Tests` needs `-m:1` — multi-node builds race on `GetTargetFrameworks`.
The two `-p:` flags are only needed in a sandbox without source-control access.

## The Example project

`Example/` is a runnable app that uses every feature, one model per file under
`Example/Models/`. It is both the living documentation and the compile gate, so
a new feature lands with a model there. New models need no `Program.cs` change —
the generated bootstrap picks them up.

Current coverage:

| Model | Shows |
| --- | --- |
| `SimpleDataModel` | The smallest model |
| `AuthorModel` | Every supported property type, and the `Guid OrderId` false-positive guard |
| `BookModel` | A foreign key, so nested routes under `/authors` |
| `ChapterModel` | A three-level chain, Author → Book → Chapter |
| `ReviewModel` | Two foreign keys on one child |
| `PostModel` → `CommentModel` → `ReactionModel` | A two-level owned collection, the inner one an `IReadOnlyList<T>` |
| `ProfileModel` | A single owned reference (`OwnsOne`) |
| `RegistrationModel` | Every validation attribute kind |
| `SensorReadingModel` | `[Scalar]` opting an id-shaped field out |
| `AuditLogModel` | `Endpoints.None` — storage with no API |
| `AuditNoteModel` | `Endpoints.Crud` — flat routes only, under a `None` parent |
| `ExchangeRateModel` | `Endpoints.ReadOnly` |
