# Contract & correctness (validation, ProblemDetails, TypedResults/OpenAPI) — design

**Date:** 2026-07-21
**Status:** Revised per best-practice research (see §0); pending implementation plan
**Scope:** One increment. Make the generated API's error contract correct and
its OpenAPI document accurate: DataAnnotations validation → **422** ProblemDetails,
ProblemDetails on every error path (422/404/409), and `TypedResults` with union
return types so OpenAPI types every response. Builds directly on the
**relationship-endpoints working tree** (nested DTOs, `PagedResponse<T>`,
`WriteResult`, conflict handling already present).

Out of scope (deferred): sorting/filtering (`?sortBy=`, `?field=`), DDD entities,
and the structural routing/pagination changes listed in §0.

## 0. Best-practice research reconciliation

A dedicated research pass (RFC 9457, RFC 9110, Zalando & Microsoft & Google-AIP
guidelines, OpenAPI 3.1, Stripe/GitHub conventions) drove the binding choices
below. Where it contradicted an earlier assumption, the research wins.

**Adopted into THIS increment:**
- **Validation failures → 422 Unprocessable Content**, not 400. 400 is reserved
  for unparseable/mis-framed requests; a well-formed body with invalid values is
  the defining 422 case, matching modern frameworks. Body stays RFC 9457 with an
  `errors` field-map. (ASP.NET defaults to 400 here; we deliberately emit 422.)
- **A write whose foreign key (in the request BODY) references a missing parent
  → 422**, not 409. It is invalid content, not a state conflict. This supersedes
  the relationship increment's 409 for flat `POST`/`PUT` bad-FK; this increment
  rewrites those handlers, so it corrects the code and the one integration test
  (`FlatPost_BadForeignKey_Returns409` → `…Returns422`).
- **Nested `POST` to a missing parent named in the PATH → 404** (unchanged — the
  path addresses a nonexistent resource).
- **Deleting a parent that still has children → 409 Conflict** (unchanged — a
  genuine state conflict).
- **Nested item not under its parent → 404** (unchanged).
- **ProblemDetails**: always `application/problem+json`; always populate `type`,
  `title`, `status`; populate a one-sentence, occurrence-specific `detail`; set
  `instance` to the request path; add a machine-readable `code` extension member
  (camelCase status phrase, Microsoft convention) so clients branch on data, not
  prose.
- **OpenAPI**: stable `operationId` per operation, `tags` per resource, and a
  single shared `ProblemDetails`/validation schema referenced by every error
  response.

**Deferred to follow-up increments (NOT this one — flagged for the maintainer):**
- **Routing depth**: research recommends the flat item URL be canonical and the
  *deep nested item path* `/authors/{id}/books/{bookId}` be dropped (nest only
  the child *collection*; cap depth at 1). The relationship increment shipped the
  deep nested item paths by explicit choice; revisiting that is a structural
  routing change, out of scope here.
- **Pagination**: research prefers cursor (keyset) as the default with offset
  opt-in, and a `{ data, pagination }` envelope. We shipped offset + `{ items,
  totalCount, page, pageSize }`; changing it is a breaking pagination redesign,
  deferred.
- **PUT**: research prefers `200 + representation`; we return `204`. 204 is
  spec-permitted, so kept for now to limit churn.
- **DDD command endpoints** (`POST /orders/{id}/cancel` for guarded transitions):
  informs the queued DDD-entities increment, designed separately.

## 1. Motivation

Today the generated endpoints return bare status codes (`Results.NotFound()`,
`Results.Conflict()`) with no bodies, do no request validation (a missing
required field or an over-long string reaches the database and surfaces as a
500), and use untyped `Results.X`, so an OpenAPI document would not describe
422/404/409 or the response bodies. This increment closes all three gaps.

## 2. Validation → 422 ProblemDetails

### Copy DataAnnotations onto DTOs
Each model property's property-level attributes in the
`System.ComponentModel.DataAnnotations` namespace (e.g. `MaxLength`, `MinLength`,
`StringLength`, `Range`, `RegularExpression`, `Required`, `EmailAddress`,
`Phone`, `Url`, `Compare`) are re-emitted verbatim on the corresponding
generated DTO properties — for the flat `Create{N}Request`/`Update{N}Request`
DTOs **and** the nested `Create{Child}Under{Parent}Request`/`Update…` DTOs.
Attributes are reconstructed from `AttributeData`: fully-qualified attribute
type name + positional constructor arguments + named arguments (literal values:
strings, numbers, bools, `typeof`, enum members).

Additionally, `[System.ComponentModel.DataAnnotations.Required]` is emitted on
each **non-nullable reference-typed** DTO property that does not already carry
it, so a missing/null JSON field yields 400 rather than a downstream 500.
(Value-typed fields such as `int` cannot be caught this way — a missing `int`
binds to `0`; this is inherent and documented.)

### Runtime validation
A generated static helper (once per assembly) validates any DTO via reflection:

```csharp
public static class EZRestAPIValidation
{
    public static System.Collections.Generic.IDictionary<string, string[]>? Validate(object request)
    {
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(request);
        var results = new System.Collections.Generic.List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (System.ComponentModel.DataAnnotations.Validator.TryValidateObject(request, context, results, validateAllProperties: true))
        {
            return null;
        }
        return results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : new[] { string.Empty })
                .Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(x => x.Member)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Invalid").ToArray());
    }
}
```

Every generated POST and PUT handler (flat and nested) calls it before touching
the repository:

```csharp
var validationErrors = EZRestAPIValidation.Validate(request);
if (validationErrors is not null)
{
    var problem = new Microsoft.AspNetCore.Http.HttpValidationProblemDetails(validationErrors)
    {
        Status = StatusCodes.Status422UnprocessableEntity,
        Title = "One or more validation errors occurred.",
        Detail = "One or more fields failed validation; see 'errors' for details.",
    };
    problem.Extensions["code"] = "unprocessableEntity";
    return TypedResults.Problem(problem);
}
```

`TypedResults.ValidationProblem` is fixed at HTTP 400, so to emit **422** we
build an `HttpValidationProblemDetails` (which carries the `errors` field-map),
set `Status = 422`, add the same `detail` + machine-readable `code` extension
every other error body carries (§3), and return it via `TypedResults.Problem(...)`
— `application/problem+json`, RFC 9457, status 422. In OpenAPI this response is
documented with `.ProducesValidationProblem(422)` (not the bare
`.ProducesProblem`) so the `errors` field-map appears in the generated schema.

**Why reflection, not `AddValidation()`:** .NET 10's built-in minimal-API
validation discovers validatable types with *its own* source generator. Roslyn
source generators cannot observe one another's generated output in the same
compilation, so the framework generator never sees our generator-produced DTOs
and would validate nothing. Reflection at runtime is independent of generator
ordering and reliably validates the copied DataAnnotations.

## 3. ProblemDetails on every error path

- `AddProblemDetails()` is added to the generated `AddEZRestAPI()` service
  registration.
- Every error body is `application/problem+json` (RFC 9457) and always carries
  `type`, `title`, `status`, a one-sentence occurrence-specific `detail`,
  `instance` (the request path), and a machine-readable `code` extension member
  (camelCase status phrase — `unprocessableEntity`, `notFound`, `conflict`).
- Status mapping (post-reconciliation, §0):
  - **422** validation failure → the `HttpValidationProblemDetails` from §2.
  - **422** body foreign key references a missing parent (flat `POST`/`PUT`) →
    `TypedResults.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "The referenced <Parent> does not exist.")`. (Supersedes the shipped 409.)
  - **404** addressed resource not found — flat item by id, nested item not
    under its parent, or nested `POST`/`GET`/`PUT`/`DELETE` whose PATH parent is
    missing → `TypedResults.Problem(statusCode: 404, title: "Not Found", detail: ...)`.
  - **409** deleting a parent that still has children →
    `TypedResults.Problem(statusCode: 409, title: "Conflict", detail: "The <Parent> has dependent <Children> and cannot be deleted.")`.
- Success responses keep their status (201/200/204) but flow through
  `TypedResults` for OpenAPI typing.

A single generated helper builds these consistently (once per assembly):

```csharp
public static class EZRestAPIProblems
{
    public static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult NotFound(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: detail,
        extensions: new System.Collections.Generic.Dictionary<string, object?> { ["code"] = "notFound" });
    public static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Conflict(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: detail,
        extensions: new System.Collections.Generic.Dictionary<string, object?> { ["code"] = "conflict" });
    public static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Unprocessable(string detail) => TypedResults.Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity, title: "Unprocessable Entity", detail: detail,
        extensions: new System.Collections.Generic.Dictionary<string, object?> { ["code"] = "unprocessableEntity" });
}
```

## 4. TypedResults + accurate OpenAPI

Handlers are rewritten from `Results.X` to `TypedResults.X` and declare explicit
union return types. Because validation, bad-FK, not-found, and conflict all
return `ProblemHttpResult` (from `TypedResults.Problem(...)`, which does **not**
encode its status code in the type), the generator attaches explicit
`.ProducesProblem(statusCode, "application/problem+json")` metadata for every
status a route can actually return, plus `.Produces` for success. Per verb
(branches by whether the model participates in relationships):

| Handler | Return type | Explicit `.ProducesProblem` |
|---|---|---|
| Create (flat, related) | `Results<Created<Create{N}Response>, ProblemHttpResult>` | 422 |
| Create (flat, unrelated) | `Results<Created<Create{N}Response>, ProblemHttpResult>` | 422 |
| Read | `Results<Ok<Read{N}Response>, ProblemHttpResult>` | 404 |
| List | `Results<Ok<PagedResponse<Read{N}Response>>, ProblemHttpResult>` | 422 (bad paging) |
| Update (related) | `Results<NoContent, ProblemHttpResult>` | 422, 404 |
| Update (unrelated) | `Results<NoContent, ProblemHttpResult>` | 422, 404 |
| Delete (parent) | `Results<NoContent, ProblemHttpResult>` | 404, 409 |
| Delete (non-parent) | `Results<NoContent, ProblemHttpResult>` | 404 |
| Nested create | `Results<Created<Create{Child}Response>, ProblemHttpResult>` | 422, 404 (missing path parent) |
| Nested list/read | `Results<Ok<…>, ProblemHttpResult>` | 404 (missing path parent / item) |
| Nested update | `Results<NoContent, ProblemHttpResult>` | 422, 404 |
| Nested delete | `Results<NoContent, ProblemHttpResult>` | 404, 409 |

OpenAPI quality (per research §10): each route keeps its stable `operationId`
via the existing `.WithName("…")`, and gains `.WithTags("{PluralName}")` so a
client generator emits one class per resource. All ProblemDetails responses
reference one shared schema (ASP.NET emits `ProblemDetails`/
`HttpValidationProblemDetails` as reusable components automatically once
`AddProblemDetails()` and the typed `.ProducesProblem` metadata are present).

The **Example** app gains `builder.Services.AddOpenApi()` and `app.MapOpenApi()`
so `GET /openapi/v1.json` renders the document.

## 5. Architecture

- `EZRestAPI/Providers/ProviderExtensions.cs`: `Property` gains
  `EquatableArray<string> DataAnnotations` (rendered attribute strings, e.g.
  `"System.ComponentModel.DataAnnotations.MaxLengthAttribute(255)"`), populated
  in `CreateProperty` from `property.GetAttributes()` filtered to the
  DataAnnotations namespace. Source locations stay out of the cached model.
- `EZRestAPI/Generators/DtoGenerator.cs`: emit copied attributes + synthesized
  `[Required]` on DTO properties (flat and nested); emit `EZRestAPIValidation`
  and `EZRestAPIProblems` once per assembly.
- `EZRestAPI/Generators/EndpointsGenerator.cs`: inline validation calls (→ 422);
  convert to `TypedResults` with union return types; use `EZRestAPIProblems` for
  422/404/409 bodies; add `.WithTags` + `.ProducesProblem(...)` metadata;
  replace the shipped flat bad-FK **409** with **422**.
- `EZRestAPI/Generators/BootstrapGenerator.cs`: add `AddProblemDetails()`.
- `Example/Program.cs`: add `AddOpenApi()` / `MapOpenApi()`.

## 6. Testing

**Fast generator tests (`EZRestAPI.Tests`, no DB):**
- DataAnnotations copied onto flat and nested DTOs (`[MaxLength(255)]` appears on `CreateBookRequest.Title` and `CreateBookUnderAuthorRequest.Title`).
- `[Required]` synthesized on non-nullable reference DTO props; absent on value types.
- `EZRestAPIValidation` and `EZRestAPIProblems` generated once.
- Endpoints emit `EZRestAPIValidation.Validate(request)` and return a 422 `HttpValidationProblemDetails` in POST/PUT.
- Endpoints emit the 422/404/409 `EZRestAPIProblems` helpers (no bare `Results.NotFound()`/`Results.Conflict()` remain).
- Endpoints carry `.WithTags("Books")` and `.ProducesProblem(...)` for each error status.
- Handler signatures contain the expected `Results<...>` unions.

**Runnable, no-Docker (`Example.Tests`, new class NOT in the "MsSql" collection):**
- Boot `WebApplicationFactory<Program>` with a dummy connection string; `GET /openapi/v1.json` → 200; assert the document describes `POST /books` with 201 and 422, `DELETE /authors/{id}` with 409, and `GET /books/{id}` with 200 and 404, and that error responses reference a ProblemDetails schema. (No endpoint is invoked, so no database is required.)

**Docker integration (`Example.Tests`, existing "MsSql" collection):**
- Update the relationship + SimpleData tests to assert error bodies are `application/problem+json` for 422/404/409, change `FlatPost_BadForeignKey_Returns409` → `…Returns422`, and add a validation case (`POST /books` with an over-long or missing `Title` → 422 with an `errors` map). These require Docker and are run by the maintainer where Docker exists.

## 7. Out of scope

Sorting/filtering, DDD entities, FluentValidation, localization of messages,
custom problem `type` URIs. Tracked for later cycles.
