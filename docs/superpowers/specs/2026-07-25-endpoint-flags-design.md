# Endpoint flags for `[Model]`

**Date:** 2026-07-25
**Status:** approved, not implemented
**Scope:** `[Model]` only. `[Aggregate]` keeps its current all-or-nothing surface.

## Problem

`EndpointsGenerator` emits five flat endpoints — List, Create, Read, Update,
Delete — plus one nested group per parent relationship, for every `[Model]`,
unconditionally. A consumer who wants a read-only resource, or who wants to
hand-write one endpoint, has no way to say so short of abandoning the
generator. ROADMAP Phase 2 names this as the first customization item:
"an all-or-nothing generator gets abandoned at the first special case."

Worse, the current behaviour publishes a writable HTTP surface for a class the
moment `[Model]` is applied. Marking a class as persistable and publishing five
public routes over it are different decisions, and today they are the same
keystroke.

## Solution

`[Model]` accepts a `[Flags]` enum naming every endpoint feature to generate.
Exposure is **opt-in**: the default is `Endpoints.None`.

```csharp
[Model("Post", "Posts", Endpoints = Endpoints.ReadOnly)]
public partial class PostModel { ... }
```

### The governing rule

> **`None` is persistence only. Any non-zero flag generates the complete
> building blocks and exposes the selected routes.**

Two tiers, not a per-flag sliding scale:

- **`Endpoints.None`** — the model is a persistence concern and nothing else.
  It gets a `DbSet`, its EF configuration (including owned-type mapping), and
  its generated `[Key] public int Id`. No repository, no DTOs, no endpoint
  class, no registration in `AddEZRestAPI`/`MapEZRestAPI`. A `[Model]` that
  names no endpoints is asking to be stored, not served, and generating an
  unreferenced repository and five DTOs for it is dead code in the consumer's
  assembly.

- **Any non-zero combination** — the repository and DTO surface is generated
  in full, regardless of which verbs are selected. `Endpoints.List` alone still
  yields `CreateAsync`, `UpdateAsync`, `DeleteAsync` and their request DTOs.
  This is deliberate: the reason to withhold `POST /posts` is almost always
  that you are hand-writing that endpoint, and the replacement needs
  `CreateAsync` and `CreatePostRequest` to build on. Pruning per-verb would
  delete exactly the escape hatch that makes partial flags worth having.

So the building blocks follow the model's *purpose* (served or not), while the
routes follow the flags precisely. `Endpoints.None` subsumes ROADMAP's
separately sketched `GenerateEndpoints = false`, so there is one switch rather
than two overlapping ones.

### Why opt-in

Defaulting to `All` makes the safe configuration the one you have to remember
to write. Adding a property to a `[Model]` would silently widen a public API,
and adding `[Model]` to a class — the natural thing to do to get persistence —
would publish `DELETE /posts/{id}` to the internet as a side effect. Opt-in
inverts that: the generator exposes exactly what the author named, and nothing
arrives by omission.

The cost is a breaking change with a wider blast radius than routes alone.
Every existing `[Model]` loses its endpoints *and* its repository and DTOs
until annotated. Hand-written code holding a `PostRepository` stops compiling
rather than silently 404-ing, which is the better of the two failure modes.
`EZR014` (below) surfaces it in the editor.

## Public API

`AttributesGenerator` emits a new post-initialization source
`EZRestAPIEndpoints.g.cs`:

```csharp
namespace EZRestAPI;

[Flags]
public enum Endpoints
{
    None   = 0,

    List   = 1,
    Create = 2,
    Read   = 4,
    Update = 8,
    Delete = 16,
    Nested = 32,

    Crud     = List | Create | Read | Update | Delete,
    ReadOnly = List | Read | Nested,
    All      = Crud | Nested,
}
```

`ModelAttribute` gains one member:

```csharp
public Endpoints Endpoints { get; set; }
```

The property is settable rather than get-only because C# attribute named
arguments bind to settable properties. It carries no initializer: the CLR
default for the enum is `None`, which is exactly the opt-in default, so the
absent-argument case and the explicit-`None` case are the same value by
construction rather than by agreement between two places.

Naming: the property and the enum type share the name `Endpoints`. This is
legal — the left side of a named argument is always a member name and the
right side resolves the type (C# "color-color" rule) — and it reads best at
the use site: `Endpoints = Endpoints.List | Endpoints.Read`.

### Two axes, not one

Per-verb flags answer "should this resource be writable?". `Nested` answers
"should this resource be reachable under its parent?". These are independent
questions with independent answers, and per-verb control alone cannot express
"full CRUD, flat routes only" — hence `Endpoints.Crud`, which omits `Nested`.

Nested routes live on the *child* model (`InsertNestedGroup` is driven by
`model.ParentRelationships`, so `/authors/{id}/books` is emitted into
`BookEndpoints`). The child's flags therefore govern them. A parent model's
flags never reach into routes whose resource is the child.

A consequence worth stating: a child with `Nested` under a parent with
`Endpoints.None` produces `/authors/{id}/books` while `/authors` itself does
not exist. This is correct and supported — the parent segment is a scoping
path, and the child repository's parent-existence check queries the `DbSet`,
which `None` still generates. It is not a diagnostic.

## Reading the flag inside the generator

The generator is a netstandard2.0 analyzer. It cannot reference
`EZRestAPI.Endpoints`, because that type exists only in the *user's*
compilation, which the generator is in the middle of producing. So
`ProviderExtensions` declares its own mirror:

```csharp
[Flags]
internal enum EndpointFeatures { None = 0, List = 1, ... All = Crud | Nested }
```

parsed from `attribute.NamedArguments` by locating the `Endpoints` entry and
casting `TypedConstant.Value` to `int`, then to `EndpointFeatures`. When the
argument is absent, or its value is not an `int`, the result is
`EndpointFeatures.None`.

Silent drift between the mirror and the emitted enum would misroute every
flag while still compiling, so a test reflects over `EndpointFeatures` and
asserts each name/value pair appears in the emitted `EZRestAPIEndpoints.g.cs`
text. That test is the contract between the two declarations.

`ProviderExtensions.Model` gains a trailing defaulted parameter:

```csharp
EndpointFeatures Endpoints = EndpointFeatures.None
```

Trailing and defaulted, so no existing construction site — including the
`with` expressions in `GetModelsWithRelationships` — needs to change.

## What each generator does with the flags

| Generator | `None` | Non-zero |
| --- | --- | --- |
| `ModelGenerator` (`[Key] public int Id`) | unchanged — still emitted | unchanged |
| `DbContextGenerator` (`DbSet`, owned types, FK config) | unchanged — still emitted | unchanged |
| `NestedGenerator` (`{Nested}Dto` + mappers) | unchanged — keyed off `[Nested]`, not `[Model]` | unchanged |
| `DtoGenerator` — per-model request/response DTOs | **skipped** | emitted in full |
| `DtoGenerator` — shared infrastructure (`PagedResponse<T>`, problem helpers) | unchanged — aggregates depend on it | unchanged |
| `RepositoryGenerator` | **skipped** | emitted in full |
| `EndpointsGenerator` | **skipped entirely** — no class, no method | selected routes only |
| `BootstrapGenerator` | model **excluded** from `AddScoped` and `Map…Endpoints` | included |

`BootstrapGenerator` filtering is what makes the rest safe: it currently emits
`services.AddScoped<{name}Repository>()` and `app.Map{name}Endpoints()` for
every model unconditionally, so without the filter a `None` model would emit
references to two types that no longer exist. Its provider changes from
`GetModels()` to one carrying the flags, and both loops filter on non-`None`.
Aggregates are unaffected and keep their unconditional registration.

`DtoGenerator` needs care in the opposite direction: its shared emissions are
not per-model and must survive even when every `[Model]` is `None`, because
`[Aggregate]` consumes them. Only the per-model registrations are gated.

## Emission rules for routes

Within a non-zero model, `EndpointsGenerator` guards each insertion:

| Route | Emitted when |
| --- | --- |
| `GET /posts` | `List` |
| `POST /posts` | `Create` |
| `GET /posts/{id}` | `Read` |
| `PUT /posts/{id}` | `Update` |
| `DELETE /posts/{id}` | `Delete` |
| `GET /authors/{authorId}/posts` | `Nested` and `List` |
| `POST /authors/{authorId}/posts` | `Nested` and `Create` |
| `GET /authors/{authorId}/posts/{id}` | `Nested` and `Read` |
| `PUT /authors/{authorId}/posts/{id}` | `Nested` and `Update` |
| `DELETE /authors/{authorId}/posts/{id}` | `Nested` and `Delete` |

The nested group — its `MapGroup` call and its variable — is skipped entirely
when `Nested` is unset or when every verb is unset, so no empty group is ever
emitted.

Blank-line emission between endpoints becomes conditional on something having
been written, so a partially-flagged model does not produce a file with
ragged runs of empty lines.

## Diagnostics

**`EZR013` (Warning) — Create endpoint has an unreachable Location header.**

> Model '{0}' generates a Create endpoint whose Location header points at
> '/{1}/{{id}}', but Endpoints.Read is not set.

Fires when `Create` is set and `Read` is not. `InsertCreateEndpoint` emits
`TypedResults.Created($"/{plural}/{id}", response)`; without the item route
that `Location` resolves to a 404, which violates the 201 contract in
`docs/superpowers/research/2026-07-21-rest-best-practices.md`. Every other
flag combination merely yields a smaller API; this is the one that yields a
wrong response, so it is the only combination that warrants a diagnostic.

**`EZR014` (Info) — Model generates no API surface.**

> Model '{0}' is registered in the DbContext only; set Endpoints on [Model]
> to generate a repository, DTOs and endpoints.

Fires when the resolved flags are `None`. With opt-in defaults, "I applied
`[Model]` and nothing appeared" is the predictable first-run confusion, and
this answers it in the editor at the point of the mistake rather than at
runtime. The message names the DbContext registration explicitly so the
persistence-only outcome reads as intended behaviour rather than a failure.

Info rather than Warning because a deliberate persistence-only model is a
legitimate, supported configuration now spelled by simply omitting the
property. Warning severity would put build noise on a correct program, and
teams building with `TreatWarningsAsErrors` would be unable to express it at
all.

Both diagnostics are reported at the `[Model]` attribute location via the
existing `ModelDiagnostics` pipeline, and registered in
`AnalyzerReleases.Unshipped.md`.

## Migration

The default flip is breaking, and the plan absorbs the churn in one pass:

- **`Example`** — all 13 models get an explicit `Endpoints`. Most take
  `Endpoints.All` to keep the feature tour's coverage intact; the tour gains
  value by having at least one model on `ReadOnly`, one on `Crud`, and one
  left at `None` so the showcase demonstrates the flags — including the
  persistence-only tier — rather than just surviving them.
- **`EZRestAPI.Tests`** — every scenario whose assertions require a route,
  repository or DTO to exist gains `Endpoints = Endpoints.All` in its inline
  source. This is a find-and-fix across the suite driven by test failures, not
  a judgement call per test. Note this reaches beyond the endpoint tests:
  `RepositoryTests`-style and DTO-shape scenarios are affected too, because
  those generators are now gated.
- **`Example.Tests`** — no changes expected beyond those following from the
  `Example` models above, since it exercises HTTP against them.

The 104 existing fast tests passing after this pass is the evidence that the
flip changed only defaults and not behaviour.

## Testing

New `EZRestAPI.Tests/EndpointFlagsTests.cs`:

- **Default is closed.** A `[Model]` with no `Endpoints` argument generates a
  `DbSet` entry and an `Id` partial and nothing else — no repository, no DTOs,
  no endpoint class. This is the security-relevant assertion in the suite.
- **Explicit `None`** — identical output to the omitted-argument case.
- **`None` is excluded from bootstrap** — the generated `AddEZRestAPI` and
  `MapEZRestAPI` contain no reference to the model, while a sibling non-`None`
  model in the same compilation still appears in both.
- **`Endpoints.All` reproduces today's surface** byte-for-byte, so the flip is
  provably a default change and not a behaviour change.
- **Each verb in isolation** — `Endpoints.List` alone emits `MapGet("/")` and
  nothing else, and so on for the other four.
- **Building blocks are complete under partial flags** — `Endpoints.List`
  still generates `CreateAsync`/`UpdateAsync`/`DeleteAsync` and
  `CreatePostRequest`/`UpdatePostRequest`. This is the rule most likely to be
  "simplified" away by a later change, so it is asserted directly.
- **`ReadOnly`** — flat and nested GETs only; no `MapPost`/`MapPut`/`MapDelete`.
- **`Crud`** — all five flat verbs, no nested group, on a model that has a
  parent relationship.
- **`Nested` with a subset of verbs** — e.g. `List | Nested` on a child emits
  both `GET /posts` and `GET /authors/{authorId}/posts` and nothing else.
- **`Nested` child under a `None` parent** — the child's nested routes still
  generate and the parent's `DbSet` is still present, per the consequence noted
  above.
- **Shared DTO infrastructure survives an all-`None` compilation** —
  `PagedResponse<T>` and the problem helpers are still emitted, so aggregates
  in the same assembly keep compiling.
- **Mirror-drift test** — reflection over `EndpointFeatures` versus the
  emitted enum text.
- **`EZR013`** — fires on `Create` without `Read`; does not fire on
  `Endpoints.All`, `ReadOnly`, or `None`.
- **`EZR014`** — fires on the omitted argument and on explicit `None`; does
  not fire on any non-zero combination.

`Example.Tests` asserts over HTTP that a route disabled by flags returns 404
while an enabled route on the same model works.
`dotnet build Example/Example.csproj` remains the gate that generated code
actually compiles — and it is the check that catches a missed
`BootstrapGenerator` filter, since that failure mode is a dangling type
reference rather than a wrong string.

## Documentation

README gains an `Endpoints` section with the enum, the presets, the two-tier
rule, and the persistence-only meaning of `None`. Because exposure is opt-in,
every `[Model]` example in the README — including the quick-start — must carry
an explicit `Endpoints` argument, or the documented first run produces no API.

ROADMAP Phase 2's "Attribute options" item is updated: the
`ReadOnly`/`GenerateEndpoints` sketch is replaced by the delivered `Endpoints`
flag, leaving `Route`/`KeyType` still open.

## Out of scope

- `[Aggregate]` endpoint flags.
- `Route` renaming, `KeyType`, authorization policies — separate Phase 2 items.
- Per-verb gating of repository methods or DTOs — excluded by the two-tier
  rule above.
