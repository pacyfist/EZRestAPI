# Relationship-aware nested endpoints — design

**Date:** 2026-07-21
**Status:** Approved (design), pending implementation plan
**Scope:** One increment. Convention-based associations between top-level
`[EZRestAPI.Model]`s, generating full nested + flat CRUD routes with paginated
collections. Does **not** cover DDD entities or sorting/filtering (later cycles).

## 1. Motivation

The generator turns EF Core models into CRUD APIs, and supports `[Nested]`
*owned* aggregate parts. It does **not** understand *associations* between two
independent aggregate roots (e.g. `Author` ⟶ `Book`). Real APIs need
relationship-aware, nested sub-resource routes. This increment adds that,
and — because collection routes require it — a paginated list capability.

Non-goals for this cycle: many-to-many, one-to-one, sorting/filtering, DDD
entities, cascade-configurable deletes.

## 2. Relationship detection (naming convention)

A property is treated as a **foreign key** to a parent model when **all** hold:

1. Its name is `{Singular}Id`, where `{Singular}` is the singular name of an
   existing `[EZRestAPI.Model]` (e.g. `AuthorId` ⟶ model `Author`).
2. Its type is `int` (matching the only supported key type — see `EZR007`).
   `int?` is allowed and means an **optional** parent.
3. It is not marked `[EZRestAPI.Scalar]` (the opt-out attribute, new).

A model with several matching FKs (`AuthorId` + `PublisherId`) is a child of
several parents; each FK generates its own nested route group.

### Escape hatch and diagnostic

- `[EZRestAPI.Scalar]` on a `{Singular}Id` property forces it to be treated as
  a plain scalar, never an FK. (New attribute, emitted by `AttributesGenerator`.)
- **`EZR011` (Warning):** a property named `{X}Id` whose `{X}` matches no
  `[Model]`, **or** matches a `[Model]` but whose type is not `int`, when it
  *looks* like it was meant to be a foreign key. Message steers the user to
  either create the model, fix the type, or mark it `[Scalar]` to silence.
  Severity is **Warning**, never Error — a false positive must not break builds.

### Live fixture (regression guard)

`Example/Models/AuthorModel.cs` already declares `public Guid OrderId { get; set; }`
with **no** `Order` model and type `Guid`. This must remain a plain scalar and
must **not** become an FK. It is a required test case: convention name-match
alone is insufficient; the `int`-type + existing-model checks must both gate FK
treatment. (Whether it should emit `EZR011` given the type mismatch is decided
in §7 tests: it does **not**, because a non-`int` type is a strong signal it was
never intended as a key; only a name+missing-model with an `int` type warns.)

## 3. Routes (full nested + full flat)

For child `Book` (plural `Books`) with FK `AuthorId` ⟶ `Author` (plural `Authors`):

| Route | Verb | Behavior |
|---|---|---|
| `/books` | GET | Paginated list of all books |
| `/books` | POST | Flat create; `AuthorId` supplied in body |
| `/books/{id}` | GET / PUT / DELETE | Flat item |
| `/authors/{authorId}/books` | GET | Paginated list scoped to that author |
| `/authors/{authorId}/books` | POST | Create under author; **FK from route**, omitted from body |
| `/authors/{authorId}/books/{bookId}` | GET / PUT / DELETE | Scoped item |

Plural names are lowercased for routes, consistent with the existing generator.

## 4. Pagination

- Query params: `page` (1-based, default `1`), `pageSize` (default `20`,
  hard cap `100`; larger values are clamped to the cap, not rejected).
  `page < 1` or `pageSize < 1` → **400**.
- Stable ordering by `Id` ascending (deterministic paging).
- Envelope, identical for flat and nested collections:
  ```json
  { "items": [ ... ], "totalCount": 57, "page": 1, "pageSize": 20 }
  ```
- Generated as a reusable `PagedResponse<T>` type (one per assembly).

## 5. Referential integrity & status codes

- **DB constraint:** each FK produces a real relationship in the generated
  `OnModelCreating` with `DeleteBehavior.Restrict`. Nullable FK ⟶ optional
  relationship; non-nullable ⟶ required.
- **Scoped item routes** (`/authors/{authorId}/books/{bookId}`): the handler
  verifies `book.AuthorId == authorId`. Mismatch or missing book → **404**.
- **Nested POST** to a nonexistent parent (`authorId` not found) → **404**.
- **Flat POST/PUT** with a nonexistent FK value → **409 Conflict**.
- **DELETE parent with existing children** → RESTRICT ⟹ **409 Conflict**
  (associated children are preserved; the caller must reassign or delete them).
  This intentionally differs from `[Nested]` owned types, which still cascade.

Status-code decisions (approved): nonexistent-FK-on-write = **409**;
parent-delete-with-children = **409 (RESTRICT, no cascade)**.

## 6. Architecture

Follows the existing pipeline; no rewrite. New/changed pieces:

- **Symbol collection / model:** a new equatable `RelationshipInfo`
  (parent singular/plural, child singular/plural, FK property name,
  nullable flag) computed during model collection, alongside the existing
  model/nested providers. Source locations stay out of the cached model
  (consistent with commit `8701b42`). Detection lives next to the current
  property-collection code and reuses `StringExtensions`.
- **`AttributesGenerator`:** emit the new `[EZRestAPI.Scalar]` marker attribute.
- **`DiagnosticsGenerator`:** add `EZR011` descriptor + the check.
- **`RepositoryGenerator`:** add `ListAsync(page, pageSize)` returning a page
  slice + total count, and per-FK `ListByParentAsync(parentId, page, pageSize)`,
  plus scoped `ReadScopedAsync/UpdateScopedAsync/DeleteScopedAsync(parentId, id)`.
  Reuse existing DTO-based signatures.
- **`DtoGenerator`:** generate `PagedResponse<T>` once; nested create/update
  request DTOs **omit** the FK property (route owns it). Flat DTOs keep it.
- **`EndpointsGenerator`:** add the nested `RouteGroupBuilder` group per FK and
  the flat list route; wire status codes above.
- **`DbContextGenerator`:** add the FK relationship configuration in
  `OnModelCreating` with `DeleteBehavior.Restrict`.
- **Example project:** add a `BookModel` (plural `Books`) with
  `required int AuthorId` to exercise the feature against the existing
  `AuthorModel`, and keep `OrderId` as the false-positive guard.

Each generator change is independently testable via the existing harness.

## 7. Testing

**Fast generator tests (`EZRestAPI.Tests`, `CSharpGeneratorDriver`, no DB):**
- FK detected from `{Singular}Id` + `int`; nested route group generated.
- `int?` FK ⟶ optional relationship.
- Multiple FKs on one child ⟶ multiple nested groups.
- `[Scalar]` opt-out suppresses FK treatment.
- `OrderId` (Guid, no model) ⟶ **plain scalar, no FK, no `EZR011`**.
- A `{X}Id` `int` with no matching model ⟶ **`EZR011` warning** (build still succeeds).
- `PagedResponse<T>` generated once; nested create/update DTOs omit the FK.

**Integration tests (`Example.Tests`, Testcontainers SQL Server, real HTTP):**
- Flat + nested CRUD happy paths for `Author`/`Book`.
- Nested item scoping 404 (book of another author).
- Nested POST to missing author → 404.
- Flat POST with bad `AuthorId` → 409.
- DELETE author with books → 409; children still present afterward.
- Pagination: envelope shape, `totalCount`, `pageSize` cap clamp, `page<1` → 400.

## 8. Multi-agent execution plan (how the fleet advances it)

Implementation runs as a `Workflow`: independent implement→verify lanes
(EF/DbContext config, repository, DTOs+pagination, endpoints+status codes,
diagnostics+`[Scalar]`), each producing code + tests, each adversarially
verified against this spec and best-practice REST before acceptance; then a
final integration lane that builds the solution and runs the full test suite
(`dotnet test`) and reports pass/fail with output. Lanes touching generated
output are sequenced or worktree-isolated to avoid clobbering shared files.

## 9. Out of scope (explicit)

Many-to-many, one-to-one, sorting/filtering, cascade-delete option, DDD
entities, NuGet packaging. Tracked for later cycles.
