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
