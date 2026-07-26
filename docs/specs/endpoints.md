# Endpoints

`[Model]` takes an `Endpoints` flag saying which routes to generate. Exposure is
**opt-in**: leaving it out means `None`.

```csharp
[Flags]
public enum Endpoints
{
    None   = 0,
    List   = 1, Create = 2, Read = 4, Update = 8, Delete = 16,
    Nested = 32,

    Crud     = List | Create | Read | Update | Delete,   // flat routes only
    ReadOnly = List | Read | Nested,
    All      = Crud | Nested,
}
```

## Why opt-in

Marking a class as storable and publishing five public routes over it are two
different decisions. If the default were `All`, adding `[Model]` to get
persistence would also publish `DELETE /posts/{id}`, and adding a property would
silently widen a public API. Opt-in means the generator exposes exactly what the
author named, and nothing arrives by omission.

## The governing rule

> **`Endpoints` controls routes and nothing else.** The building blocks — the
> `Id`, the `DbSet`, the DTOs and the repository — are always generated.

So `Endpoints.None` still gives you a full, typed way to read and write the
table. It just gives you no HTTP.

That is the point. `None` means "I do not want routes", not "I do not want data
access". You still want to use the table from a job or from an endpoint you
write yourself.

The same reasoning covers partial flags. `Endpoints.List` alone still yields
`CreateAsync`, `UpdateAsync`, `DeleteAsync` and their request DTOs, because the
usual reason to withhold `POST /posts` is that you are hand-writing it — and
your replacement needs those to build on.

## Which generator does what

| Generator | `None` | Non-zero |
| --- | --- | --- |
| `ModelGenerator` (the `Id`) | emitted | emitted |
| `DbContextGenerator` (`DbSet`, owned types, FKs) | emitted | emitted |
| `NestedGenerator` (`{Nested}Dto`) | emitted | emitted |
| `DtoGenerator` | emitted | emitted |
| `RepositoryGenerator` | emitted | emitted |
| `BootstrapGenerator` — `AddEZRestAPI` | repository registered | repository registered |
| `EndpointsGenerator` | **skipped** — no class at all | selected routes |
| `BootstrapGenerator` — `MapEZRestAPI` | **model excluded** | model included |

Only the last two rows read the flags. They move together: `MapEZRestAPI` must
skip a `None` model, or it would call a `Map{Name}Endpoints()` that was never
generated.

## Routes

For plural name `Posts`, the path is the plural lowercased.

| Route | Emitted when |
| --- | --- |
| `GET /posts` | `List` |
| `POST /posts` | `Create` |
| `GET /posts/{id}` | `Read` |
| `PUT /posts/{id}` | `Update` |
| `DELETE /posts/{id}` | `Delete` |
| `GET /authors/{authorId}/posts` | `Nested` **and** `List` |
| `POST /authors/{authorId}/posts` | `Nested` **and** `Create` |
| `GET /authors/{authorId}/posts/{id}` | `Nested` **and** `Read` |
| `PUT /authors/{authorId}/posts/{id}` | `Nested` **and** `Update` |
| `DELETE /authors/{authorId}/posts/{id}` | `Nested` **and** `Delete` |

### `Nested` is ANDed, not added

`Nested` has no route of its own. It switches on the nested *form* of whichever
verbs are also set. So `Endpoints.Nested` alone generates no route at all
(`EZR014`), and `Endpoints.Crud` deliberately omits `Nested` to mean "full
CRUD, flat routes only". The whole nested group — its `MapGroup` line included —
is skipped when no nested route survives, so no empty group is ever emitted.

### Nested routes belong to the child

`/authors/{id}/books` is emitted into `BookEndpoints`, driven by the child's
relationships, so the **child's** flags govern it. A parent's flags never reach
into a route whose resource is the child.

A consequence: a child with `Nested` under a parent with `None` produces
`/authors/{id}/books` while `/authors` does not exist. This is supported and
correct — the parent segment is only a scope, and the child's parent-exists
check queries the `DbSet`, which `None` still generates. It is not a diagnostic.

## Paging

`GET /posts` and `GET /authors/{id}/posts` return one page at a time.

- `?page` — 1-based, default `1`.
- `?pageSize` — default `20`, capped at `100`.
- A `pageSize` over `100` is **clamped**, not rejected.
- `page < 1` or `pageSize < 1` → `422`.
- Ordering is by `Id` ascending, so paging is stable.

The envelope is the same for flat and nested lists:

```json
{ "items": [ ... ], "totalCount": 57, "page": 1, "pageSize": 20 }
```

It is generated once per assembly as `PagedResponse<T>`.

## Diagnostics

`EZR012` (Warning) fires on `Create` without `Read`. `EZR013` (Info) says a model
publishes no routes. `EZR014` (Info) fires when a non-zero value selects no verb.
See [diagnostics.md](diagnostics.md).

## Reading the flag inside the generator

The generator is a `netstandard2.0` analyzer, so it cannot reference
`EZRestAPI.Endpoints` — that type exists only in the compilation it is
producing. `ProviderExtensions` declares a mirror, `internal enum
EndpointFeatures`, and parses the named argument out of `attribute.NamedArguments`
as an `int`. An absent or non-`int` argument reads as `None`.

Drift between the mirror and the emitted enum would misroute every flag while
still compiling, so a test reflects over `EndpointFeatures` and asserts every
name and value appears in the emitted `EZRestAPIEndpoints.g.cs`. That test is
the contract between the two declarations.
