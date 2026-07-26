# Models

`[EZRestAPI.Model(singular, plural)]` marks an **EF Core model**: a table with
columns. Plain data to store and serve. The generator reads it at compile time
and writes the storage and API code for it.

A model holds no rules, so the generator can own it fully — it creates,
replaces and deletes the whole object. A class that *does* have rules to protect
is an `[Aggregate]`; see [model-vs-aggregate.md](model-vs-aggregate.md).

## Rules for the class

- It must be `partial`, so the generator can add to it (`EZR001`).
- The singular and plural names must be valid C# identifiers (`EZR008`).
- No two models may share a singular name (`EZR002`) or a plural name (`EZR003`).
- A class cannot be both `[Model]` and `[Nested]`, or both `[Model]` and
  `[Aggregate]` (`EZR010`).

## The key

Every model gets `[Key] public int Id { get; set; }` written into its partial
class. `int` is the only supported key type. If the user declares their own
`Id`, it is used as-is and must be `int` (`EZR007`).

## Storage

Every model gets a `DbSet<T>` on the generated `CustomDbContext`, named after
the plural name. This happens for **every** model, whatever its `Endpoints`
flags say — see [endpoints.md](endpoints.md).

## Properties

Public instance properties with a getter **and** a setter become columns.
EF Core decides the SQL type; `[Column(TypeName = "...")]` overrides it.

Proven scalar types: `string`, `bool`, `byte`, `short`, `int`, `long`, `float`,
`double`, `decimal`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`, and
`byte[]`. Add `?` for nullable. `Example/Models/AuthorModel.cs` uses every one
and records the SQL column it maps to. Other types are passed to EF Core
untouched; they are not tested and there is no diagnostic for an unsupported
one.

Two kinds of property are not columns:

- A property whose type is another `[Model]` — an error today (`EZR004`); see
  the Planned section in [relationships.md](relationships.md).
- A property whose type is `[Nested]` — owned data, see
  [nested-types.md](nested-types.md).

An `int`/`int?` property named `{Singular}Id` may be read as a foreign key —
see [relationships.md](relationships.md).

## Validation

Property attributes from `System.ComponentModel.DataAnnotations` are copied
onto the generated request DTOs, verbatim: the attribute type, its positional
arguments, and its named arguments. This covers `[Required]`, `[MaxLength]`,
`[MinLength]`, `[StringLength]`, `[Range]`, `[RegularExpression]`,
`[EmailAddress]`, `[Phone]`, `[Url]`, `[Compare]`, and anything else in that
namespace. Both flat and nested request DTOs get them.

One attribute is added that the user did not write: a **non-nullable
reference-typed** property gets `[Required]` if it does not already have one.
So a missing `string Title` is caught as a `422` instead of failing at the
database. A missing `int` cannot be caught this way — it binds to `0`.

Every generated `POST` and `PUT` handler validates the request before it
touches the repository. Failures return `422` with an `errors` field map; see
[errors.md](errors.md).

### Why reflection and not `AddValidation()`

.NET 10's built-in minimal-API validation finds validatable types with its own
source generator. One Roslyn generator cannot see another's output in the same
compilation, so the framework generator would never see our DTOs and would
validate nothing. Instead a small generated helper, `EZRestAPIValidation`,
calls `Validator.TryValidateObject` at runtime. That works whatever order the
generators run in.

## Generated DTOs

For a model named `Book`:

| Type | Fields |
| --- | --- |
| `CreateBookRequest` | writable properties, with validation attributes |
| `CreateBookResponse` | `Id` plus the created values |
| `ReadBookResponse` | `Id` plus all properties |
| `UpdateBookRequest` | writable properties, with validation attributes |

Nested (parent-scoped) variants are named `Create{Child}Under{Parent}Request`
and `Update{Child}Under{Parent}Request`. They drop the foreign key, because the
route supplies it.

These are always generated, whatever `Endpoints` says, along with a
`{Singular}Repository` that uses them. `Endpoints` only decides which routes
exist. See [endpoints.md](endpoints.md).
