# Nested (owned) types

`[EZRestAPI.Nested("Comment")]` marks a class that belongs to one parent and has
no life of its own — a value object or a child entity. It maps to an EF Core
**owned type**.

Owned data has no routes and no id of its own. You send it inside the parent and
read it back inside the parent. Deleting the parent deletes it too.

Nested types are used by `[Model]`.

## Shapes

A property on the owner decides how it maps:

| Property shape | EF mapping |
| --- | --- |
| A single `[Nested]` type (`Address Address`) | `OwnsOne` |
| A collection of a `[Nested]` type | `OwnsMany` |

Supported collection types are `List<T>`, `IList<T>`, `ICollection<T>`,
`IReadOnlyList<T>`, and `IReadOnlyCollection<T>`. Anything else raises `EZR009`.

Every shape needs a public getter and setter, the read-only interfaces
included: `IReadOnlyList<Reaction> { get; set; }` maps as `OwnsMany` with the
element exposed as `ReactionDto`, never as the owning type.

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
