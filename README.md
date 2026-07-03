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

You can then wire everything up in a minimal API:

```csharp
builder.Services.AddDbContextFactory<CustomDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Example")));

// ...

group.MapPost("/", async (
    [FromServices] SimpleDataRepository repository,
    [FromBody] CreateSimpleDataRequest data,
    CancellationToken cancellationToken) =>
{
    var id = await repository.CreateAsync(
        integerProperty: data.IntegerProperty,
        doubleProperty: data.DoubleProperty,
        stringProperty: data.StringProperty,
        dateTimeOffsetProperty: data.DateTimeOffsetProperty,
        cancellationToken);

    return Results.Ok(new CreateSimpleDataResponse()
    {
        Id = id,
        // ...
    });
});
```

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
   - [ ] Delete request
5. Controller / endpoints
   - [ ] Generate REST endpoints

## Status

This project is a work in progress — endpoint generation is not implemented yet, and the generated DTOs/repositories are still evolving.
