# Aggregates

`[EZRestAPI.Aggregate("Order", "Orders")]` marks a **DDD domain entity**. Use it
when the class has rules about its own state: an order that must not be
cancelled after it ships, a line that must not have a negative quantity.

A `[Model]` matches a table. An aggregate matches your domain. EF Core still
stores it, but that is not what shapes it. See
[model-vs-aggregate.md](model-vs-aggregate.md) for the choice.

One rule drives the rest: **the aggregate is the only thing you address.**
Everything inside travels with it, creation goes through one factory, and every
change is a named command — never a `PUT`.

## The class

Same rules as `[Model]`: `partial`, an `int Id` is generated, singular and
plural names must be unique (`EZR002`/`EZR003`), and a class cannot be both
`[Model]` and `[Aggregate]` (`EZR010`).

EF Core loads rows through a **private parameterless constructor**, which the
class must declare, and writes private-setter properties through its backing
fields. No public setters are needed.

The aggregate gets a `DbSet` and joins `CustomDbContext` exactly like a
`[Model]`.

## Reads

Aggregates collect properties by a looser rule than `[Model]`: **every public
getter counts**, whatever the setter is. So `{ get; private set; }`,
`{ get; init; }` and get-only `{ get; }` projections all appear in
`ReadOrderResponse`. (The `[Model]` rule needs a setter and would drop them.)

Value objects and child lists inside the aggregate are `[Nested]` owned types —
see [nested-types.md](nested-types.md). They are embedded, never separately
addressable.

`GET /orders` (paginated) and `GET /orders/{id}` are generated in the usual
shape.

## Creation — `[Factory]`

An aggregate declares exactly **one** creation entry point marked
`[EZRestAPI.Factory]`. It may be a public static method returning the aggregate
type, or a public constructor.

```csharp
[EZRestAPI.Factory]
public static Order Place(CustomerRef customer) => new() { Customer = customer };
```

- `CreateOrderRequest` mirrors the factory's parameters by name and type. A
  value-object parameter nests its `{VO}Dto`.
- `POST /orders` maps the request to the factory arguments, calls it, saves, and
  returns `201` with a `Location` header and a `ReadOrderResponse` body.
- Zero or more than one `[Factory]` is an error (`EZR012`) — creation would be
  impossible or ambiguous.

Generated code never object-initializes the aggregate, so private setters are
never assigned from outside and the class keeps its rules.

## Changes — `[Command]`

Each public instance method marked `[EZRestAPI.Command]` becomes its own
endpoint:

```
POST /orders/{id}/cancel
POST /orders/{id}/add-line
```

- The route name is the attribute's argument (`[Command("cancel")]`) or the
  method name kebab-cased (`AddLine` → `add-line`).
- The request body is a generated `{Method}{Singular}Request` holding the
  method's parameters. A parameterless command takes no body.
- The handler loads the **tracked** aggregate (404 if missing), maps the body to
  arguments, calls the method, saves, and returns `200` with the updated
  `ReadOrderResponse`.
- The repository exposes one `Execute{Method}Async` per command.

## Errors from commands

Commands throw ordinary exceptions. The handler maps them:

| Thrown | Status | Meaning |
| --- | --- | --- |
| `ArgumentException` and subclasses | `422` | The command's argument guard rejected the input |
| `InvalidOperationException` | `409` | A state guard refused the transition |
| anything else | `500` | Propagates |

The `ArgumentException` arm is written first so `ArgumentOutOfRangeException`
never falls into a broader arm.

The exception's `Message` becomes the `detail` of the `problem+json` body. Domain
messages are written for developers, so keeping secrets out of them is the
author's responsibility.

## No `PUT`, and delete

There is deliberately **no** `PUT` for an aggregate — a full replace would
bypass every rule. `DELETE /orders/{id}` returns `204`, or `404` if it is not
there. Owned data cascades with it.

## No endpoint flags

`[Aggregate]` has no `Endpoints` property. Its surface is all-or-nothing, and
its repository is always registered.

## Out of scope

`PATCH` and free-form editable properties; more than one factory; commands
returning something other than `void` or the aggregate; domain events; links
between aggregates; the `POST /orders/{id}:cancel` URL form; long-running
commands returning `202`.
