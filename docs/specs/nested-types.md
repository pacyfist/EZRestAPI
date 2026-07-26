# Nested (owned) types

`[EZRestAPI.Nested("Comment")]` marks a class that belongs to one parent and has
no life of its own — a value object or a child entity. It maps to an EF Core
**owned type**.

Owned data has no routes and no id of its own. You send it inside the parent and
read it back inside the parent. Deleting the parent deletes it too.

Nested types are used by both `[Model]` and `[Aggregate]`.

## Shapes

A property on the owner decides how it maps:

| Property shape | EF mapping |
| --- | --- |
| A single `[Nested]` type (`Address Address`) | `OwnsOne` |
| A collection of a `[Nested]` type | `OwnsMany` |
| A collection of a primitive (`IReadOnlyList<string>`) | `PrimitiveCollection`, with field access |

Supported collection types are `List<T>`, `IList<T>`, `ICollection<T>`,
`IReadOnlyList<T>`, and `IReadOnlyCollection<T>`. Anything else raises `EZR009`.

The two read-only interfaces matter for aggregates: a get-only
`IReadOnlyList<InvoiceLine>` over a private backing list is the idiomatic DDD
projection, and it must map as `OwnsMany` with the element exposed as
`InvoiceLineDto` — not as the raw domain entity.

## Nesting

Nested types may contain other nested types, to any depth. `Post` → `Comment` →
`Reaction` is the Example project's case. The shape must be a tree: a cycle is
an error (`EZR005`).

Two `[Nested]` classes may not share a singular name (`EZR006`).

## Generated code

Each nested type gets one `{Singular}Dto` plus mappers, generated from the
`[Nested]` attribute alone. That is independent of any `[Model]`, so a nested
DTO is emitted even when every model in the assembly is `Endpoints.None`.

The DTO is embedded in the owner's request and response DTOs, recursively.
