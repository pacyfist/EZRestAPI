namespace Example.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

/// <summary>Runtime check that a single owned reference (Profile.Address)
/// round-trips through create and read. Docker-only ("MsSql").</summary>
[Collection("MsSql")]
public class OwnedReferenceEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public OwnedReferenceEndpointTests(MsSqlContainerFixture fixture)
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
    public async Task Profile_CreateWithAddress_ThenReadEmbedsIt()
    {
        var create = await client.PostAsJsonAsync(
            "/profiles",
            new
            {
                DisplayName = "Ada",
                Address = new
                {
                    Street = "1 Analytical Way",
                    City = "London",
                    PostalCode = "EC1",
                },
            }
        );
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var created = await create.Content.ReadFromJsonAsync<JsonDocument>();
        var id = created!.RootElement.GetProperty("id").GetInt32();

        using var readResponse = await (
            await client.GetAsync($"/profiles/{id}")
        ).Content.ReadFromJsonAsync<JsonDocument>();
        var address = readResponse!.RootElement.GetProperty("address");
        Assert.Equal("London", address.GetProperty("city").GetString());
    }
}
