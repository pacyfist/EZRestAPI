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

        // POST /books -> 201 (created) and 422 (validation / bad body FK).
        var createBook = Operation(paths, "/books", "post");
        var createResponses = createBook.GetProperty("responses");
        Assert.True(createResponses.TryGetProperty("201", out _));
        AssertProblemResponse(createResponses, "422");

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
        Assert.True(responses.TryGetProperty(status, out var response), $"Missing {status} response.");
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
}
