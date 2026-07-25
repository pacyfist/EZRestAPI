# EZRestAPI

Write a plain C# class. Get a full REST API. No controllers to write.

EZRestAPI is a source generator. It reads your classes at compile time and writes the
code for a working REST API. You only add attributes to your own classes.

**What you get:**

- CRUD routes for each class (create, read, list, update, delete) backed by a database.
- Entity Framework Core storage and ASP.NET Core minimal API routes, generated for you.
- OpenAPI docs and clean error responses (RFC 9457 `problem+json`) out of the box.

## Contents

- [Add it to your project](#add-it-to-your-project)
- [Quick start](#quick-start)
- [Your models](#your-models)
- [Endpoints](#endpoints)
- [Links between models](#links-between-models)
- [Owned data](#owned-data)
- [Rich domain models (aggregates)](#rich-domain-models-aggregates)
- [Errors](#errors)
- [API docs (OpenAPI)](#api-docs-openapi)
- [Build warnings](#build-warnings)
- [The Example project & tests](#the-example-project--tests)

## Add it to your project

Reference the generator as an analyzer (it runs during build; there is no NuGet
package yet):

```xml
<ItemGroup>
  <ProjectReference Include="..\EZRestAPI\EZRestAPI.csproj" OutputItemType="Analyzer" />
</ItemGroup>
```

Wire it up in `Program.cs`. Give your `DbContext` a connection string named after it
(here the context is `CustomDbContext`, so the string is named `CustomDbContext`):

```csharp
builder.Services.AddDbContextFactory<CustomDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("CustomDbContext")));

builder.Services.AddEZRestAPI();   // register the generated services
builder.Services.AddOpenApi();     // needed for MapOpenApi below

var app = builder.Build();

app.MapEZRestAPI();   // add the generated routes
app.MapOpenApi();     // serve the OpenAPI document

app.Run();
```

## Quick start

Mark a class with `[EZRestAPI.Model(...)]`. The first name is singular, the second
is plural. Make the class `partial` so the generator can add to it (it adds an `int Id`
key for you):

```csharp
[EZRestAPI.Model("Book", "Books", Endpoints = EZRestAPI.Endpoints.All)]
public partial class BookModel
{
    public required string Title { get; set; }
}
```

`Endpoints` says which routes to expose; here `All` turns on every one. See
[Endpoints](#endpoints) for the other options. That gives you these routes.
The path is the plural name, lowercased (`/books`):

- `POST /books` — create one. Returns `201 Created`.
- `GET /books/{id}` — read one. Returns `200 OK`, or `404` if not found.
- `GET /books` — list them, paginated (`?page` and `?pageSize`). Returns `200 OK`.
- `PUT /books/{id}` — replace one. Returns `204 No Content`, or `404` if not found.
- `DELETE /books/{id}` — delete one. Returns `204 No Content`, or `404` if not found.

## Your models

A model is a plain C# class. Mark it `partial` and add `[EZRestAPI.Model]` with a
singular and a plural name. At compile time, EZRestAPI reads the class and always writes:

- An `Id` field (`int`, the primary key). You don't write it. If you want your own `Id`, add one — it must be an `int`.
- A `DbSet` on your database context, named after the plural name (so `Books`). This is the table.

Everything else — the repository, the DTOs, and the REST endpoints — depends on the
`Endpoints` flags you set on `[Model]`. See [Endpoints](#endpoints) for what those
generate and how to choose them.

### Field types

Your model's public properties become table columns. Common types work as you'd expect:

- `string`
- `int`, `long`
- `bool`
- `decimal`
- `DateTime`, `DateTimeOffset`
- `Guid`
- `byte[]`

More are supported too: `byte`, `short`, `float`, `double`, and `TimeSpan`. Add `?` to make a field nullable (`string?`).

For the full list of every supported type, see `Example/Models/AuthorModel.cs`. It uses each one and shows the database column it maps to.

### Validation

Put standard .NET validation attributes on your fields. EZRestAPI copies them onto the generated request types and checks them on every `POST` and `PUT`. Common ones:

- `[Required]` — the field must be sent.
- `[MaxLength(n)]` / `[StringLength(min, max)]` — length limits.
- `[Range(min, max)]` — number range.
- `[EmailAddress]` — must look like an email.
- `[RegularExpression(pattern)]` — must match a pattern.

```csharp
[EZRestAPI.Model("Registration", "Registrations", Endpoints = EZRestAPI.Endpoints.All)]
public partial class RegistrationModel
{
    [Required]
    [StringLength(32, MinimumLength = 3)]
    public required string Username { get; set; }

    [Required]
    [EmailAddress]
    public required string Email { get; set; }

    [Range(18, 120)]
    public int Age { get; set; }
}
```

If the input is bad, the API returns `422` with an `application/problem+json` body (RFC 9457). It includes an `errors` map: each field name points to a list of what went wrong. So the caller sees exactly which fields failed and why.

```json
{
  "type": "...",
  "title": "One or more validation errors occurred.",
  "status": 422,
  "detail": "One or more fields failed validation; see 'errors' for details.",
  "code": "unprocessableEntity",
  "errors": {
    "Username": ["The field Username must be a string with a minimum length of 3 and a maximum length of 32."],
    "Email": ["The Email field is not a valid e-mail address."]
  }
}
```

One extra check happens for free: a non-nullable `string` (like `string Title` with no `?`) is treated as `[Required]`, so a missing value is caught as a `422` instead of failing later.

See `Example/Models/RegistrationModel.cs` for a full validation example.

## Endpoints

`[Model]` takes an `Endpoints` flag saying which routes to expose:

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

| Flag | Route it turns on |
| --- | --- |
| `List` | `GET /{plural}` |
| `Create` | `POST /{plural}` |
| `Read` | `GET /{plural}/{id}` |
| `Update` | `PUT /{plural}/{id}` |
| `Delete` | `DELETE /{plural}/{id}` |
| `Nested` | the nested form of whichever verbs above are also set (see [Links between models](#links-between-models)) |

Combine flags with `|`, e.g. `Endpoints = EZRestAPI.Endpoints.List | EZRestAPI.Endpoints.Read`.
Three presets cover the common cases: `Crud` (every flat route, no nested routes),
`ReadOnly` (read-only, flat and nested), and `All` (everything).

### The default is `None`

Leave `Endpoints` off `[Model]` and you get `None`. The model is still registered
on the `DbContext` and gets its `Id` — that much always happens — but nothing
else: no repository, no DTOs, no routes, and it is left out of `AddEZRestAPI` and
`MapEZRestAPI`. That is a supported way to persist a type with no API of its own;
see `AuditLogModel` in [the Example project](#the-example-project--tests).

### Any other value builds everything, and exposes only what you chose

Set even one flag — say just `Endpoints.List` — and EZRestAPI still generates the
*complete* repository and DTO surface: `CreateAsync`, `ReadAsync`, `UpdateAsync`,
`DeleteAsync`, `ListAsync`, and every request/response type (`CreateBookRequest`,
`UpdateBookRequest`, and so on). Only the routes for the flags you actually set
get mapped. The usual reason to leave out `Create` is that you want to hand-write
`POST /books` yourself — with `CreateAsync` and `CreateBookRequest` already there,
your replacement endpoint has something to build on instead of writing that
plumbing from scratch.

### `Nested` is ANDed with the verbs, not added to them

`Nested` has no route of its own. It switches on the nested *form* of whichever
verbs are also set. `Endpoints.List | Endpoints.Nested` gives you both
`GET /books` and `GET /authors/{authorId}/books`. `Endpoints.Crud` deliberately
leaves `Nested` out, so it gives flat routes only, even for a model with a
foreign key that would otherwise make it nestable.

### Three diagnostics watch this

- `EZR013` (Warning) — `Create` is set without `Read`. The `Create` response's
  `Location` header points at the `Read` route, which doesn't exist without it,
  so the header would point nowhere.
- `EZR014` (Info) — the model is `Endpoints.None` and generates no API surface
  at all: no repository, no DTOs, no endpoint class, just the `DbSet`.
- `EZR015` (Info) — the model's `Endpoints` is non-zero (so the repository,
  DTOs and endpoint class ARE generated) but selects no verb, so no route is
  generated either. `Endpoints.Nested` alone is the common way to hit this: it
  only switches the nested *form* of whichever verbs are also set, so on its
  own it selects nothing.

`EZR014` and `EZR015` are both informational, not mistakes, but easy to miss:
MSBuild's console logger hides Info diagnostics at the default verbosity, so
if a model you expected to have an API doesn't, rebuild with `-v detailed` to
see them (an IDE shows them as normal).

## Links between models

To link two models, add a property named `{Singular}Id`. If its name (minus `Id`)
matches another model's singular name, and its type is `int` or `int?`, it becomes
a foreign key. Use `int?` if the link is optional.

`Book` points to `Author`:

```csharp
[EZRestAPI.Model("Book", "Books", Endpoints = EZRestAPI.Endpoints.All)]
public partial class BookModel
{
    [MaxLength(255)]
    public required string Title { get; set; }

    public required int AuthorId { get; set; }  // -> Author
}
```

You get two ways to reach the same books:

- Flat: `/books`, `/books/{id}` (all the usual CRUD).
- Nested under the parent: `/authors/{authorId}/books`, `/authors/{authorId}/books/{id}`.

The nested routes only show books that belong to that author. Creating a book under
`/authors/5/books` sets `AuthorId` to `5` for you.

### Pagination

The two list routes (`GET /books` and `GET /authors/{authorId}/books`) return one
page at a time. Two query values control it:

- `?page` — which page, starts at `1` (default `1`).
- `?pageSize` — how many per page (default `20`, max `100`).

If you ask for more than 100, it quietly gives you 100. If `page` or `pageSize` is
less than 1, you get `422` with a message.

The response wraps the list:

```json
{
  "items": [ ... ],
  "totalCount": 42,
  "page": 1,
  "pageSize": 20
}
```

### When you get an error

- Reading `/authors/5/books/9` but book 9 belongs to another author -> `404`.
- Using a parent id that does not exist (`/authors/999/books`) -> `404`.
- Creating or updating a book with an `AuthorId` that does not exist -> `422`.
- Deleting an author that still has books -> `409` (the books block it).

## Owned data

Use `[EZRestAPI.Nested]` for parts that belong to one parent and have no life of
their own. They are stored with the parent (EF owned types). They get no routes.
You save the whole tree in one call, and deleting the parent deletes them too.

A post has comments, and each comment has reactions:

```csharp
[EZRestAPI.Model("Post", "Posts", Endpoints = EZRestAPI.Endpoints.All)]
public partial class PostModel
{
    [MaxLength(255)]
    public required string Title { get; set; }

    public required List<CommentModel> Comments { get; set; }
}

[EZRestAPI.Nested("Comment")]
public class CommentModel
{
    [MaxLength(1024)]
    public required string Text { get; set; }

    public required List<ReactionModel> Reactions { get; set; }
}

[EZRestAPI.Nested("Reaction")]
public class ReactionModel
{
    [MaxLength(16)]
    public required string Emoji { get; set; }
}
```

There is no `/comments` route. You send the comments and reactions inside the post,
and read them back inside the post.

### Opt out with [Scalar]

Sometimes a property is named like a foreign key but is not one. If you have an
`int` property ending in `Id` and no matching model exists, the generator warns you
with `EZR011`. Add `[EZRestAPI.Scalar]` to say "this is just a plain number, leave
it alone." The warning goes away and no nested route is made.

```csharp
[EZRestAPI.Model("SensorReading", "SensorReadings", Endpoints = EZRestAPI.Endpoints.All)]
public partial class SensorReadingModel
{
    [EZRestAPI.Scalar]
    public required int ExternalId { get; set; }  // not a link, just a value

    public required double Value { get; set; }
    public required DateTimeOffset TakenAt { get; set; }
}
```

## Rich domain models (aggregates)

Some classes have rules to protect. A plain `[Model]` gives you a `PUT` that overwrites every
field. That is fine for simple data, but wrong when the class must guard its own state.

An aggregate fixes this. You do not overwrite it. You **create** it through one factory, and you
**change** it through named actions. Nothing else can touch its insides.

### Mark the class

Use `[EZRestAPI.Aggregate("Order", "Orders")]` (singular, plural). Add a private
parameterless constructor so EF Core can load rows from the database. Mark it `partial` so the
generator can add code to it.

```csharp
[EZRestAPI.Aggregate("Order", "Orders")]
public partial class Order
{
    private Order() { } // EF needs this
}
```

### Create it with a [Factory]

One method makes new orders. Mark it `[EZRestAPI.Factory]`. It can be a static method
(like below) **or** a public constructor. Its parameters become the request body.

```csharp
[EZRestAPI.Factory]
public static Order Place(CustomerRef customer)
{
    return new Order { Customer = customer, Status = "Placed" };
}
```

This gives you `POST /orders` which returns `201 Created`.

### Read shows read-only fields too

Fields with `private set`, get-only, or `init` still show up when you read the order. So
`Status` and the `Lines` list below are returned even though outside code cannot set them.

```csharp
public string Status { get; private set; } = "";
public IReadOnlyList<string> Lines => _lines;   // read-only view over a private list
private readonly List<string> _lines = new();
```

### Change it with [Command] actions

Each `[EZRestAPI.Command]` method becomes its own `POST` action. You can pass a route name
(`[Command("cancel")]`), or leave it blank and the method name is turned into kebab-case
(`AddLine` -> `add-line`). Parameters become the request body.

```csharp
[EZRestAPI.Command("cancel")]
public void Cancel()
{
    if (Status == "Shipped")
        throw new InvalidOperationException("Cannot cancel a shipped order.");
    Status = "Cancelled";
}

[EZRestAPI.Command]                       // route: add-line
public void AddLine(string sku, int quantity)
{
    if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
    _lines.Add($"{sku} x{quantity}");
}
```

You get:

```
POST /orders/{id}/cancel
POST /orders/{id}/add-line
```

Each one loads the saved order, runs the method, saves it, and returns `200 OK` with the
updated order. If no order has that id, you get `404`.

There is **no PUT** for aggregates. That is the whole point: you change them only through
these named actions.

### The error rule

Your methods throw normal exceptions. The generator maps them to status codes:

- Bad input (`ArgumentException` and any subclass, like `ArgumentOutOfRangeException`) -> **422**
- Wrong state (`InvalidOperationException`) -> **409**

So `AddLine` with `quantity = 0` returns 422. `Cancel` on a shipped order returns 409. Both
come back as `application/problem+json`.

### Value objects and child lists

Parts that live inside the aggregate are `[EZRestAPI.Nested]` owned types (see
[Owned data](#owned-data)). They have no routes of their own; they travel with the aggregate. A
get-only `IReadOnlyList<Child>` shows up in reads as a list of `{Child}Dto`.

```csharp
[EZRestAPI.Nested("CustomerRef")]
public class CustomerRef
{
    public required string Name { get; set; }
    public required string Email { get; set; }
}
```

### See the full examples

- `Example/Models/OrderAggregate.cs` — static-method factory, a `cancel` command, and a
  string-list projection.
- `Example/Models/InvoiceAggregate.cs` — a child-entity list (`InvoiceLine`) mapped as an
  owned collection.
- `Example/Models/ShoppingCartAggregate.cs` — a factory that is a public constructor instead
  of a static method.

## Errors

Every error comes back as `application/problem+json` (RFC 9457). The body has the same fields each time:

- `type` — a link that names the error kind.
- `title` — a short label, like `Not Found`.
- `status` — the HTTP status code.
- `detail` — a plain sentence about what went wrong.
- `code` — a short machine string: `notFound`, `conflict`, or `unprocessableEntity`.

When you send bad input you get `422` and one extra field: `errors` — a map from each bad field name to a list of messages (see the [Validation](#validation) example).

Common status codes and when you get them:

| Status | Meaning | When |
| --- | --- | --- |
| 200 OK | Success with a body | Read one, list, or run an aggregate command |
| 201 Created | Made a new thing | POST create |
| 204 No Content | Success, empty body | PUT update, DELETE |
| 404 Not Found | The thing is not there | Missing id, missing nested parent, or a scoped id that does not match |
| 409 Conflict | The action clashes with the current state | Delete a parent that still has children; an aggregate command that throws `InvalidOperationException` |
| 422 Unprocessable Entity | The request was understood but not valid | Failed validation, a bad foreign key in the body, `page`/`pageSize` below 1, or an aggregate command that throws `ArgumentException` |

## API docs (OpenAPI)

The Example app turns on OpenAPI in two lines:

```csharp
builder.Services.AddOpenApi();   // in service setup
app.MapOpenApi();                // only when in Development
```

In Development you can open `/openapi/v1.json` to get the full document. Each route is described with:

- a **tag** — the model's plural name, so routes group by resource.
- an **operation id** — a stable name like `CreateBook` or `ListBooks`, used by client generators.
- its **error responses** — every route lists the `application/problem+json` errors it can return. A `422` is documented as a validation problem so the `errors` map shows up in the schema.

## Build warnings

The generator checks your models at compile time. Most problems stop the build (Error); some are just a heads-up (Warning, Info). Fix the code and rebuild.

| Code | Level | Meaning |
| --- | --- | --- |
| EZR001 | Error | A `[Model]` class is not `partial`. |
| EZR002 | Error | Two models share the same singular name. |
| EZR003 | Error | Two models share the same plural name. |
| EZR004 | Error | A property points at another `[Model]` type; use its id, or mark it `[Nested]`. |
| EZR005 | Error | `[Nested]` classes contain each other in a loop; nesting must be a tree. |
| EZR006 | Error | Two `[Nested]` classes share the same singular name. |
| EZR007 | Error | An `Id` property is not an `int`; only `int` keys work. |
| EZR008 | Error | A name you gave is not a valid C# identifier. |
| EZR009 | Error | Nested items sit in an unsupported collection; use `List<T>`, `IList<T>`, `ICollection<T>`, `IReadOnlyList<T>`, or `IReadOnlyCollection<T>`. |
| EZR010 | Error | A class has `[Model]` plus `[Nested]` (or `[Model]` plus `[Aggregate]`); pick one. |
| EZR011 | Warning | A property looks like a foreign key (`XId`) but no `[Model]` has singular name `X`; add that model, or mark it `[Scalar]`. |
| EZR012 | Error | An `[Aggregate]` does not have exactly one `[Factory]` entry point. |
| EZR013 | Warning | `Endpoints.Create` is set without `Endpoints.Read`, so the `Create` response's `Location` header points at a `GET` route that does not exist. |
| EZR014 | Info | The model has `Endpoints.None` (the default) and generates no repository, DTOs, or routes — just the `DbSet`. If this is a surprise, add an `Endpoints` value to `[Model]`. Console builds hide Info diagnostics at the default verbosity; run with `-v detailed` to see it (an IDE shows it as usual). |
| EZR015 | Info | The model's `Endpoints` selects no verb (`List`/`Create`/`Read`/`Update`/`Delete`) — for example `Endpoints.Nested` alone. The repository, DTOs and endpoint class are still generated, but no route is, since `Nested` only switches the *form* of a verb that must also be set. Add at least one verb. |

## The Example project & tests

`Example/` is a small runnable app that uses every feature. Look at `Example/Models/` to see each one:

- `SimpleDataModel` — the smallest model.
- `AuthorModel` — many property types; and `BookModel` shows a foreign-key relationship.
- `PostModel` / `CommentModel` / `ReactionModel` — `[Nested]` owned types.
- `RegistrationModel` — validation.
- `SensorReadingModel` — `[Scalar]` to opt an id-shaped field out.
- `ReviewModel` — more than one foreign key.
- `AuditLogModel` — `Endpoints.None`: persistence with no API at all.
- `ExchangeRateModel` — `Endpoints.ReadOnly`: only the `GET` routes.
- `AuditNoteModel` — `Endpoints.Crud`: a child of `AuditLogModel` whose nested routes are deliberately left off, so it only appears at its flat, top-level path.
- `OrderAggregate`, `ShoppingCartAggregate` (constructor factory), `InvoiceAggregate` (`OwnsMany` child) — DDD aggregates.

`Example/Program.cs` is the full wiring (`AddEZRestAPI`, `MapEZRestAPI`, OpenAPI).

`Example.Tests` runs the generated API for real. Most tests boot the app against a live SQL Server, so **they need Docker** — a container starts automatically through Testcontainers (`MsSqlContainerFixture`). Run them with:

```bash
dotnet test
```

One test class, `OpenApiDocumentTests`, only reads the OpenAPI document and needs no database or Docker. The database-backed classes are marked `[Collection("MsSql")]` and share one container.

---

_Status: work in progress._
