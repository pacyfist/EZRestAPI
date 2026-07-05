namespace Example.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

[Collection("MsSql")]
public class EndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public EndpointTests(MsSqlContainerFixture fixture)
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
    public async Task SimpleData_CrudRoundTrip_OverHttp()
    {
        var createResponse = await client.PostAsJsonAsync(
            "/simpledataplural",
            new
            {
                IntegerProperty = 1,
                DoubleProperty = 1.1,
                StringProperty = "Test",
                DateTimeOffsetProperty = DateTimeOffset.UtcNow,
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<CreateSimpleDataResponse>();
        Assert.NotNull(created);
        Assert.Equal("Test", created.StringProperty);

        var read = await client.GetFromJsonAsync<ReadSimpleDataResponse>(
            $"/simpledataplural/{created.Id}"
        );
        Assert.Equal("Test", read?.StringProperty);

        var updateResponse = await client.PutAsJsonAsync(
            $"/simpledataplural/{created.Id}",
            new
            {
                Id = created.Id,
                IntegerProperty = 2,
                DoubleProperty = 2.2,
                StringProperty = "Test2",
                DateTimeOffsetProperty = DateTimeOffset.UtcNow,
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var reread = await client.GetFromJsonAsync<ReadSimpleDataResponse>(
            $"/simpledataplural/{created.Id}"
        );
        Assert.Equal("Test2", reread?.StringProperty);

        var deleteResponse = await client.DeleteAsync($"/simpledataplural/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var notFound = await client.GetAsync($"/simpledataplural/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task Post_NestedGraphCrudRoundTrip_OverHttp()
    {
        var createResponse = await client.PostAsJsonAsync(
            "/posts",
            new
            {
                Title = "Hello",
                Comments = new List<CommentDto>
                {
                    new() { Text = "First", Reactions = [new() { Emoji = "+1" }] },
                    new() { Text = "Second", Reactions = [] },
                },
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<CreatePostResponse>();
        Assert.NotNull(created);

        var read = await client.GetFromJsonAsync<ReadPostResponse>($"/posts/{created.Id}");
        Assert.NotNull(read);
        Assert.Equal("Hello", read.Title);
        Assert.Equal(2, read.Comments.Count);

        var first = Assert.Single(read.Comments, c => c.Text == "First");
        Assert.Equal("+1", Assert.Single(first.Reactions).Emoji);
        Assert.Empty(Assert.Single(read.Comments, c => c.Text == "Second").Reactions);

        var updateResponse = await client.PutAsJsonAsync(
            $"/posts/{created.Id}",
            new
            {
                Id = created.Id,
                Title = "Hello2",
                Comments = new List<CommentDto>
                {
                    new() { Text = "Replaced", Reactions = [new() { Emoji = "🎉" }] },
                },
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var reread = await client.GetFromJsonAsync<ReadPostResponse>($"/posts/{created.Id}");
        Assert.NotNull(reread);
        Assert.Equal("Hello2", reread.Title);

        var replaced = Assert.Single(reread.Comments);
        Assert.Equal("Replaced", replaced.Text);
        Assert.Equal("🎉", Assert.Single(replaced.Reactions).Emoji);

        var deleteResponse = await client.DeleteAsync($"/posts/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var notFound = await client.GetAsync($"/posts/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task SimpleData_MissingId_Returns404()
    {
        var read = await client.GetAsync("/simpledataplural/424242");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        var delete = await client.DeleteAsync("/simpledataplural/424242");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        var update = await client.PutAsJsonAsync(
            "/simpledataplural/424242",
            new
            {
                Id = 424242,
                IntegerProperty = 1,
                DoubleProperty = 1.1,
                StringProperty = "Test",
                DateTimeOffsetProperty = DateTimeOffset.UtcNow,
            }
        );
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
    }
}
