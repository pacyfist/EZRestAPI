# Roadmap

Goal: take EZRestAPI from a solid prototype to a generator a team can adopt for
a real service.

Phases are ordered so each one unlocks the next. Inside a phase, items are in
priority order. Every item lands with tests and a docs update.

Design docs live in [`docs/`](docs/).

## Done

- **Correctness hardening.** No known input produces uncompilable output.
  Keyword-safe identifiers, DTO-based repository signatures, degenerate models
  (zero and one property), `EZR001`–`EZR010`, and a fast in-memory generator
  test suite that runs in about a second.
- **Paginated list.** `GET /{plural}?page=&pageSize=`, clamped size, stable
  ordering, `{ items, totalCount, page, pageSize }`.
- **Validation.** DataAnnotations copied onto the generated DTOs and checked on
  every write, returning `422` with an `errors` map.
- **Error semantics.** `application/problem+json` everywhere, with `detail`,
  `instance` and a machine-readable `code`.
- **TypedResults and OpenAPI.** Union return types, stable `operationId`s, tags
  per resource, and a typed schema for every status including errors.
- **Relationships.** `{Singular}Id` foreign keys by convention, flat and nested
  routes, parent scoping, `Restrict` on delete, `[Scalar]` to opt out.
- **DDD aggregates.** `[Aggregate]` with `[Factory]` creation, `[Command]`
  endpoints, encapsulated reads, and exception-to-status mapping. No `PUT`.
- **Endpoint flags.** `[Model(..., Endpoints = ...)]`, opt-in, choosing which
  routes are generated. Presets `Crud`, `ReadOnly`, `All`. The flags control
  routes only — the `Id`, `DbSet`, DTOs and repository are always generated, so
  `Endpoints.None` gives a typed way to use a table without publishing it.
- **Example feature tour.** Fifteen models covering every feature, each backed
  by an OpenAPI assertion and, where it has runtime behaviour, an integration
  test.

## Next — API features

- [ ] **Sorting and filtering.** `?sortBy=` checked against a generated
      whitelist of scalar properties. Equality filters per scalar property to
      start (`?title=foo`). No dynamic LINQ built from raw user strings.
- [ ] **Kebab-case routes.** `AuditNotes` currently becomes `/auditnotes`.
      Fix it with the `Route` option below, so a multi-word plural reads right.

## Next — customization and security

An all-or-nothing generator gets dropped at the first special case. Endpoint
flags were the first opt-out; these are the rest.

- [ ] **Remaining `[Model]` options:** `Route = "blog-posts"`,
      `KeyType = KeyType.Guid`.
- [ ] **Authorization.** `[Model(..., Policy = "PostEditor")]` →
      `.RequireAuthorization("PostEditor")` on the route group, with an
      `AllowAnonymousRead` switch for public-read/private-write. Anonymous stays
      the default only while no policy is configured. Needs a docs section.
- [ ] **Partial hooks.** Make generated endpoint classes `partial` with a
      `static partial void Configure(RouteGroupBuilder group)`, so a user can
      add rate limiting, caching or filters per model without forking.
- [ ] **Optimistic concurrency.** An opt-in `rowversion` property → `ETag` on
      `GET`, `If-Match` required on `PUT`/`DELETE`, `412` on mismatch.
- [ ] **Endpoint flags for `[Aggregate]`.** Deliberately left out of the
      `[Model]` work.

## Next — EF-faithful relationships

Links are declared by convention today. The goal is to also declare them the way
EF Core does, with navigation properties carrying EF's own settings. Phased;
design notes are in [docs/specs/relationships.md](docs/specs/relationships.md).

- [ ] **P1 — one-to-many via navigation properties.** Cardinality inferred from
      the nav shape, a generated foreign key, `[OnDelete]`, `[ForeignKey]`,
      `[InverseProperty]`. Reverses `EZR004`. The heaviest phase.
- [ ] **P2** — one-to-one.
- [ ] **P3** — many-to-many, with link and unlink routes.
- [ ] **P4** — composite and alternate keys, shadow FKs, self-references, and
      the full delete-behavior matrix.

## Next — data layer

- [ ] **Migrations.** Document and sample EF migrations against the generated
      `CustomDbContext`, including a design-time factory. Say plainly that
      `EnsureCreated` is test-only.
- [ ] **Split queries.** `AsSplitQuery()` on reads for models with more than one
      owned collection, to avoid a cartesian blow-up.
- [ ] **Provider matrix.** Run the integration tests against PostgreSQL as well
      as SQL Server, and remove any provider-specific assumption.
- [ ] Optional, opt-in: soft delete (`IsDeleted` filter) and audit fields
      (`CreatedAt` / `UpdatedAt`).

## Next — packaging

- [ ] **NuGet package.** Analyzer layout (`analyzers/dotnet/cs`), package
      README, source link, deterministic build, semantic versioning.
- [ ] **Release automation.** Tag-triggered GitHub Actions publish using NuGet
      trusted publishing (OIDC, no long-lived keys).
- [ ] **Models outside the web project.** Endpoint generation must only switch
      on where ASP.NET Core types exist, or behind an assembly-level opt-in, so
      a class library plus a web host works.
- [ ] **Docs.** Upgrade notes and a CHANGELOG. The Example project stays the
      living sample.

## Later — confidence

- [ ] Benchmarks: generator throughput on large models, and the API hot paths.
- [ ] Load-test the generated endpoints once and publish what it showed.
- [ ] Port one small real service, write down every friction point, and feed the
      list back into the customization phase.

## Why this order

Correctness came first because every later feature multiplies the surface where
broken codegen can hide, and the repository signature change was breaking.
API features are the difference between a toy and something usable;
customization between usable and surviving real requirements; the data layer and
packaging between surviving and being adoptable by a stranger. Benchmarks are
evidence, and evidence comes last.

## Not doing, for now

- GraphQL, OData, or JSON:API surfaces.
- Multi-tenancy, CQRS, event sourcing, message-bus integration.
- Replacing hand-written APIs that hold real domain logic. EZRestAPI targets the
  CRUD-shaped 80%, and should say so loudly.
