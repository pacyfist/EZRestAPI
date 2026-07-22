namespace EZRestAPI.Tests;

public class AggregateReadTests
{
    // An Order aggregate with a factory, a value-object read property, a
    // { get; init; } and a get-only IReadOnlyList projection over a private
    // backing field, plus a command (whose endpoint is out of scope for T2).
    private const string OrderAggregate = """
        namespace Tests;

        using System.Collections.Generic;

        [EZRestAPI.Nested("CustomerRef")]
        public class CustomerRef
        {
            public required string Name { get; set; }
        }

        [EZRestAPI.Aggregate("Order", "Orders")]
        public partial class Order
        {
            private Order() { }

            [EZRestAPI.Factory]
            public static Order Place(CustomerRef customer)
            {
                return new Order { Customer = customer };
            }

            public CustomerRef Customer { get; private set; } = null!;
            public string Status { get; init; } = "";
            public IReadOnlyList<string> Lines => _lines;
            private readonly List<string> _lines = new();

            [EZRestAPI.Command("cancel")]
            public void Cancel() { }
        }
        """;

    // ---- DTOs -------------------------------------------------------------

    [Fact]
    public void ReadResponse_IncludesGetOnlyAndPrivateSetAndInitProperties()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var dto = GeneratorHarness.GetSource(result, "ReadOrderResponse.g.cs");

        Assert.Contains("public int? Id { get; set; }", dto);
        // { get; private set; } value object -> embedded as its Dto.
        Assert.Contains("CustomerRefDto Customer { get; set; }", dto);
        // { get; init; } scalar.
        Assert.Contains("string Status { get; set; }", dto);
        // get-only projection over a private backing field (dropped by [Model]).
        Assert.Contains("Lines { get; set; }", dto);
    }

    [Fact]
    public void NoCreateOrUpdateRequestDto_IsGeneratedForAggregate()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var hintNames = result
            .Results.SelectMany(r => r.GeneratedSources)
            .Select(s => s.HintName)
            .ToArray();

        Assert.DoesNotContain("CreateOrderRequest.g.cs", hintNames);
        Assert.DoesNotContain("UpdateOrderRequest.g.cs", hintNames);
    }

    // ---- Repository -------------------------------------------------------

    [Fact]
    public void Repository_HasReadListAndDeleteMethods()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var repo = GeneratorHarness.GetSource(result, "OrderRepository.g.cs");

        Assert.Contains(
            "public async Task<ReadOrderResponse?> ReadAsync(int id, CancellationToken cancellationToken)",
            repo
        );
        Assert.Contains(
            "public async Task<PagedResponse<ReadOrderResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)",
            repo
        );
        Assert.Contains(
            "public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)",
            repo
        );
        // Read is AsNoTracking; Delete loads + removes so owned VOs cascade.
        Assert.Contains(".AsNoTracking()", repo);
        Assert.Contains("context.Orders.Remove(entity);", repo);
    }

    [Fact]
    public void Repository_HasNoCreateOrUpdateMethods()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var repo = GeneratorHarness.GetSource(result, "OrderRepository.g.cs");

        Assert.DoesNotContain("CreateAsync(", repo);
        Assert.DoesNotContain("UpdateAsync(", repo);
    }

    // ---- Endpoints --------------------------------------------------------

    [Fact]
    public void Endpoints_ReadListAndDelete_HaveUnionReturnTypes()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var endpoints = GeneratorHarness.GetSource(result, "OrderEndpoints.g.cs");

        Assert.Contains(
            "async Task<Results<Ok<ReadOrderResponse>, ProblemHttpResult>> (",
            endpoints
        );
        Assert.Contains(
            "async Task<Results<Ok<PagedResponse<ReadOrderResponse>>, ProblemHttpResult>> (",
            endpoints
        );
        Assert.Contains("async Task<Results<NoContent, ProblemHttpResult>> (", endpoints);
    }

    [Fact]
    public void Endpoints_MapExpectedVerbsAndRoutes()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var endpoints = GeneratorHarness.GetSource(result, "OrderEndpoints.g.cs");

        Assert.Contains("var group = app.MapGroup(\"/orders\");", endpoints);
        Assert.Contains("group.MapGet(\"/\",", endpoints);
        Assert.Contains("group.MapGet(\"/{id:int}\",", endpoints);
        Assert.Contains("group.MapDelete(\"/{id:int}\",", endpoints);
    }

    [Fact]
    public void Endpoints_HaveNoPutAndNoCreatePost()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var endpoints = GeneratorHarness.GetSource(result, "OrderEndpoints.g.cs");

        // No blind PUT and no create POST for an aggregate in this increment.
        Assert.DoesNotContain("MapPut", endpoints);
        Assert.DoesNotContain("MapPost", endpoints);
        Assert.DoesNotContain(".WithName(\"CreateOrder\")", endpoints);
        Assert.DoesNotContain(".WithName(\"UpdateOrder\")", endpoints);
    }

    [Fact]
    public void Endpoints_CarryTagsNamesAndProblemMetadata()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var endpoints = GeneratorHarness.GetSource(result, "OrderEndpoints.g.cs");

        Assert.Contains(".WithName(\"ReadOrder\")", endpoints);
        Assert.Contains(".WithName(\"ListOrders\")", endpoints);
        Assert.Contains(".WithName(\"DeleteOrder\")", endpoints);
        Assert.Contains(".WithTags(\"Orders\")", endpoints);
        Assert.Contains(
            ".ProducesProblem(StatusCodes.Status404NotFound, \"application/problem+json\")",
            endpoints
        );
        Assert.Contains(
            "return EZRestAPIProblems.NotFound($\"No Order with id {id} exists.\");",
            endpoints
        );
    }

    // ---- Bootstrap --------------------------------------------------------

    [Fact]
    public void Bootstrap_RegistersRepositoryAndMapsEndpoints()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var bootstrap = GeneratorHarness.GetSource(result, "EZRestAPIExtensions.g.cs");

        Assert.Contains("services.AddScoped<OrderRepository>();", bootstrap);
        Assert.Contains("app.MapOrderEndpoints();", bootstrap);
    }

    // ---- Persistence ------------------------------------------------------

    [Fact]
    public void DbContext_HasDbSetOwnedTypeAndFieldAccessMode()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var dbContext = GeneratorHarness.GetSource(result, "CustomDbContext.g.cs");

        Assert.Contains("Order> Orders { get; set; } = null!;", dbContext);
        // Value object mapped as an owned type.
        Assert.Contains("OwnsOne(e => e.Customer)", dbContext);
        // IReadOnlyList<T> projection over a private backing field.
        Assert.Contains(
            "UsePropertyAccessMode(Microsoft.EntityFrameworkCore.PropertyAccessMode.Field)",
            dbContext
        );
    }
}
