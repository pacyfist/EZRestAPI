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

## Status

This project is a work in progress — the generated API shape (DTOs, routes, repository signatures) is still evolving.
