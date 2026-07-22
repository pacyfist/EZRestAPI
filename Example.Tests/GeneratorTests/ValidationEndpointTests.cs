namespace Example.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

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
