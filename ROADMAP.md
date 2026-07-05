# Production readiness roadmap

Goal: take EZRestAPI from "solid prototype" to a generator that teams can adopt
for real services. Phases are ordered so that each one unlocks the next; within
a phase, items are ordered by priority. Every item lands with tests (generator
snapshot tests and/or HTTP integration tests) and README updates.

## Phase 0 — Correctness hardening

Fix everything that can produce broken generated code, and make misuse fail
with clear diagnostics instead of cryptic errors in invisible files.

- [x] **Escape C# keywords in identifiers.** Property names no longer become
      generated identifiers at all (repositories consume DTOs and access
      `request.{Property}`), and assembly names are sanitized into valid,
      keyword-escaped namespaces via `ToValidNamespace`.
- [x] **Replace the repository tuple API with generated read models.**
      `ReadAsync` returns the generated `Read{Name}Response`, and
      `CreateAsync`/`UpdateAsync` accept the request DTOs instead of one
      parameter per property.
- [x] **Handle degenerate models.** Zero-property and one-property models
      generate valid code (covered by generator tests).
- [x] **Analyzer diagnostics (`EZR001`–`EZR006`)** reported at the user's code:
      class not `partial`, duplicate singular/plural names across models,
      duplicate `[Nested]` names, a `[Model]` used as a navigation property
      (must reference by id), and `[Nested]` cycles. (A diagnostic for
      unsupported property types is deferred — needs a careful definition of
      "supported" to avoid false positives.)
- [x] **Generator snapshot tests.** `EZRestAPI.Tests` runs every generator
      in-memory via `CSharpGeneratorDriver` and asserts generated output and
      diagnostics per scenario in ~1s, no database needed. The Testcontainers
      suite stays as the end-to-end layer. Bonus catch: generated files now
      carry their own `using` directives instead of silently requiring
      `ImplicitUsings` in the consuming project.

Exit criteria: no known input produces uncompilable output; every known misuse
produces a targeted diagnostic; CI runs generator tests in seconds.

## Phase 1 — Table-stakes API features

The features every production consumer expects on day one.

- [ ] **List endpoint with pagination.** `GET /{plural}?page=&pageSize=` (caps
      enforced), stable default ordering by `Id`, response envelope with
      `items`, `totalCount`, `page`, `pageSize`. Repository gets
      `ListAsync(page, pageSize)`.
- [ ] **Sorting and filtering.** `?sortBy=` validated against a generated
      whitelist of scalar properties; equality filters per scalar property to
      start (`?title=foo`). No dynamic LINQ from raw user strings.
- [ ] **Validation.** Copy DataAnnotations (`[MaxLength]`, `[Range]`, etc.)
      from model properties onto generated DTOs and enable minimal-API
      validation (`AddValidation()`, .NET 10) so violations return 400
      ProblemDetails instead of database-level 500s.
- [ ] **Error semantics.** ProblemDetails everywhere (`AddProblemDetails()` in
      the bootstrap), malformed-body and constraint-violation cases covered by
      integration tests.
- [ ] **TypedResults + OpenAPI metadata.** Generate handlers returning
      `Results<Created<T>, ValidationProblem>` etc., so the OpenAPI document
      accurately describes 200/201/204/400/404 without manual annotations.

Exit criteria: a consumer can build a real UI against a generated API (browse,
page, search, validate) using only generated endpoints; the OpenAPI doc is
accurate enough to generate a client from.

## Phase 2 — Customization and security

An all-or-nothing generator gets abandoned at the first special case. Add
opt-outs and hooks before adding more conventions.

- [ ] **Attribute options:**
      `[Model("Post", "Posts", Route = "blog-posts", ReadOnly = true,
      GenerateEndpoints = false, KeyType = KeyType.Guid)]`.
      `ReadOnly` generates only GET endpoints; `GenerateEndpoints = false`
      keeps repository/DTOs but skips HTTP.
- [ ] **Authorization.** `[Model(..., Policy = "PostEditor")]` →
      `.RequireAuthorization("PostEditor")` on the group, with an
      `AllowAnonymousRead` switch for public-read/private-write. Default stays
      anonymous only when no policy is configured, and the README gains a
      security section.
- [ ] **Partial hooks.** Generated endpoint classes become `partial` with a
      `static partial void Configure(RouteGroupBuilder group)` so users can add
      rate limiting, output caching, or filters per model without forking.
- [ ] **Optimistic concurrency.** Opt-in `rowversion` property on models →
      ETag on GET, `If-Match` required on PUT/DELETE, `412 Precondition
      Failed` on mismatch.

Exit criteria: the generator survives contact with one real special case per
model without being ripped out.

## Phase 3 — Data layer maturity

- [ ] **Migrations story.** Document (and sample) EF migrations against the
      generated `CustomDbContext`, including a design-time factory; make it
      explicit that `EnsureCreated` is test-only.
- [ ] **Split queries.** Models with multiple owned collections generate
      `AsSplitQuery()` on reads to avoid cartesian explosion.
- [ ] **Cross-aggregate references.** `public int AuthorId { get; set; }`
      with `[References(typeof(AuthorModel))]` → FK constraint configured,
      404/409 semantics when the referenced id does not exist.
- [ ] **Provider matrix.** Integration tests against PostgreSQL (Testcontainers)
      in addition to SQL Server; remove any provider-specific assumptions.
- [ ] Optional, opt-in: soft delete (`IsDeleted` filter) and audit fields
      (`CreatedAt`/`UpdatedAt`).

Exit criteria: a schema change ships to a real database via migrations, and the
generator is not silently SQL Server-only.

## Phase 4 — Packaging and adoption

- [ ] **NuGet package.** Analyzer packaging layout (`analyzers/dotnet/cs`),
      package README, source link, deterministic build, semantic versioning.
- [ ] **Release automation.** Tag-triggered GitHub Actions publish using NuGet
      trusted publishing (OIDC, no long-lived API keys).
- [ ] **Models outside the web project.** Endpoints generation must activate
      only where ASP.NET Core types exist (or via explicit assembly-level
      opt-in), so a models class library + web host layout works.
- [ ] **Docs.** Quick-start, feature matrix, customization reference, upgrade
      notes, CHANGELOG. The Example project stays the living sample.

Exit criteria: a stranger can `dotnet add package EZRestAPI` and have a working
API in ten minutes without reading the source.

## Phase 5 — Production confidence

- [ ] Benchmark suite (generator throughput on large models; API hot paths).
- [ ] Load-test the generated endpoints once, publish findings.
- [ ] Dogfood: port one small real service and record every friction point;
      feed that list back into Phase 2 options.

## Sequencing rationale

Phase 0 first because every later feature multiplies the surface where broken
codegen can hide, and the repository-signature change is breaking — it must
land before adoption. Phase 1 is the difference between "toy" and "usable";
Phase 2 between "usable" and "survives real requirements"; Phases 3–4 between
"survives" and "adoptable by strangers". Phase 5 is evidence.

## Explicit non-goals (for now)

- GraphQL/OData/JSON:API surfaces.
- Multi-tenancy, CQRS/event sourcing, message-bus integration.
- Replacing hand-written APIs with complex domain logic — EZRestAPI targets
  the CRUD-shaped 80%, and should say so loudly.
