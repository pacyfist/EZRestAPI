namespace Example.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

public class EndpointTests : IAsyncLifetime
{
    private MsSqlContainer container = null!;
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        container = new MsSqlBuilder(
            "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04"
        ).Build();
        await container.StartAsync();

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Example"] = container.GetConnectionString(),
                        }
                    )
            )
        );

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomDbContext>();
        await context.Database.EnsureCreatedAsync();

        client = factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
        await container.StopAsync();
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
