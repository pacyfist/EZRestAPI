namespace Example.Tests;

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Boots the app with a dummy (never-connected) connection string and reads the
/// generated OpenAPI document. No endpoint is invoked, so no database — and no
/// Docker — is required. Deliberately NOT in the "MsSql" collection.
/// </summary>
public class OpenApiDocumentTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public OpenApiDocumentTests()
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // MapOpenApi() is only wired up in the Development environment.
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Example"] =
                                "Server=localhost;Database=none;User Id=sa;Password=none;",
                        }
                    )
            );
        });

        client = factory.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task OpenApiDocument_DescribesErrorContract()
    {
        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;
        var paths = root.GetProperty("paths");

        // POST /books -> 201 (created) and 422 (validation / bad body FK); the
        // 422 body documents the RFC 9457 field-to-messages `errors` map.
        var createBook = Operation(paths, "/books", "post");
        var createResponses = createBook.GetProperty("responses");
        Assert.True(createResponses.TryGetProperty("201", out _));
        AssertValidationResponse(root, createResponses, "422");

        // DELETE /authors/{id} -> 409 (parent still has children).
        var deleteAuthor = Operation(paths, "/authors/{id}", "delete");
        AssertProblemResponse(deleteAuthor.GetProperty("responses"), "409");

        // GET /books/{id} -> 200 (found) and 404 (missing).
        var readBook = Operation(paths, "/books/{id}", "get");
        var readResponses = readBook.GetProperty("responses");
        Assert.True(readResponses.TryGetProperty("200", out _));
        AssertProblemResponse(readResponses, "404");

        // The shared ProblemDetails schema is a real component.
        Assert.True(
            root.GetProperty("components")
                .GetProperty("schemas")
                .TryGetProperty("ProblemDetails", out _)
        );
    }

    [Fact]
    public async Task OpenApiDocument_DescribesAggregateContract()
    {
        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;
        var paths = root.GetProperty("paths");

        // Factory-based creation: POST /orders -> 201 (created) and 422
        // (the request DTO failed validation).
        var createOrder = Operation(paths, "/orders", "post");
        var createResponses = createOrder.GetProperty("responses");
        Assert.True(createResponses.TryGetProperty("201", out _));
        AssertValidationResponse(root, createResponses, "422");

        // GET /orders (paginated list) and GET /orders/{id} (single read + 404).
        var listOrders = Operation(paths, "/orders", "get");
        Assert.True(listOrders.GetProperty("responses").TryGetProperty("200", out _));

        var readOrder = Operation(paths, "/orders/{id}", "get");
        var readResponses = readOrder.GetProperty("responses");
        Assert.True(readResponses.TryGetProperty("200", out _));
        AssertProblemResponse(readResponses, "404");

        // DELETE /orders/{id} -> 204 (deleted) and 404 (missing).
        var deleteOrder = Operation(paths, "/orders/{id}", "delete");
        var deleteResponses = deleteOrder.GetProperty("responses");
        Assert.True(deleteResponses.TryGetProperty("204", out _));
        AssertProblemResponse(deleteResponses, "404");

        // Guarded transitions are command sub-resources, NOT a status PUT. The
        // InvalidOperationException guard surfaces as 409; the ArgumentException
        // guard surfaces as 422.
        var cancelOrder = Operation(paths, "/orders/{id}/cancel", "post");
        AssertProblemResponse(cancelOrder.GetProperty("responses"), "409");

        var addLineOrder = Operation(paths, "/orders/{id}/add-line", "post");
        var addLineResponses = addLineOrder.GetProperty("responses");
        AssertProblemResponse(addLineResponses, "409");
        AssertValidationResponse(root, addLineResponses, "422");

        // No blind PUT for an aggregate: a full-replace would bypass invariants.
        Assert.True(paths.TryGetProperty("/orders/{id}", out var orderItem));
        Assert.False(
            orderItem.TryGetProperty("put", out _),
            "Aggregate must not expose PUT /orders/{id}."
        );
    }

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
        var addressProp = readProfile.GetProperty("properties").GetProperty("address");
        var refText = addressProp.TryGetProperty("$ref", out var r)
            ? r.GetString()
            : addressProp.GetRawText();
        Assert.Contains("Address", refText);

        foreach (var path in paths.EnumerateObject())
            Assert.DoesNotContain("/addresses", path.Name);
    }

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
        // The element must be the generated InvoiceLineDto, NOT the raw domain
        // entity Example.Models.InvoiceLine — the domain type must never leak
        // into the public read surface.
        Assert.Contains("InvoiceLineDto", linesProp.GetProperty("items").GetRawText());
        Assert.False(
            schemas.TryGetProperty("InvoiceLine", out _),
            "The domain entity InvoiceLine leaked into the OpenAPI schema set."
        );
    }

    private static JsonElement Operation(JsonElement paths, string path, string verb)
    {
        Assert.True(paths.TryGetProperty(path, out var item), $"Missing path {path}.");
        Assert.True(item.TryGetProperty(verb, out var operation), $"Missing {verb} on {path}.");
        return operation;
    }

    /// <summary>
    /// Asserts the given status exists and its body is described as
    /// application/problem+json referencing the shared ProblemDetails schema.
    /// </summary>
    private static void AssertProblemResponse(JsonElement responses, string status)
    {
        Assert.True(
            responses.TryGetProperty(status, out var response),
            $"Missing {status} response."
        );
        Assert.True(
            response.TryGetProperty("content", out var content),
            $"{status} response has no content."
        );
        Assert.True(
            content.TryGetProperty("application/problem+json", out var media),
            $"{status} response is not application/problem+json."
        );
        var reference = media.GetProperty("schema").GetProperty("$ref").GetString();
        Assert.Equal("#/components/schemas/ProblemDetails", reference);
    }

    /// <summary>
    /// Asserts a validation error status exists, is application/problem+json, and
    /// its schema (whether referenced or inline) documents the `errors` field-map
    /// — so a generated client can type the per-field validation messages.
    /// </summary>
    private static void AssertValidationResponse(
        JsonElement root,
        JsonElement responses,
        string status
    )
    {
        Assert.True(
            responses.TryGetProperty(status, out var response),
            $"Missing {status} response."
        );
        Assert.True(
            response
                .GetProperty("content")
                .TryGetProperty("application/problem+json", out var media),
            $"{status} response is not application/problem+json."
        );
        var schema = media.GetProperty("schema");
        string schemaText;
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var componentName = reference.GetString()!.Split('/')[^1];
            schemaText = root.GetProperty("components")
                .GetProperty("schemas")
                .GetProperty(componentName)
                .GetRawText();
        }
        else
        {
            schemaText = schema.GetRawText();
        }
        Assert.Contains("errors", schemaText);
    }
}
