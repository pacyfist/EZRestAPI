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
