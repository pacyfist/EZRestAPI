namespace Example.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

/// <summary>Runtime checks for the constructor-factory ShoppingCart and the
/// OwnsMany-child-collection Invoice aggregate. Docker-only ("MsSql").</summary>
[Collection("MsSql")]
public class AggregateTourEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public AggregateTourEndpointTests(MsSqlContainerFixture fixture)
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

    private static readonly object Owner = new { Name = "Ada", Email = "ada@example.com" };

    [Fact]
    public async Task ShoppingCart_ConstructorFactoryCreate_ThenCheckout()
    {
        var create = await client.PostAsJsonAsync("/shoppingcarts", new { Owner });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = await create.Content.ReadFromJsonAsync<JsonDocument>();
        var id = created!.RootElement.GetProperty("id").GetInt32();

        var checkout = await client.PostAsync($"/shoppingcarts/{id}/checkout", content: null);
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);

        var second = await client.PostAsync($"/shoppingcarts/{id}/checkout", content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Invoice_AddLine_ThenReadShowsChildCollection()
    {
        var create = await client.PostAsJsonAsync("/invoices", new { BillTo = Owner });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = await create.Content.ReadFromJsonAsync<JsonDocument>();
        var id = created!.RootElement.GetProperty("id").GetInt32();

        var addLine = await client.PostAsJsonAsync(
            $"/invoices/{id}/add-line",
            new
            {
                Sku = "WIDGET-1",
                Quantity = 2,
                UnitPrice = 9.99m,
            }
        );
        Assert.Equal(HttpStatusCode.OK, addLine.StatusCode);

        using var read = await (
            await client.GetAsync($"/invoices/{id}")
        ).Content.ReadFromJsonAsync<JsonDocument>();
        var lines = read!.RootElement.GetProperty("lines");
        Assert.Equal(1, lines.GetArrayLength());
        Assert.Equal("WIDGET-1", lines[0].GetProperty("sku").GetString());
        Assert.Equal(2, lines[0].GetProperty("quantity").GetInt32());
    }
}
