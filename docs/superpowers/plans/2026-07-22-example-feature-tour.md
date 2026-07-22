# Example Feature Tour Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand the `Example` project into a complexity-ordered tour of the generator's full feature surface, each addition proven by an OpenAPI assertion and (where runtime-bearing) a Docker integration test.

**Architecture:** Add self-contained demonstration models under `Example/Models/` (they auto-register through the generated `AddEZRestAPI`/`MapEZRestAPI` — no `Program.cs` change). Prove each at the Example level: a no-Docker OpenAPI-document assertion for shape, and a "MsSql"-collection Testcontainers test for runtime behavior. TDD at the Example level — the OpenAPI assertion is written first and fails until the model exists.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core 10, xUnit, Testcontainers SQL Server, `WebApplicationFactory<Program>`.

## Global Constraints

- Build/test env (sandbox): `export NUGET_PACKAGES="$PWD/.nuget-cache"` and `FLAGS="-p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q"` on every dotnet command.
- Gates: `dotnet test EZRestAPI.Tests $FLAGS`; `dotnet build Example/Example.csproj $FLAGS`; `dotnet build Example.Tests $FLAGS -m:1`; `dotnet test Example.Tests --filter OpenApiDocumentTests -m:1 $FLAGS`.
- No Docker in the sandbox — "MsSql" integration tests are written and compile-checked only; the maintainer runs them.
- New models need NO `Program.cs` or DI wiring — the generator's bootstrap auto-includes every `[Model]`/`[Aggregate]`.
- Models live one-per-file in `Example/Models/`, `namespace Example.Models;`, `partial` for `[Model]`/`[Aggregate]`, matching existing comment style.
- **Gap policy (green-first):** if a Tier 2–4 combination does not generate cleanly, either apply a small/obvious fix (few lines, no design change) or reduce the model to the supported subset / omit it, and record it under "Follow-ups" in `docs/superpowers/specs/2026-07-22-example-feature-tour-design.md` §7 and in memory. The build MUST end green. Never drop an edge case silently.
- Agents do not commit; the maintainer commits per tier after independent verification.
- Commit trailers: end messages with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_01Su9RuVg5kPncewfVy1sk2d`.

---

## Shared prerequisite: OpenAPI test helper

`Example.Tests/GeneratorTests/OpenApiDocumentTests.cs` currently fetches + parses the document inline inside each `[Fact]`. Add one helper so new facts don't duplicate the boilerplate; `.Clone()` detaches the root from the disposed `JsonDocument`.

- [ ] **Step P1: Add the loader helper** to `OpenApiDocumentTests.cs` (inside the class, alongside the existing `Operation`/`AssertProblemResponse`/`AssertValidationResponse` helpers):

```csharp
/// <summary>Fetches and parses /openapi/v1.json, returning a root element
/// detached from the (disposed) JsonDocument via Clone().</summary>
private async Task<JsonElement> LoadRootAsync()
{
    var response = await client.GetAsync("/openapi/v1.json");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    await using var stream = await response.Content.ReadAsStreamAsync();
    using var document = await JsonDocument.ParseAsync(stream);
    return document.RootElement.Clone();
}
```

This helper is consumed by every tier's OpenAPI fact below.

---

## Task 1: Tier 1 — validation + scalar opt-out

**Files:**
- Create: `Example/Models/RegistrationModel.cs`
- Create: `Example/Models/SensorReadingModel.cs`
- Modify: `Example.Tests/GeneratorTests/OpenApiDocumentTests.cs` (new facts + Step P1 helper)
- Create: `Example.Tests/GeneratorTests/ValidationEndpointTests.cs` (Docker)

**Interfaces:**
- Produces: models `Registration`/`Registrations`, `SensorReading`/`SensorReadings`; generated DTOs `CreateRegistrationRequest`, `ReadRegistrationResponse`, `ReadSensorReadingResponse`; routes `/registrations`, `/sensorreadings`.
- Consumes: `LoadRootAsync()` (Step P1); existing `Operation`, `AssertValidationResponse`.

- [ ] **Step 1: Write the failing OpenAPI fact** — add to `OpenApiDocumentTests.cs`:

```csharp
[Fact]
public async Task OpenApiDocument_DescribesValidationAndScalarOptOut()
{
    var root = await LoadRootAsync();
    var paths = root.GetProperty("paths");

    // Rich-validation model: POST advertises 201 and a 422 validation body.
    var createReg = Operation(paths, "/registrations", "post");
    var regResponses = createReg.GetProperty("responses");
    Assert.True(regResponses.TryGetProperty("201", out _));
    AssertValidationResponse(root, regResponses, "422");

    // [Scalar] opt-out: the flat resource exists and the FK-shaped ExternalId
    // produced NO nested route group.
    Assert.True(paths.TryGetProperty("/sensorreadings", out _), "Missing /sensorreadings.");
    foreach (var path in paths.EnumerateObject())
        Assert.DoesNotContain("/externals/", path.Name);
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `export NUGET_PACKAGES="$PWD/.nuget-cache"; FLAGS="-p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q"; dotnet test Example.Tests --filter OpenApiDocumentTests -m:1 $FLAGS`
Expected: FAIL — `Missing path /registrations`.

- [ ] **Step 3: Create `Example/Models/RegistrationModel.cs`**

```csharp
namespace Example.Models;

using System.ComponentModel.DataAnnotations;

// Rich validation annotations beyond [MaxLength]: each is copied onto the
// generated CreateRegistrationRequest and enforced by the validation filter,
// so a bad field yields a 422 problem+json with an RFC 9457 `errors` map.
[EZRestAPI.Model("Registration", "Registrations")]
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

    [RegularExpression(@"^\+?[0-9]{7,15}$")]
    public string? PhoneNumber { get; set; }

    [Url]
    public string? Website { get; set; }
}
```

- [ ] **Step 4: Create `Example/Models/SensorReadingModel.cs`**

```csharp
namespace Example.Models;

// ExternalId matches the {Singular}Id foreign-key convention (int, "…Id") but
// there is no `External` model, so without an opt-out it would raise EZR011.
// [EZRestAPI.Scalar] keeps it a plain scalar column and suppresses the warning,
// generating NO nested route.
[EZRestAPI.Model("SensorReading", "SensorReadings")]
public partial class SensorReadingModel
{
    [EZRestAPI.Scalar]
    public required int ExternalId { get; set; }

    public required double Value { get; set; }

    public required DateTimeOffset TakenAt { get; set; }
}
```

- [ ] **Step 5: Verify Example compiles and the fact passes**

Run: `dotnet build Example/Example.csproj $FLAGS` → Expected: `Build succeeded`.
Run: `dotnet test Example.Tests --filter OpenApiDocumentTests -m:1 $FLAGS` → Expected: PASS.
Run: `dotnet test EZRestAPI.Tests $FLAGS` → Expected: unchanged pass count (102/102). If EZR011 fires for `ExternalId`, the `[Scalar]` opt-out is not being honored — that is a real generator bug; apply a small fix or, per gap policy, log it.

- [ ] **Step 6: Write the Docker integration test** `Example.Tests/GeneratorTests/ValidationEndpointTests.cs`

```csharp
namespace Example.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

/// <summary>Runtime validation behavior for the Registration model. Needs SQL
/// Server, so it runs in the shared "MsSql" collection (Docker only).</summary>
[Collection("MsSql")]
public class ValidationEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public ValidationEndpointTests(MsSqlContainerFixture fixture)
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Example"] = fixture.ConnectionString,
                        }
                    )
            )
        );
        client = factory.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task Registration_InvalidEmailAndAge_Returns422WithFieldErrors()
    {
        var response = await client.PostAsJsonAsync(
            "/registrations",
            new
            {
                Username = "ab", // too short (min 3)
                Email = "not-an-email",
                Age = 5, // below Range(18,120)
            }
        );

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType
        );

        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var errors = body!.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Email", out _));
        Assert.True(errors.TryGetProperty("Age", out _));
        Assert.True(errors.TryGetProperty("Username", out _));
    }

    [Fact]
    public async Task Registration_Valid_Returns201()
    {
        var response = await client.PostAsJsonAsync(
            "/registrations",
            new
            {
                Username = "ada",
                Email = "ada@example.com",
                Age = 36,
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
```

Add `using System.Text.Json;` if not already present for `JsonDocument`.

- [ ] **Step 7: Compile the test project**

Run: `dotnet build Example.Tests $FLAGS -m:1` → Expected: `0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add Example/Models/RegistrationModel.cs Example/Models/SensorReadingModel.cs \
        Example.Tests/GeneratorTests/OpenApiDocumentTests.cs \
        Example.Tests/GeneratorTests/ValidationEndpointTests.cs
git commit -m "Example tour Tier 1: rich validation + [Scalar] opt-out."
```

---

## Task 2: Tier 2 — multi-FK child + 3-level relationship chain

**Files:**
- Create: `Example/Models/ReviewModel.cs`
- Create: `Example/Models/ChapterModel.cs`
- Modify: `Example.Tests/GeneratorTests/OpenApiDocumentTests.cs` (new fact)
- Create: `Example.Tests/GeneratorTests/RelationshipTourEndpointTests.cs` (Docker)

**Interfaces:**
- Consumes: existing `AuthorModel` (`/authors`), `BookModel` (`/books`, has `AuthorId`).
- Produces: `Review`/`Reviews` (FKs `AuthorId`, `BookId`), `Chapter`/`Chapters` (FK `BookId`); routes `/reviews`, `/authors/{id}/reviews`, `/books/{id}/reviews`, `/chapters`, `/books/{id}/chapters`.

- [ ] **Step 1: Write the failing OpenAPI fact** — add to `OpenApiDocumentTests.cs`:

```csharp
[Fact]
public async Task OpenApiDocument_DescribesMultiParentAndChainedRelationships()
{
    var root = await LoadRootAsync();
    var paths = root.GetProperty("paths");

    // Review has two parents -> nested collection under each, plus the flat route.
    Assert.True(paths.TryGetProperty("/reviews", out _), "Missing flat /reviews.");
    Assert.True(
        paths.TryGetProperty("/authors/{parentId}/reviews", out _),
        "Missing /authors/{parentId}/reviews."
    );
    Assert.True(
        paths.TryGetProperty("/books/{parentId}/reviews", out _),
        "Missing /books/{parentId}/reviews."
    );

    // Chapter hangs off Book, which itself hangs off Author (3-level chain).
    Assert.True(
        paths.TryGetProperty("/books/{parentId}/chapters", out _),
        "Missing /books/{parentId}/chapters."
    );
}
```

> Note: the nested-group path template is `/{parentPlural}/{parentId}/{childPlural}`. If the generator emits a typed constraint (e.g. `{parentId:int}`) in the OpenAPI path key, adjust the expected strings to match the actual document — inspect a generated path key first (`Operation`/`EnumerateObject`) and use the real form. This is a string-shape match, not a behavior change.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test Example.Tests --filter OpenApiDocumentTests -m:1 $FLAGS`
Expected: FAIL — `Missing flat /reviews`.

- [ ] **Step 3: Create `Example/Models/ReviewModel.cs`**

```csharp
namespace Example.Models;

using System.ComponentModel.DataAnnotations;

// Two foreign keys on one child: Review nests under BOTH /authors/{id}/reviews
// and /books/{id}/reviews, and is addressable flat at /reviews.
[EZRestAPI.Model("Review", "Reviews")]
public partial class ReviewModel
{
    [MaxLength(2048)]
    public required string Body { get; set; }

    public required int AuthorId { get; set; }

    public required int BookId { get; set; }
}
```

- [ ] **Step 4: Create `Example/Models/ChapterModel.cs`**

```csharp
namespace Example.Models;

using System.ComponentModel.DataAnnotations;

// Third level of a chain: Author -> Book -> Chapter. Chapter's parent (Book) is
// itself a child of Author.
[EZRestAPI.Model("Chapter", "Chapters")]
public partial class ChapterModel
{
    [MaxLength(255)]
    public required string Heading { get; set; }

    public required int BookId { get; set; }
}
```

- [ ] **Step 5: Verify Example compiles and the fact passes**

Run: `dotnet build Example/Example.csproj $FLAGS` → Expected: `Build succeeded`.
Run: `dotnet test Example.Tests --filter OpenApiDocumentTests -m:1 $FLAGS` → Expected: PASS (after adjusting path strings per the Step 1 note if needed).
**Gap check:** if a two-FK child fails to generate (e.g. duplicate route-group collision, or only one parent group emitted), apply a small fix if obvious; otherwise reduce `ReviewModel` to a single FK (`BookId`) to keep green and log "multi-FK child not supported" under Follow-ups. Same for the chain if `/books/{id}/chapters` doesn't emit.

- [ ] **Step 6: Write the Docker integration test** `Example.Tests/GeneratorTests/RelationshipTourEndpointTests.cs`

```csharp
namespace Example.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

/// <summary>Runtime checks for a multi-parent child (Review) and a 3-level
/// chain (Author->Book->Chapter). Docker-only ("MsSql").</summary>
[Collection("MsSql")]
public class RelationshipTourEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public RelationshipTourEndpointTests(MsSqlContainerFixture fixture)
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Example"] = fixture.ConnectionString,
                        }
                    )
            )
        );
        client = factory.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private async Task<int> CreateAuthorAsync()
    {
        var response = await client.PostAsJsonAsync(
            "/authors",
            new
            {
                FirstName = "Ada",
                Biography = "Pioneer",
                ProfilePicture = Array.Empty<byte>(),
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return body!.RootElement.GetProperty("id").GetInt32();
    }

    private async Task<int> CreateBookAsync(int authorId)
    {
        var response = await client.PostAsJsonAsync(
            "/books",
            new { Title = "EF Core", AuthorId = authorId }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return body!.RootElement.GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Review_CreatesUnderBothParents_AndListsThere()
    {
        var authorId = await CreateAuthorAsync();
        var bookId = await CreateBookAsync(authorId);

        var create = await client.PostAsJsonAsync(
            "/reviews",
            new
            {
                Body = "Great",
                AuthorId = authorId,
                BookId = bookId,
            }
        );
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var underAuthor = await client.GetAsync($"/authors/{authorId}/reviews");
        Assert.Equal(HttpStatusCode.OK, underAuthor.StatusCode);
        var underBook = await client.GetAsync($"/books/{bookId}/reviews");
        Assert.Equal(HttpStatusCode.OK, underBook.StatusCode);
    }

    [Fact]
    public async Task Chapter_CreatesUnderBook_ThreeLevelChain()
    {
        var authorId = await CreateAuthorAsync();
        var bookId = await CreateBookAsync(authorId);

        var create = await client.PostAsJsonAsync(
            "/chapters",
            new { Heading = "Intro", BookId = bookId }
        );
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var underBook = await client.GetAsync($"/books/{bookId}/chapters");
        Assert.Equal(HttpStatusCode.OK, underBook.StatusCode);
    }
}
```

> If Tier-2 generation was reduced under the gap policy, trim the corresponding scenarios and note it in the test's XML comment.

- [ ] **Step 7: Compile the test project**

Run: `dotnet build Example.Tests $FLAGS -m:1` → Expected: `0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add Example/Models/ReviewModel.cs Example/Models/ChapterModel.cs \
        Example.Tests/GeneratorTests/OpenApiDocumentTests.cs \
        Example.Tests/GeneratorTests/RelationshipTourEndpointTests.cs
git commit -m "Example tour Tier 2: multi-FK child + 3-level relationship chain."
```

---

## Task 3: Tier 3 — single owned reference (OwnsOne) on an anemic model

**Files:**
- Create: `Example/Models/ProfileModel.cs` (contains `ProfileModel` + nested `AddressModel`)
- Modify: `Example.Tests/GeneratorTests/OpenApiDocumentTests.cs` (new fact)
- Create: `Example.Tests/GeneratorTests/OwnedReferenceEndpointTests.cs` (Docker)

**Interfaces:**
- Produces: `Profile`/`Profiles`; nested `Address` → `AddressDto`; `ReadProfileResponse` embeds an `AddressDto Address`; route `/profiles`.
- Consumes: `LoadRootAsync()`.

- [ ] **Step 1: Write the failing OpenAPI fact** — add to `OpenApiDocumentTests.cs`:

```csharp
[Fact]
public async Task OpenApiDocument_DescribesSingleOwnedReference()
{
    var root = await LoadRootAsync();
    var paths = root.GetProperty("paths");

    Assert.True(paths.TryGetProperty("/profiles", out _), "Missing /profiles.");

    // The Profile read schema embeds an AddressDto (single owned reference),
    // and Address is NOT independently routed.
    var schemas = root.GetProperty("components").GetProperty("schemas");
    Assert.True(schemas.TryGetProperty("ReadProfileResponse", out var readProfile));
    var addressProp = readProfile
        .GetProperty("properties")
        .GetProperty("address");
    var refText = addressProp.TryGetProperty("$ref", out var r)
        ? r.GetString()
        : addressProp.GetRawText();
    Assert.Contains("Address", refText);

    foreach (var path in paths.EnumerateObject())
        Assert.DoesNotContain("/addresses", path.Name);
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test Example.Tests --filter OpenApiDocumentTests -m:1 $FLAGS`
Expected: FAIL — `Missing /profiles`.

- [ ] **Step 3: Create `Example/Models/ProfileModel.cs`**

```csharp
namespace Example.Models;

using System.ComponentModel.DataAnnotations;

// A single (non-collection) owned reference: Address maps as OwnsOne in the
// anemic [Model] path, contrasting Post's owned *collection* of Comments.
[EZRestAPI.Nested("Address")]
public class AddressModel
{
    [MaxLength(255)]
    public required string Street { get; set; }

    [MaxLength(128)]
    public required string City { get; set; }

    [MaxLength(16)]
    public required string PostalCode { get; set; }
}

[EZRestAPI.Model("Profile", "Profiles")]
public partial class ProfileModel
{
    [MaxLength(128)]
    public required string DisplayName { get; set; }

    public required AddressModel Address { get; set; }
}
```

- [ ] **Step 4: Verify Example compiles and the fact passes**

Run: `dotnet build Example/Example.csproj $FLAGS` → Expected: `Build succeeded`.
Run: `dotnet test Example.Tests --filter OpenApiDocumentTests -m:1 $FLAGS` → Expected: PASS.
**Gap check:** if the anemic `[Model]` path only supports `[Nested]` *collections* (like `List<CommentModel>`) and chokes on a single reference, apply a small fix if obvious; otherwise change `Address` to `List<AddressModel> Addresses` (owned collection, already supported), keep green, and log "single owned reference (OwnsOne) on anemic model not supported" under Follow-ups. Update the fact's property name accordingly (`addresses`).

- [ ] **Step 5: Write the Docker integration test** `Example.Tests/GeneratorTests/OwnedReferenceEndpointTests.cs`

```csharp
namespace Example.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

/// <summary>Runtime check that a single owned reference (Profile.Address)
/// round-trips through create and read. Docker-only ("MsSql").</summary>
[Collection("MsSql")]
public class OwnedReferenceEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public OwnedReferenceEndpointTests(MsSqlContainerFixture fixture)
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Example"] = fixture.ConnectionString,
                        }
                    )
            )
        );
        client = factory.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task Profile_CreateWithAddress_ThenReadEmbedsIt()
    {
        var create = await client.PostAsJsonAsync(
            "/profiles",
            new
            {
                DisplayName = "Ada",
                Address = new
                {
                    Street = "1 Analytical Way",
                    City = "London",
                    PostalCode = "EC1",
                },
            }
        );
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var created = await create.Content.ReadFromJsonAsync<JsonDocument>();
        var id = created!.RootElement.GetProperty("id").GetInt32();

        using var readResponse = await (await client.GetAsync($"/profiles/{id}"))
            .Content.ReadFromJsonAsync<JsonDocument>();
        var address = readResponse!.RootElement.GetProperty("address");
        Assert.Equal("London", address.GetProperty("city").GetString());
    }
}
```

- [ ] **Step 6: Compile the test project**

Run: `dotnet build Example.Tests $FLAGS -m:1` → Expected: `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add Example/Models/ProfileModel.cs \
        Example.Tests/GeneratorTests/OpenApiDocumentTests.cs \
        Example.Tests/GeneratorTests/OwnedReferenceEndpointTests.cs
git commit -m "Example tour Tier 3: single owned reference (OwnsOne) on a model."
```

---

## Task 4: Tier 4 — constructor factory + OwnsMany child-entity aggregate

**Files:**
- Create: `Example/Models/ShoppingCartAggregate.cs`
- Create: `Example/Models/InvoiceAggregate.cs`
- Modify: `Example.Tests/GeneratorTests/OpenApiDocumentTests.cs` (new fact)
- Create: `Example.Tests/GeneratorTests/AggregateTourEndpointTests.cs` (Docker)

**Interfaces:**
- Consumes: existing `CustomerRef` value object (`[Nested("CustomerRef")]`, in `OrderAggregate.cs`, namespace `Example.Models`).
- Produces: `ShoppingCart`/`ShoppingCarts` (constructor factory, `Checkout` command); `Invoice`/`Invoices` (static factory `Open`, `AddLine` command, `IReadOnlyList<InvoiceLine>` owned child collection); DTOs `CreateShoppingCartRequest`, `CreateInvoiceRequest`, `AddLineInvoiceRequest`, `ReadInvoiceResponse` (embeds `InvoiceLineDto` list); routes `/shoppingcarts`, `/shoppingcarts/{id}/checkout`, `/invoices`, `/invoices/{id}/add-line`.

- [ ] **Step 1: Write the failing OpenAPI fact** — add to `OpenApiDocumentTests.cs`:

```csharp
[Fact]
public async Task OpenApiDocument_DescribesConstructorFactoryAndOwnedChildCollection()
{
    var root = await LoadRootAsync();
    var paths = root.GetProperty("paths");

    // Constructor-factory aggregate: POST create + a command sub-resource, no PUT.
    var createCart = Operation(paths, "/shoppingcarts", "post");
    Assert.True(createCart.GetProperty("responses").TryGetProperty("201", out _));
    Assert.True(
        paths.TryGetProperty("/shoppingcarts/{id}/checkout", out _),
        "Missing /shoppingcarts/{id}/checkout."
    );
    Assert.False(
        paths.TryGetProperty("/shoppingcarts/{id}", out var cartItem)
            && cartItem.TryGetProperty("put", out _),
        "Aggregate must not expose PUT."
    );

    // OwnsMany child-entity collection: the Invoice read embeds InvoiceLineDto.
    Assert.True(
        paths.TryGetProperty("/invoices/{id}/add-line", out _),
        "Missing /invoices/{id}/add-line."
    );
    var schemas = root.GetProperty("components").GetProperty("schemas");
    Assert.True(schemas.TryGetProperty("ReadInvoiceResponse", out var readInvoice));
    var linesProp = readInvoice.GetProperty("properties").GetProperty("lines");
    Assert.Equal("array", linesProp.GetProperty("type").GetString());
    Assert.Contains("InvoiceLine", linesProp.GetProperty("items").GetRawText());
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test Example.Tests --filter OpenApiDocumentTests -m:1 $FLAGS`
Expected: FAIL — `Missing path /shoppingcarts`.

- [ ] **Step 3: Create `Example/Models/ShoppingCartAggregate.cs`**

```csharp
namespace Example.Models;

// An aggregate whose creation entry point is a PUBLIC CONSTRUCTOR marked
// [Factory] (exercising the `new Type(args)` creation path), contrasting
// Order's static-method factory. Reuses the CustomerRef value object.
[EZRestAPI.Aggregate("ShoppingCart", "ShoppingCarts")]
public partial class ShoppingCart
{
    private ShoppingCart() { } // EF materialization ctor

    [EZRestAPI.Factory]
    public ShoppingCart(CustomerRef owner)
    {
        Owner = owner;
        Status = "Open";
    }

    public CustomerRef Owner { get; private set; } = null!;

    public string Status { get; private set; } = "";

    [EZRestAPI.Command]
    public void Checkout()
    {
        if (Status == "CheckedOut")
        {
            throw new System.InvalidOperationException("Cart is already checked out.");
        }

        Status = "CheckedOut";
    }
}
```

- [ ] **Step 4: Create `Example/Models/InvoiceAggregate.cs`**

```csharp
namespace Example.Models;

using System.Collections.Generic;

// A child ENTITY (not a value object): mapped as an OwnsMany owned type inside
// the Invoice aggregate and embedded in reads as InvoiceLineDto.
[EZRestAPI.Nested("InvoiceLine")]
public class InvoiceLine
{
    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public required string Sku { get; set; }

    public required int Quantity { get; set; }

    public required decimal UnitPrice { get; set; }
}

// An aggregate with an OwnsMany child-entity collection (IReadOnlyList over a
// private backing field), contrasting Order's IReadOnlyList<string> projection.
[EZRestAPI.Aggregate("Invoice", "Invoices")]
public partial class Invoice
{
    private Invoice() { } // EF materialization ctor

    [EZRestAPI.Factory]
    public static Invoice Open(CustomerRef billTo)
    {
        return new Invoice { BillTo = billTo, Status = "Open" };
    }

    public CustomerRef BillTo { get; private set; } = null!;

    public string Status { get; private set; } = "";

    public IReadOnlyList<InvoiceLine> Lines => _lines;

    private readonly List<InvoiceLine> _lines = new();

    [EZRestAPI.Command]
    public void AddLine(string sku, int quantity, decimal unitPrice)
    {
        if (quantity <= 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(quantity));
        }

        _lines.Add(
            new InvoiceLine
            {
                Sku = sku,
                Quantity = quantity,
                UnitPrice = unitPrice,
            }
        );
    }
}
```

- [ ] **Step 5: Verify Example compiles and the fact passes**

Run: `dotnet build Example/Example.csproj $FLAGS` → Expected: `Build succeeded`.
Run: `dotnet test Example.Tests --filter OpenApiDocumentTests -m:1 $FLAGS` → Expected: PASS.
Run: `dotnet test EZRestAPI.Tests $FLAGS` → Expected: still green.
**Gap check (two independent probes):**
- *Constructor factory:* if `EZR012` fires or no POST is generated for `ShoppingCart`, the constructor-`[Factory]` path is unsupported — apply a small fix if obvious, else switch `ShoppingCart` to a static `Create` factory to keep green and log "constructor [Factory] not supported" under Follow-ups.
- *OwnsMany child entity on aggregate:* if `ReadInvoiceResponse.Lines` isn't emitted as an `InvoiceLineDto` array (or `Example` fails to compile the mapper), apply a small fix if obvious, else reduce `Invoice.Lines` to `IReadOnlyList<string>` (proven, like Order) and log "OwnsMany child-entity collection on aggregate not supported" under Follow-ups; update the fact accordingly.

- [ ] **Step 6: Write the Docker integration test** `Example.Tests/GeneratorTests/AggregateTourEndpointTests.cs`

```csharp
namespace Example.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

/// <summary>Runtime checks for the constructor-factory ShoppingCart and the
/// OwnsMany-child-collection Invoice aggregate. Docker-only ("MsSql").</summary>
[Collection("MsSql")]
public class AggregateTourEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public AggregateTourEndpointTests(MsSqlContainerFixture fixture)
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Example"] = fixture.ConnectionString,
                        }
                    )
            )
        );
        client = factory.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    private static readonly object Owner = new { Name = "Ada", Email = "ada@example.com" };

    [Fact]
    public async Task ShoppingCart_ConstructorFactoryCreate_ThenCheckout()
    {
        var create = await client.PostAsJsonAsync("/shoppingcarts", new { Owner });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = await create.Content.ReadFromJsonAsync<JsonDocument>();
        var id = created!.RootElement.GetProperty("id").GetInt32();

        var checkout = await client.PostAsync($"/shoppingcarts/{id}/checkout", content: null);
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);

        var second = await client.PostAsync($"/shoppingcarts/{id}/checkout", content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Invoice_AddLine_ThenReadShowsChildCollection()
    {
        var create = await client.PostAsJsonAsync("/invoices", new { BillTo = Owner });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = await create.Content.ReadFromJsonAsync<JsonDocument>();
        var id = created!.RootElement.GetProperty("id").GetInt32();

        var addLine = await client.PostAsJsonAsync(
            $"/invoices/{id}/add-line",
            new
            {
                Sku = "WIDGET-1",
                Quantity = 2,
                UnitPrice = 9.99m,
            }
        );
        Assert.Equal(HttpStatusCode.OK, addLine.StatusCode);

        using var read = await (await client.GetAsync($"/invoices/{id}"))
            .Content.ReadFromJsonAsync<JsonDocument>();
        var lines = read!.RootElement.GetProperty("lines");
        Assert.Equal(1, lines.GetArrayLength());
        Assert.Equal("WIDGET-1", lines[0].GetProperty("sku").GetString());
        Assert.Equal(2, lines[0].GetProperty("quantity").GetInt32());
    }
}
```

> If either Tier-4 probe was reduced under the gap policy, trim the matching scenario and note it in the test's XML comment.

- [ ] **Step 7: Compile the test project**

Run: `dotnet build Example.Tests $FLAGS -m:1` → Expected: `0 Error(s)`.

- [ ] **Step 8: Commit**

```bash
git add Example/Models/ShoppingCartAggregate.cs Example/Models/InvoiceAggregate.cs \
        Example.Tests/GeneratorTests/OpenApiDocumentTests.cs \
        Example.Tests/GeneratorTests/AggregateTourEndpointTests.cs
git commit -m "Example tour Tier 4: constructor factory + OwnsMany child aggregate."
```

---

## Task 5: Coverage review + follow-ups

**Files:**
- Modify: `docs/superpowers/specs/2026-07-22-example-feature-tour-design.md` (§7 Follow-ups, if any gaps fired)
- Modify: memory `ezrestapi-vision-and-progress` (mark the tour done; list any follow-ups)

- [ ] **Step 1: Re-run every gate from a clean state**

```bash
export NUGET_PACKAGES="$PWD/.nuget-cache"
FLAGS="-p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q"
dotnet test  EZRestAPI.Tests $FLAGS
dotnet build Example/Example.csproj $FLAGS
dotnet build Example.Tests $FLAGS -m:1
dotnet test  Example.Tests --filter OpenApiDocumentTests -m:1 $FLAGS
```
Expected: fast tests green; Example builds; Example.Tests builds; OpenAPI facts all pass.

- [ ] **Step 2: Audit coverage** — confirm each spec §2 model exists and each has (a) an OpenAPI fact and (b) a Docker test (or a documented gap-policy reduction). List anything missing.

- [ ] **Step 3: Record follow-ups** — for every gap-policy reduction, write a bullet under spec §7 (what combination, what the model was reduced to, why) and mirror a one-line note in memory. If no gaps fired, state "no unsupported combinations found."

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-07-22-example-feature-tour-design.md
git commit -m "Example tour: record coverage + any unsupported-combination follow-ups."
```

---

## Self-Review (completed)

- **Spec coverage:** every spec §2 model → a task (Tier 1 → Task 1, Tier 2 → Task 2, Tier 3 → Task 3, Tier 4 → Task 4); spec §3 tests → each task's OpenAPI fact + Docker test; spec §5 gap policy → per-task "Gap check" steps + Task 5 §7 capture; spec §0/§7 → Task 5.
- **Placeholder scan:** no TBD/TODO; all model and test code is complete; the only conditional text is the explicit gap-policy branches (real instructions with concrete fallbacks), not placeholders.
- **Type consistency:** route/DTO names are consistent across facts and tests (`/registrations`, `/reviews`, `/authors/{parentId}/reviews`, `/profiles` + `AddressDto`/`ReadProfileResponse`, `/shoppingcarts` + `/shoppingcarts/{id}/checkout`, `/invoices` + `/invoices/{id}/add-line` + `ReadInvoiceResponse.lines` → `InvoiceLineDto`). `CustomerRef` reused from `OrderAggregate.cs`. The nested path-template exact string is flagged (Task 2 Step 1 note) to match the real document form before asserting.
