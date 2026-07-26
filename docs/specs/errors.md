# Errors and OpenAPI

## Every error is `problem+json`

Every error response is `application/problem+json` (RFC 9457) and always carries:

| Field | Meaning |
| --- | --- |
| `type` | A URI naming the kind of error |
| `title` | A short label, e.g. `Not Found` |
| `status` | The HTTP status code |
| `detail` | One sentence about this occurrence |
| `instance` | The request path |
| `code` | A machine-readable string: `notFound`, `conflict`, `unprocessableEntity` |

`code` exists so clients branch on data instead of parsing prose.

A `422` from validation carries one more field, `errors`: a map from each bad
field name to its list of messages.

```json
{
  "title": "One or more validation errors occurred.",
  "status": 422,
  "detail": "One or more fields failed validation; see 'errors' for details.",
  "code": "unprocessableEntity",
  "errors": {
    "Username": ["...must be a string with a minimum length of 3..."],
    "Email": ["The Email field is not a valid e-mail address."]
  }
}
```

Bodies are built by one generated helper per assembly, `EZRestAPIProblems`, with
`NotFound`, `Conflict` and `Unprocessable`. Handlers never write a bare
`Results.NotFound()`.

`AddEZRestAPI()` calls `AddProblemDetails()`, so unhandled framework errors use
the same shape.

## Status codes

| Status | When |
| --- | --- |
| `200 OK` | Read one, list, or run an aggregate command |
| `201 Created` | `POST` create. `Location` is always the flat item URL |
| `204 No Content` | `PUT` update, `DELETE` |
| `404 Not Found` | Missing id; missing parent named in the path; an item that does not belong to the parent in the path |
| `409 Conflict` | Deleting a parent that still has children; an aggregate command that throws `InvalidOperationException` |
| `422 Unprocessable Entity` | Failed validation; a foreign key in the **body** that does not exist; `page` or `pageSize` below 1; an aggregate command that throws `ArgumentException` |

Two distinctions carry most of this:

- **Body versus path.** A bad foreign key in a request body is invalid content
  (`422`). A bad id in the path is a resource that is not there (`404`).
- **Content versus state.** `422` means the request was understood and rejected.
  `409` means the request was fine but the current state refuses it.

`400` is left to the framework, for a body it cannot parse at all.

## Why 422 and not 400

RFC 9110 §15.5.1 scopes `400` to requests the server cannot process at the
syntax level. §15.5.21 defines `422` as well-formed content the server will not
accept — which is exactly a failed validation attribute. Rails, Laravel and
FastAPI all default to `422`. ASP.NET Core defaults to `400`, a defensible
dissent, so making the status configurable is a reasonable future option.

`TypedResults.ValidationProblem` is hard-wired to `400`, so the generator builds
an `HttpValidationProblemDetails` (which carries the `errors` map), sets
`Status = 422`, adds `detail` and `code`, and returns it through
`TypedResults.Problem(...)`.

## OpenAPI

Handlers use `TypedResults` and declare explicit union return types, so the
document describes real types rather than `object`:

```csharp
Results<Ok<ReadBookResponse>, ProblemHttpResult>
```

`ProblemHttpResult` does not encode its status code in the type, so each route
also declares the statuses it can actually return:

| Handler | Declared problem statuses |
| --- | --- |
| Create | 422 |
| Read | 404 |
| List | 422 |
| Update | 422, 404 |
| Delete (has children) | 404, 409 |
| Delete (no children) | 404 |
| Nested create | 422, 404 |
| Nested list / read | 404 |
| Nested update | 422, 404 |
| Nested delete | 404, 409 |
| Aggregate command | 404, 409, 422 |

Every route also carries:

- `.WithName(...)` — a stable `operationId` like `CreateBook`, so client
  generators produce predictable method names.
- `.WithTags("{Plural}")` — so routes group by resource, one class per resource.

Validation responses use `.ProducesValidationProblem(422)` rather than the plain
`.ProducesProblem`, so the `errors` map appears in the schema. ASP.NET emits
`ProblemDetails` and `HttpValidationProblemDetails` as shared components
automatically.

Turn the document on with `AddOpenApi()` and `MapOpenApi()`; it is served at
`/openapi/v1.json`.
