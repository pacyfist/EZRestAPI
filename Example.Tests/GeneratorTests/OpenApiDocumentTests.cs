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
