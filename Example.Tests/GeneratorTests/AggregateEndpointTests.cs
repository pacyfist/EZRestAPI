namespace Example.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

/// <summary>
/// End-to-end HTTP tests for the DDD <c>Order</c> aggregate: factory-based
/// creation, encapsulated reads, command sub-resources, and the exception →
/// status mapping (ArgumentException → 422, InvalidOperationException → 409).
/// Needs a real SQL Server, so it runs in the shared "MsSql" collection and
/// only where Docker is available (skipped in the no-Docker sandbox).
/// </summary>
[Collection("MsSql")]
public class AggregateEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public AggregateEndpointTests(MsSqlContainerFixture fixture)
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

    private static void AssertProblemJson(HttpResponseMessage response) =>
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

    private async Task<ReadOrderResponse> PlaceOrderAsync()
    {
        var createResponse = await client.PostAsJsonAsync(
            "/orders",
            new { Customer = new { Name = "Ada Lovelace", Email = "ada@example.com" } }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<ReadOrderResponse>();
        Assert.NotNull(created);
        Assert.NotNull(created!.Id);
        return created;
    }

    [Fact]
    public async Task Order_FactoryCreate_ThenRead_OverHttp()
    {
        var created = await PlaceOrderAsync();

        // The factory seeds Status = "Placed" and no lines; the read reflects the
        // encapsulated (private-set / get-only) state.
        Assert.Equal("Placed", created.Status);
        Assert.Empty(created.Lines);
        Assert.Equal("Ada Lovelace", created.Customer.Name);

        var read = await client.GetFromJsonAsync<ReadOrderResponse>($"/orders/{created.Id}");
        Assert.NotNull(read);
        Assert.Equal("Placed", read!.Status);
        Assert.Equal("ada@example.com", read.Customer.Email);
    }

    [Fact]
    public async Task Order_AddLineCommand_ThenReadShowsLine_OverHttp()
    {
        var created = await PlaceOrderAsync();

        var addLine = await client.PostAsJsonAsync(
            $"/orders/{created.Id}/add-line",
            new { Sku = "WIDGET-1", Quantity = 3 }
        );
        Assert.Equal(HttpStatusCode.OK, addLine.StatusCode);

        // The command returns the mutated representation directly...
        var afterCommand = await addLine.Content.ReadFromJsonAsync<ReadOrderResponse>();
        Assert.NotNull(afterCommand);
        Assert.Equal("WIDGET-1 x3", Assert.Single(afterCommand!.Lines));

        // ...and a fresh GET shows the persisted line via the field-backed projection.
        var read = await client.GetFromJsonAsync<ReadOrderResponse>($"/orders/{created.Id}");
        Assert.NotNull(read);
        Assert.Equal("WIDGET-1 x3", Assert.Single(read!.Lines));
    }

    [Fact]
    public async Task Order_CancelCommand_Succeeds_ThenReadShowsCancelled_OverHttp()
    {
        var created = await PlaceOrderAsync();

        var cancel = await client.PostAsync($"/orders/{created.Id}/cancel", content: null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var afterCommand = await cancel.Content.ReadFromJsonAsync<ReadOrderResponse>();
        Assert.NotNull(afterCommand);
        Assert.Equal("Cancelled", afterCommand!.Status);

        var read = await client.GetFromJsonAsync<ReadOrderResponse>($"/orders/{created.Id}");
        Assert.NotNull(read);
        Assert.Equal("Cancelled", read!.Status);
    }

    [Fact]
    public async Task Order_ForbiddenCancel_Returns409ProblemJson_OverHttp()
    {
        var created = await PlaceOrderAsync();

        // First cancel succeeds and moves the order into the terminal Cancelled state.
        var first = await client.PostAsync($"/orders/{created.Id}/cancel", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // A second cancel is refused by the aggregate's state guard
        // (InvalidOperationException) -> 409 application/problem+json.
        var second = await client.PostAsync($"/orders/{created.Id}/cancel", content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        AssertProblemJson(second);
    }

    [Fact]
    public async Task Order_InvalidAddLineQuantity_Returns422ProblemJson_OverHttp()
    {
        var created = await PlaceOrderAsync();

        // quantity <= 0 trips the ArgumentOutOfRangeException guard -> 422.
        var addLine = await client.PostAsJsonAsync(
            $"/orders/{created.Id}/add-line",
            new { Sku = "BAD-QTY", Quantity = 0 }
        );
        Assert.Equal(HttpStatusCode.UnprocessableEntity, addLine.StatusCode);
        AssertProblemJson(addLine);
    }

    [Fact]
    public async Task Order_Delete_ThenReadReturns404_OverHttp()
    {
        var created = await PlaceOrderAsync();

        var delete = await client.DeleteAsync($"/orders/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var notFound = await client.GetAsync($"/orders/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        AssertProblemJson(notFound);
    }
}
