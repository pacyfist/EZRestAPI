# EF-faithful relationships, Phase 1 (one-to-many via navigation properties) — design

**Date:** 2026-07-23
**Status:** Draft (design), decisions captured inline from brainstorming.
**Goal (overall, multi-phase):** let a `[Model]` express links to other
`[Model]`s the way EF Core itself does — through **navigation properties** —
and eventually mirror **every** EF Core relationship setting. This is too large
for one spec, so it is phased. **This spec is Phase 1 only.**

## 0. Phasing (agreed)

- **P1 (this doc):** one-to-many via navigation properties + the per-link
  settings that apply to it (required/optional, delete behavior, FK name,
  inverse pairing). Establishes the nav-property foundation, cardinality
  inference, the generated FK, and the REST projection. Heaviest phase (reverses
  `EZR004`, reworks relationship detection).
- **P2:** one-to-one (reference nav on both ends, unique FK).
- **P3:** many-to-many (collection nav on both ends → join entity + link/unlink
  REST endpoints).
- **P4:** advanced keys & edge cases (composite FKs, alternate/principal keys,
  shadow FKs, self-references, the full delete-behavior matrix, one-navigation
  corner cases).

Each phase is its own spec → plan → build with a review gate.

## 1. Why (grounding)

Today a link is **implicit**: a property named `{Singular}Id` (`int`/`int?`)
that happens to match another `[Model]`'s singular name becomes a foreign key
(one-to-many, `RESTRICT`). Model-to-model **navigation properties are banned**
(`EZR004`). The maintainer wants links declared **explicitly and EF-faithfully**,
which means adopting EF's own mechanism — navigation properties — and letting
them carry EF's settings. P1 switches the mechanism to nav properties for the
one-to-many case while keeping the REST surface best-practice.

## 2. Declaring a link — navigation properties

Cardinality is inferred from the **shape** of the navigation (P1 recognizes
one-to-many only):

```csharp
[EZRestAPI.Model("Author", "Authors")]
public partial class AuthorModel
{
    [MaxLength(255)]
    public required string FirstName { get; set; }

    public List<BookModel> Books { get; set; } = [];   // collection nav = the "many" side
}

[EZRestAPI.Model("Book", "Books")]
public partial class BookModel
{
    [MaxLength(255)]
    public required string Title { get; set; }

    public required AuthorModel Author { get; set; }    // reference nav = the "one" side
}
```

- A **reference navigation** to another `[Model]` (`AuthorModel Author`) marks
  the declaring type as the **dependent** (the "one" side / foreign-key holder).
- A **collection navigation** of another `[Model]`
  (`List<BookModel>`/`IList`/`ICollection`/`IReadOnlyList`/`IReadOnlyCollection`)
  marks the declaring type as the **principal** (the "many" side).
- The **inverse is optional.** Declaring only `Book.Author` (no `Author.Books`)
  is a valid one-navigation one-to-many and generates the same relationship and
  routes. Declaring only `Author.Books` (no `Book.Author`) is also valid.
- Recognizing a nav requires the target to be a `[Model]`. A collection/reference
  of a `[Nested]` type is owned data (unchanged, §6), not a relationship.

**P1 recognizes only one-to-many shapes.** A reference-on-both-ends (one-to-one)
or collection-on-both-ends (many-to-many) shape is detected and reported as
"not supported until P2/P3" via a diagnostic (§7), never silently mis-modeled.

## 3. The generated foreign key (decision: generate it)

A reference nav needs a foreign-key value, which is also what REST writes/reads
use. When you declare `AuthorModel Author`:

- If you **do not** declare a matching FK property, EZRestAPI **generates**
  `public int AuthorId { get; set; }` on the dependent's partial class (nullable
  `int?` iff the nav is nullable). This is what appears in DTOs and routes.
- If you **do** declare `public int AuthorId`, it is used as-is.
- The FK name defaults to `{ReferenceNavName}Id` (so `Author` → `AuthorId`).
  Override with `[EZRestAPI.ForeignKey("...")]` on the nav.

The nav property is the *declaration* mechanism (cardinality + inverse +
settings + EF constraint); the generated FK is what the *API* operates on. The
nav is never loaded or serialized (reads use the FK id, §5), so there is no lazy
loading and no DTO cycle.

## 4. Per-link settings (mirroring EF one-to-one, for the one-to-many case)

Settings are attributes on the navigation property (or on the FK property when
there is no nav):

| EF setting | Expressed as | Default |
| --- | --- | --- |
| Required / optional | nav nullability (`Author` vs `Author?`) → FK `int`/`int?` | required |
| Delete behavior | `[EZRestAPI.OnDelete(DeleteBehavior.X)]` | **`Cascade`** (matches EF; see §8) |
| FK property name | `[EZRestAPI.ForeignKey("AuthorId")]` (or declare the property) | `{Nav}Id` |
| Inverse pairing (when ambiguous) | `[EZRestAPI.InverseProperty("Books")]` | by convention |

`DeleteBehavior` is EF Core's own enum (`Microsoft.EntityFrameworkCore`).
Generated `OnModelCreating` uses the real fluent call:
`entity.HasOne(e => e.Author).WithMany(p => p.Books).HasForeignKey(e => e.AuthorId).OnDelete(DeleteBehavior.X)`
— `WithMany(...)` names the inverse when present, `WithMany()` when it is a
one-navigation relationship. *(Alternate/composite `PrincipalKey` is P4.)*

## 5. REST projection (decision: link by id)

The API surface is unchanged from today's best-practice shape — only the
*declaration* mechanism changed.

- **To-one (reference nav):** the **FK id** appears in the read and write DTOs
  (`ReadBookResponse.AuthorId`, `CreateBookRequest.AuthorId`). Required nav → the
  FK is required in the create body; optional nav → nullable. The related object
  is **not embedded**; clients follow `/authors/{authorId}`.
- **To-many (collection nav):** **not embedded.** It remains the existing
  paginated sub-resource route `/authors/{id}/books` (and the flat `/books`).
- Routes, pagination, and status codes are exactly as today (flat + parent-scoped
  nested, `PagedResponse<T>`, `404`/`409` semantics per §8).

Because nothing embeds a related `[Model]`, there are no navigation cycles in
DTOs and no special cycle-breaking is needed.

## 6. Owned data unchanged

`[EZRestAPI.Nested]` owned types (value objects / child entities) are unaffected:
they embed in DTOs, have no routes, and are not "relationships between models."
A nav property whose target is `[Nested]` is owned data, not a link.

## 7. Back-compat & diagnostics

- **`{Singular}Id` convention keeps working.** Existing models (Book's
  `AuthorId`, Review's two FKs, Chapter's `BookId`) are unchanged and additive
  with nav properties. Nav properties are the new, richer, explicit way; the
  convention is the shorthand. A model may mix both.
- **`EZR004` reverses.** Model-to-model navigation moves from *error* ("use an
  id, not a navigation") to *the supported mechanism*. The descriptor is
  repurposed/removed; its old test is replaced.
- **New P1 diagnostics** (exact ids assigned in the plan):
  - A nav whose target is not a `[Model]` (and not `[Nested]`) → error.
  - A one-to-one shape (reference on both ends) → error "one-to-one arrives in
    Phase 2."
  - A many-to-many shape (collection on both ends) → error "many-to-many arrives
    in Phase 3."
  - An ambiguous inverse (two navs to the same type with no `[InverseProperty]`)
    → error steering to `[InverseProperty]`.
  - `EZR011` (unresolved `{X}Id`) is retained for the convention path.

## 8. Behavior change / migration (needs maintainer eyes at review)

Adopting EF's default delete behavior (**`Cascade`** for a required one-to-many)
is a deliberate change from today's `RESTRICT`:

- **Before:** deleting a parent that still has children → `409 Conflict`
  (children preserved).
- **After (nav-property relationships):** deleting a parent **cascades** to its
  children by default. To keep the refuse-with-409 behavior, set
  `[EZRestAPI.OnDelete(DeleteBehavior.Restrict)]`.

Open sub-decision for review: whether the **legacy `{Singular}Id` convention**
also adopts the `Cascade` default (uniform, but changes existing
Example/tests behavior — the delete-parent-`409` tests), or **keeps `RESTRICT`**
for back-compat (non-breaking, but the two mechanisms then differ in default).
**Recommendation:** keep the convention at `RESTRICT` (non-breaking); nav-prop
relationships use EF's `Cascade` default; both honor `[OnDelete]`. Documented as
a known, intentional difference.

## 9. Architecture (files)

- `AttributesGenerator.cs`: emit `OnDeleteAttribute` (wraps
  `Microsoft.EntityFrameworkCore.DeleteBehavior`), `ForeignKeyAttribute`,
  `InversePropertyAttribute`.
- `ProviderExtensions.cs`: detect reference/collection navs to `[Model]` types;
  resolve cardinality (one-to-many only in P1), the (declared or generated) FK,
  required-ness, delete behavior, and inverse. Extend the `RelationshipInfo`
  record with delete behavior + resolved FK/nav names. Keep the `{Singular}Id`
  convention path and merge its results with nav-derived relationships
  (de-duplicated).
- `ModelGenerator.cs`: generate the FK property on the dependent partial when a
  reference nav has no explicit FK.
- `DbContextGenerator.cs`: emit `HasOne/WithMany/HasForeignKey/OnDelete` from the
  resolved relationship (replacing the hardcoded `RESTRICT`; convention default
  stays `RESTRICT` per §8).
- `DtoGenerator.cs`: ensure the FK id (not the nav object) is the field in
  read/create/update DTOs; never emit a related `[Model]` as an embedded DTO.
- `EndpointsGenerator.cs`: nested route generation keyed off the resolved
  relationship regardless of which side declared the nav (unchanged shape).
- `DiagnosticsGenerator.cs`: repurpose `EZR004`; add the P1 diagnostics (§7).
- `Example/Models/*`: add a nav-property one-to-many pair to demonstrate P1
  (e.g. re-express or add alongside Author/Book), plus an `[OnDelete(Restrict)]`
  example and an optional (`Author?`) example.

## 10. Testing

**Fast generator tests (`EZRestAPI.Tests`):**
- A reference nav to a `[Model]` generates the FK property, the `HasOne/WithMany`
  config, and puts the FK id (not an embedded object) in the DTOs.
- A collection nav generates the nested paginated route; a one-navigation
  relationship (only one side) still generates the full relationship + routes.
- `[OnDelete(DeleteBehavior.X)]` emits the matching `OnDelete(...)`; default nav
  relationship emits `Cascade`; the `{Singular}Id` convention still emits
  `Restrict`.
- Nullable nav (`Author?`) → nullable FK + optional in the create body.
- `[ForeignKey]`/`[InverseProperty]` honored; ambiguous inverse → diagnostic.
- One-to-one and many-to-many shapes each raise their "arrives in P2/P3"
  diagnostic. `EZR004` no longer fires for a model-to-model nav.

**Runnable no-Docker (`Example.Tests`):** the OpenAPI document for the nav-based
pair matches the id-based pair (same routes, FK id fields, no embedded related
model, nested collection route present).

**Docker integration (`Example.Tests`, "MsSql"):** create a parent, create a
child linking by FK id, list children under the parent, and verify the delete
behavior (a `Cascade` relationship deletes children; a `Restrict` one returns
`409`). Run by the maintainer where Docker exists.

## 11. Out of scope (P1)

One-to-one (P2); many-to-many + link/unlink routes (P3); composite FKs,
alternate/principal keys, shadow FKs, self-references, the full delete-behavior
matrix, embedding related resources on read (explicitly rejected — link by id),
and lazy/eager loading of navigations.
