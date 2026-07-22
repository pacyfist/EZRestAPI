# Example project feature tour — design

**Date:** 2026-07-22
**Status:** Draft (design), approved decisions captured inline.
**Scope:** Expand the `Example` project into a comprehensive, complexity-ordered
tour of the generator's feature surface, backed by tests. Showcase models are
readable and well-commented; each is paired with a no-Docker OpenAPI assertion
and, where it exercises runtime behavior, a Docker ("MsSql") integration test.
This is a *demonstration + coverage* increment — it adds no new generator
features of its own, but it will surface any unsupported feature combinations.

## 0. Goals and non-goals

**Goals.** (1) Demonstrate every generator feature at least once, ordered from
the simplest scalar model to the most complex DDD aggregate. (2) Prove each
demonstration actually works: it compiles through the real generator (`Example`
build) and is asserted (OpenAPI doc for shape; Docker integration for runtime).
(3) Serve as living documentation a new user can read top-to-bottom.

**Non-goals.** Building new generator capabilities. Where a feature *combination*
is not yet supported, this increment does **not** implement it (see §5, gap
policy). No changes to the generator projects except small/obvious fixes.

## 1. Current coverage (baseline)

Already demonstrated in `Example/Models/`:

| Model | Feature shown |
| --- | --- |
| `SimpleDataModel` | Basic scalar `[Model]` (int, double, string?, DateTimeOffset) |
| `AuthorModel` | Exhaustive C#→SQL type coverage; `[MaxLength]`, `[Column(TypeName)]` |
| `BookModel` | Relationship via `AuthorId` FK convention → nested `/authors/{id}/books` |
| `PostModel`→`CommentModel`→`ReactionModel` | 2-level `[Nested]` owned **collection** |
| `OrderAggregate` | DDD: static-method `[Factory]`, value object (`CustomerRef`), get-only `IReadOnlyList` projection, `[Command]` Cancel/AddLine |

Gaps: rich validation annotations (only `[MaxLength]` today), the `[Scalar]`
opt-out (+`EZR011`), multi-FK children, deep relationship chains, single
(non-collection) owned references, constructor `[Factory]`, and `OwnsMany`
child-entity collections on aggregates.

## 2. New models (complexity-ordered)

Each model lives in its own file under `Example/Models/`, matching the existing
style (namespace, `partial`, XML/inline comments explaining the feature).

### Tier 1 — Scalars & validation (safe; proven features)

1. **`RegistrationModel`** `[Model("Registration","Registrations")]` — rich
   validation → **422**. Fields exercise `[Required]`, `[EmailAddress]`
   (`Email`), `[Range(18,120)]` (`Age`), `[RegularExpression]` (`PhoneNumber`),
   `[StringLength(3,32)]` (`Username`), `[Url]` (`Website`). Purpose: show the
   validation pipeline rejects each annotation kind with an RFC 9457 `errors`
   map, beyond the `[MaxLength]` already shown.

2. **`SensorReadingModel`** `[Model("SensorReading","SensorReadings")]` — has an
   `int ExternalId` that matches the `{Singular}Id` FK shape but is **not** a
   relationship (there is no `External` model). Marked
   `[EZRestAPI.Scalar]` to opt out, so it stays a plain column and generates no
   nested route. Purpose: demonstrate the `[Scalar]` opt-out and that it
   suppresses the `EZR011` "unresolved FK-shaped property" warning. Also carries
   a normal scalar payload (`double Value`, `DateTimeOffset TakenAt`).

### Tier 2 — Relationships (may reveal generator gaps)

3. **`ReviewModel`** `[Model("Review","Reviews")]` with **two** FK properties,
   `AuthorId` and `BookId` → nested route groups under both
   `/authors/{id}/reviews` and `/books/{id}/reviews`, plus the flat `/reviews`.
   Purpose: multiple parents on one child.

4. **`ChapterModel`** `[Model("Chapter","Chapters")]` with `BookId` — and `Book`
   itself has `AuthorId`, forming a 3-level chain Author→Book→Chapter. Purpose:
   a relationship whose parent is itself a child.

### Tier 3 — Nested owned types (may reveal a gap)

5. **`ProfileModel`** `[Model("Profile","Profiles")]` with a **single**
   (non-collection) `[Nested]` owned reference `Address` (an `AddressModel` with
   `Street`, `City`, `PostalCode`) → `OwnsOne` in the anemic `[Model]` path,
   contrasting Post's owned *collection*.

### Tier 4 — DDD aggregates (most complex; may reveal gaps)

6. **`ShoppingCart`** `[Aggregate("ShoppingCart","ShoppingCarts")]` whose
   creation entry point is a **public constructor** marked `[EZRestAPI.Factory]`
   (`public ShoppingCart(CustomerRef owner)`), exercising the `new Type(args)`
   creation path (Order shows the static-method path). Add a `[Command]`
   (e.g. `Checkout`) with an invariant guard.

7. **`Invoice`** `[Aggregate("Invoice","Invoices")]` with an **`OwnsMany`
   child-entity collection**: `IReadOnlyList<InvoiceLine>` over a private backing
   `List<InvoiceLine>`, where `InvoiceLine` is a `[Nested]` child entity with
   real fields (`Sku`, `Quantity`, `UnitPrice`) — not Order's `string`
   projection. A `[Command] AddLine(sku, quantity, unitPrice)` appends a line;
   reads embed the `InvoiceLineDto` list. Purpose: owned child-entity collection
   on an aggregate.

## 3. Tests

**No-Docker OpenAPI (`Example.Tests/GeneratorTests/OpenApiDocumentTests.cs`).**
Extend with per-tier assertions that the document describes each new resource
correctly: Registration's POST advertises 422 with the validation `errors`
schema; SensorReading has a flat `externalId` field and **no** nested route;
Review appears under both parent paths; Chapter under `/books/{id}/chapters`;
Profile's read embeds an `AddressDto`; ShoppingCart has POST create (constructor
factory) + its command; Invoice's read embeds the `InvoiceLine` collection and
its `add-line` command exists. These run here (no Docker).

**Docker integration (`Example.Tests/GeneratorTests/`, "MsSql" collection).**
Add runtime tests for the behavior-bearing additions, matching the existing
fixture/collection style: Registration create with an invalid email/age → 422
problem+json with field errors; Review create then read under each parent;
Profile create with an embedded address then read shows it; ShoppingCart create
via the constructor factory then read; Invoice create, `add-line`, then read
shows the line collection. Compile-checked here; run by the maintainer where
Docker exists.

## 4. Architecture / where things change

- `Example/Models/*.cs` — seven new model files (plus `AddressModel`,
  `InvoiceLine` nested/child types). Additive; no existing model changes except
  as needed to form the Author→Book→Chapter chain (Book is unchanged; Chapter
  references it).
- `Example.Tests/GeneratorTests/OpenApiDocumentTests.cs` — new assertions.
- `Example.Tests/GeneratorTests/` — new integration test file(s) in the "MsSql"
  collection.
- No changes to `EZRestAPI/` generator projects except small/obvious fixes
  permitted under the gap policy (§5).

## 5. Gap policy (approved: green-first, flag gaps)

Tiers 2–4 combine features in ways the generator may not fully support yet.
For each addition: verify against the real generator (fast tests + `Example`
build + `Example.Tests` build + OpenAPI test). If a combination does **not**
generate cleanly:

1. If the fix is small and obvious (a few lines, no design change), apply it.
2. Otherwise, **keep Example green**: reduce that model to the supported subset
   (or omit it), and record the unsupported combination as a follow-up item in
   this spec's §7 and in the progress memory. Do not expand into a generator
   feature build.

The build must be green at the end. Every dropped/limited edge case is reported
explicitly, never silently.

## 6. Delivery

A multi-agent workflow in the established shape: serial per tier
(implement-until-green, since models share the OpenAPI test and DbContext),
then a kept progress-reviewer that re-runs every gate and audits coverage, then
the integration lane. Agents do not commit; the maintainer commits per tier
after independent verification.

## 7. Follow-ups / unsupported combinations discovered

**Resolved inline (small fix under the gap policy):** the `Invoice` aggregate's
`OwnsMany` child-entity collection exposed as `IReadOnlyList<InvoiceLine>` was
leaking the raw domain entity `InvoiceLine` into `ReadInvoiceResponse` and the
OpenAPI schema set (no `InvoiceLineDto`), and was **not** mapped as an EF owned
type. Root cause: `ProviderExtensions.SupportedCollectionTypes` recognized only
`List<T>`/`IList<T>`/`ICollection<T>`, not the idiomatic DDD read-only
projection interfaces. Fix: added `IReadOnlyList<T>` and `IReadOnlyCollection<T>`
to that set, so their nested element resolves to `{Nested}Dto` and the property
maps as `OwnsMany`. This also repairs the flagship case (any aggregate exposing
a child-entity collection). Regression tests added in `AggregateReadTests.cs`
(`ReadResponse_MapsReadOnlyListOfNestedEntity_ToDtoList_NotRawEntity`,
`DbContext_MapsReadOnlyListOfNestedEntity_AsOwnsMany_NotPrimitiveCollection`);
the Tier-4 OpenAPI fact was tightened to assert `InvoiceLineDto` and that no
`InvoiceLine` component leaks. `IReadOnlyList<string>` primitive projections
(e.g. Order's `Lines`) are unaffected — they stay `PrimitiveCollection` + field
access. **No outstanding unsupported combinations** — every other tier
(multi-FK child, 3-level chain, single `OwnsOne`, constructor `[Factory]`)
generated cleanly with no reduction.

## 8. Testing gates (recap)

```
export NUGET_PACKAGES="$PWD/.nuget-cache"
FLAGS="-p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q"
dotnet test  EZRestAPI.Tests $FLAGS
dotnet build Example/Example.csproj $FLAGS
dotnet build Example.Tests $FLAGS -m:1
dotnet test  Example.Tests --filter OpenApiDocumentTests -m:1 $FLAGS
```
Docker "MsSql" integration tests are written and compile-checked but run by the
maintainer where Docker exists.
