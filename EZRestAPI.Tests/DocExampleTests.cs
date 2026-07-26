namespace EZRestAPI.Tests;

using Microsoft.CodeAnalysis;
using Xunit;

/// <summary>
/// Pins the worked example in <c>docs/specs/model-vs-aggregate.md</c> to the
/// generator. That example hand-writes an endpoint against generated names
/// (<c>ProductRepository.ReadAsync</c>, <c>AddLineOrderRequest</c>,
/// <c>OrderRepository.ExecuteAddLineAsync</c>), so a rename would leave the
/// docs quietly wrong. If this test fails, fix the doc, not the assertion.
/// </summary>
public class DocExampleTests
{
    private const string Source = """
        namespace Tests;

        using System.Collections.Generic;

        [EZRestAPI.Model("Product", "Products")]
        public partial class ProductModel
        {
            public required string Sku { get; set; }
            public required decimal Price { get; set; }
            public required bool IsDiscontinued { get; set; }
        }

        [EZRestAPI.Nested("CustomerRef")]
        public class CustomerRef
        {
            public required string Name { get; set; }
        }

        [EZRestAPI.Nested("OrderLine")]
        public class OrderLine
        {
            public required string Sku { get; set; }
            public required decimal UnitPrice { get; set; }
            public required int Quantity { get; set; }
        }

        [EZRestAPI.Aggregate("Order", "Orders")]
        public partial class Order
        {
            private Order() { }

            [EZRestAPI.Factory]
            public static Order Place(CustomerRef customer) => new() { Customer = customer };

            public CustomerRef Customer { get; private set; } = null!;
            public string Status { get; private set; } = "Placed";
            public IReadOnlyList<OrderLine> Lines => _lines;
            private readonly List<OrderLine> _lines = [];

            [EZRestAPI.Command]
            public void AddLine(string sku, decimal unitPrice, int quantity)
            {
                if (quantity <= 0) throw new System.ArgumentOutOfRangeException(nameof(quantity));
                if (Status != "Placed") throw new System.InvalidOperationException("Order is closed.");
                _lines.Add(new OrderLine { Sku = sku, UnitPrice = unitPrice, Quantity = quantity });
            }
        }
        """;

    private static string[] HintNames(GeneratorDriverRunResult result) =>
        result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.HintName).ToArray();

    /// <summary>
    /// The Endpoints.None catalog model: a full repository and DTOs, no routes.
    /// </summary>
    [Fact]
    public void NoneModel_HasARepositoryAndDtosButNoEndpoints()
    {
        var result = GeneratorHarness.Run(Source);

        Assert.Contains(
            "public async Task<ReadProductResponse?> ReadAsync(int id, CancellationToken",
            GeneratorHarness.GetSource(result, "ProductRepository.g.cs")
        );

        var read = GeneratorHarness.GetSource(result, "ReadProductResponse.g.cs");
        Assert.Contains("public required string Sku", read);
        Assert.Contains("public required decimal Price", read);
        Assert.Contains("public required bool IsDiscontinued", read);

        Assert.DoesNotContain("ProductEndpoints.g.cs", HintNames(result));
    }

    /// <summary>
    /// The command DTO the hand-written endpoint object-initializes. Parameter
    /// names arrive PascalCased and settable.
    /// </summary>
    [Fact]
    public void CommandRequest_HasThePascalCasedParameters()
    {
        var request = GeneratorHarness.GetSource(
            GeneratorHarness.Run(Source),
            "AddLineOrderRequest.g.cs"
        );

        Assert.Contains("public required string Sku { get; set; }", request);
        Assert.Contains("public decimal UnitPrice { get; set; }", request);
        Assert.Contains("public int Quantity { get; set; }", request);
    }

    [Fact]
    public void CommandRepositoryMethod_HasTheSignatureTheDocCalls()
    {
        Assert.Contains(
            "public async Task<ReadOrderResponse?> ExecuteAddLineAsync(int id, AddLineOrderRequest request, CancellationToken",
            GeneratorHarness.GetSource(GeneratorHarness.Run(Source), "OrderRepository.g.cs")
        );
    }

    /// <summary>
    /// Both repositories are injectable, but only the aggregate is mapped —
    /// which is what lets the hand-written endpoint take a ProductRepository.
    /// </summary>
    [Fact]
    public void BothRepositoriesAreInjectable_OnlyTheAggregateIsMapped()
    {
        var bootstrap = GeneratorHarness.GetSource(
            GeneratorHarness.Run(Source),
            "EZRestAPIExtensions.g.cs"
        );

        Assert.Contains("services.AddScoped<ProductRepository>();", bootstrap);
        Assert.Contains("services.AddScoped<OrderRepository>();", bootstrap);
        Assert.Contains("app.MapOrderEndpoints();", bootstrap);
        Assert.DoesNotContain("MapProductEndpoints", bootstrap);
    }

    /// <summary>
    /// The doc warns that a hand-written caller must catch the domain
    /// exceptions itself, because the mapping lives in the generated endpoint
    /// rather than in the repository. This is that claim.
    /// </summary>
    [Fact]
    public void ExceptionMapping_LivesInTheEndpointNotTheRepository()
    {
        var result = GeneratorHarness.Run(Source);

        Assert.DoesNotContain(
            "catch (System.ArgumentException",
            GeneratorHarness.GetSource(result, "OrderRepository.g.cs")
        );
        Assert.Contains(
            "catch (System.ArgumentException",
            GeneratorHarness.GetSource(result, "OrderEndpoints.g.cs")
        );
    }
}
