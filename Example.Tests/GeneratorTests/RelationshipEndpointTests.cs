namespace Example.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

[Collection("MsSql")]
public class RelationshipEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public RelationshipEndpointTests(MsSqlContainerFixture fixture)
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

    private async Task<int> CreateAuthorAsync(string firstName = "Author")
    {
        var response = await client.PostAsJsonAsync(
            "/authors",
            new
            {
                FirstName = firstName,
                Biography = "Bio",
                ProfilePicture = new byte[] { 1, 2, 3 },
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<CreateAuthorResponse>();
        Assert.NotNull(created);
        return created.Id;
    }

    [Fact]
    public async Task Author_Book_FlatAndNestedCrud_RoundTrip()
    {
        var authorId = await CreateAuthorAsync();

        var createResponse = await client.PostAsJsonAsync(
            $"/authors/{authorId}/books",
            new { Title = "A" }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdBook = await createResponse.Content.ReadFromJsonAsync<CreateBookResponse>();
        Assert.NotNull(createdBook);
        Assert.Equal("A", createdBook.Title);
        Assert.Equal(authorId, createdBook.AuthorId);
        var bookId = createdBook.Id;

        var page = await client.GetFromJsonAsync<PagedResponse<ReadBookResponse>>(
            $"/authors/{authorId}/books?page=1&pageSize=20"
        );
        Assert.NotNull(page);
        Assert.True(page.TotalCount >= 1);
        Assert.Contains(page.Items, b => b.Title == "A");

        var flat = await client.GetFromJsonAsync<ReadBookResponse>($"/books/{bookId}");
        Assert.NotNull(flat);
        Assert.Equal("A", flat.Title);
        Assert.Equal(authorId, flat.AuthorId);

        var updateResponse = await client.PutAsJsonAsync(
            $"/authors/{authorId}/books/{bookId}",
            new { Title = "B" }
        );
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var reread = await client.GetFromJsonAsync<ReadBookResponse>(
            $"/authors/{authorId}/books/{bookId}"
        );
        Assert.NotNull(reread);
        Assert.Equal("B", reread.Title);

        var deleteResponse = await client.DeleteAsync($"/authors/{authorId}/books/{bookId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var notFound = await client.GetAsync($"/books/{bookId}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task NestedItem_WrongParent_Returns404()
    {
        var authorOne = await CreateAuthorAsync("One");
        var authorTwo = await CreateAuthorAsync("Two");

        var createResponse = await client.PostAsJsonAsync(
            $"/authors/{authorOne}/books",
            new { Title = "Scoped" }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createdBook = await createResponse.Content.ReadFromJsonAsync<CreateBookResponse>();
        Assert.NotNull(createdBook);

        var response = await client.GetAsync($"/authors/{authorTwo}/books/{createdBook.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NestedPost_MissingParent_Returns404()
    {
        var response = await client.PostAsJsonAsync(
            "/authors/424242/books",
            new { Title = "Orphan" }
        );
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FlatPost_BadForeignKey_Returns409()
    {
        var response = await client.PostAsJsonAsync(
            "/books",
            new { Title = "Orphan", AuthorId = 424242 }
        );
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAuthor_WithBooks_Returns409()
    {
        var authorId = await CreateAuthorAsync("HasBooks");

        var createResponse = await client.PostAsJsonAsync(
            $"/authors/{authorId}/books",
            new { Title = "Kept" }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync($"/authors/{authorId}");
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);

        var stillThere = await client.GetAsync($"/authors/{authorId}");
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    [Fact]
    public async Task Pagination_BadPage_Returns400()
    {
        var response = await client.GetAsync("/books?page=0&pageSize=20");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
