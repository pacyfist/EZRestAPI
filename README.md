# EZRestAPI

An opinionated C# source generator framework for quickly creating CRUD APIs on top of Entity Framework Core models.

The goal: get from a single entity model to a fully working REST API by adding just one attribute.

## How it works

Annotate a `partial` class with the `[EZRestAPI.Model]` attribute:

```csharp
namespace Example.Models;

[EZRestAPI.Model("SimpleData", "SimpleDataPlural")]
public partial class SimpleDataModel
{
    public required int IntegerProperty { get; set; }

    public required double DoubleProperty { get; set; }

    public required string? StringProperty { get; set; }

    public required DateTimeOffset DateTimeOffsetProperty { get; set; }
}
```

The attribute takes a singular name and a plural name, which are used to name the generated types and `DbSet` properties. At compile time, [incremental source generators](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview) then produce:

| Generated type | Description |
|---|---|
| `SimpleDataModel` (partial) | Extends your model with a `[Key] public int Id` property |
| `CustomDbContext` | An EF Core `DbContext` with a `DbSet<T>` for every annotated model |
| `SimpleDataRepository` | Repository with `CreateAsync`, `ReadAsync`, `UpdateAsync`, `DeleteAsync` |
| `CreateSimpleDataRequest` | DTO for create requests (all model properties) |
| `CreateSimpleDataResponse` | DTO for create responses (`Id` + all model properties) |
| `ReadSimpleDataResponse` | DTO for read responses (nullable `Id` + all model properties) |
| `UpdateSimpleDataRequest` | DTO for update requests (`Id` + all model properties) |
| `SimpleDataEndpoints` | Minimal-API endpoints: CRUD routes under `/simpledataplural` |
| `EZRestAPIExtensions` | `AddEZRestAPI()` (registers all repositories) and `MapEZRestAPI()` (maps all endpoints) |

## Nested models

A model can contain other models — as a single object or a collection — to any depth. Mark aggregate parts with `[EZRestAPI.Nested]`:

```csharp
[EZRestAPI.Model("Post", "Posts")]
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

Nested classes are mapped as [EF Core owned entity types](https://learn.microsoft.com/en-us/ef/core/modeling/owned-entities) (`OwnsOne`/`OwnsMany`, configured in the generated `OnModelCreating`), and each generates a `{Name}Dto` plus a recursive entity/DTO mapper. The whole CRUD surface then works on the full graph:

- **Create** — `POST /posts` accepts the nested JSON graph and inserts it in one transaction.
- **Read** — `GET /posts/{id}` returns the graph (owned types are loaded automatically).
- **Update** — `PUT /posts/{id}` replaces the nested content; removed children are deleted as orphans.
- **Delete** — `DELETE /posts/{id}` cascades through all nested tables.

Rules: a `[Nested]` class belongs to its owner (it gets no `DbSet`, repository, or endpoints of its own), the nesting must be tree-shaped (no cycles), and a `[Model]` should reference another `[Model]` by id, not by navigation property.

Wiring up the whole API is then two calls:

```csharp
builder.Services.AddDbContextFactory<CustomDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Example")));

builder.Services.AddEZRestAPI();

var app = builder.Build();

app.MapEZRestAPI();

app.Run();
```

Which serves, for each model (plural name lowercased as the route):

| Route | Verb | Response |
|---|---|---|
| `/simpledataplural` | POST | `201 Created` + response DTO |
| `/simpledataplural/{id}` | GET | `200 OK` + response DTO, or `404` |
| `/simpledataplural/{id}` | PUT | `204 No Content`, or `404` |
| `/simpledataplural/{id}` | DELETE | `204 No Content`, or `404` |

## Relationships between models

Two top-level `[Model]`s become an association by naming convention: a property named `{Singular}Id` — where `{Singular}` is the singular name of another `[Model]` — is treated as a foreign key to that model. So a `Book` with `AuthorId` becomes a child of `Author`:

```csharp
[EZRestAPI.Model("Author", "Authors")]
public partial class AuthorModel
{
    [MaxLength(255)]
    public required string FirstName { get; set; }
}

[EZRestAPI.Model("Book", "Books")]
public partial class BookModel
{
    [MaxLength(255)]
    public required string Title { get; set; }

    public required int AuthorId { get; set; }
}
```

Detection rules:

- The property name must be `{Singular}Id` and match an existing `[Model]`'s singular name.
- The type must be `int` (required parent) or `int?` (optional parent). Any other type — for example a `Guid OrderId` with no `Order` model — stays a plain scalar and is never treated as a foreign key.
- Mark the property `[EZRestAPI.Scalar]` to opt out and force plain-scalar treatment even when the name and type would otherwise match.
- A `{X}Id` `int` property with no matching `[Model]` raises the `EZR011` **warning** (build still succeeds), steering you to create the model, fix the type, or add `[Scalar]`.

Each foreign key produces a real EF Core relationship in `OnModelCreating` with `DeleteBehavior.Restrict`, and the generator emits both flat routes and parent-scoped nested routes (plural names lowercased):

| Route | Verb | Behavior |
|---|---|---|
| `/books` | GET | Paginated list of all books |
| `/books` | POST | Flat create; `AuthorId` supplied in the body |
| `/books/{id}` | GET / PUT / DELETE | Flat item |
| `/authors/{authorId}/books` | GET | Paginated list scoped to that author |
| `/authors/{authorId}/books` | POST | Create under the author; foreign key comes from the route and is omitted from the body |
| `/authors/{authorId}/books/{bookId}` | GET / PUT / DELETE | Scoped item (verifies the book belongs to the author) |

Collections are paginated with `?page=` (1-based, default `1`) and `?pageSize=` (default `20`, capped at `100`; larger values are clamped, not rejected). `page < 1` or `pageSize < 1` returns `400`. The envelope is identical for flat and nested lists:

```json
{ "items": [ ... ], "totalCount": 57, "page": 1, "pageSize": 20 }
```

Status-code semantics:

- **Scoped item** whose foreign key does not match the route parent → `404`.
- **Nested POST** to a parent that does not exist → `404`.
- **Flat POST / PUT** with a foreign key that references no existing parent → `409 Conflict`.
- **DELETE parent that still has children** → `409 Conflict` (`RESTRICT`; the children are preserved, so reassign or delete them first). This differs from `[Nested]` owned types, which cascade.

## Solution structure

| Project | Description |
|---|---|
| `EZRestAPI` | The source generators (`netstandard2.0` Roslyn analyzer project) |
| `Example` | ASP.NET Core minimal API (`net10.0`) showing the generators in use with SQL Server |
| `Example.Tests` | xUnit integration tests running the generated repositories against a real SQL Server via [Testcontainers](https://dotnet.testcontainers.org/) |

To consume the generator, reference it as an analyzer:

```xml
<ProjectReference Include="..\EZRestAPI\EZRestAPI.csproj" OutputItemType="Analyzer" />
```

## Running the tests

The integration tests spin up a SQL Server container, so Docker must be running:

```bash
dotnet test
```

## Features

1. Model
   - [x] Generate `Id` key property
2. DbContext
   - [x] Generate `CustomDbContext` with a `DbSet` per model
3. Repository
   - [x] Create
   - [x] Read
   - [x] Update
   - [x] Delete
4. DTOs
   - [x] Create request/response
   - [x] Read response
   - [x] Update request
5. REST endpoints
   - [x] POST (create)
   - [x] GET (read)
   - [x] PUT (update)
   - [x] DELETE (delete)
6. Bootstrap
   - [x] `AddEZRestAPI()` DI registration
   - [x] `MapEZRestAPI()` route mapping
7. Nested models
   - [x] `[Nested]` aggregate parts as EF owned types
   - [x] Nested DTOs + recursive mappers
   - [x] Full-graph create/read/update/delete at any depth
8. Relationships between models
   - [x] `{Singular}Id` foreign keys by convention (`int`/`int?`), `[Scalar]` opt-out, `EZR011` warning
   - [x] `DeleteBehavior.Restrict` relationship configuration
   - [x] Flat + parent-scoped nested CRUD routes
   - [x] Paginated `PagedResponse<T>` collections
   - [x] `404`/`409` referential-integrity semantics

## Status

This project is a work in progress — the generated API shape (DTOs, routes, repository signatures) is still evolving.
