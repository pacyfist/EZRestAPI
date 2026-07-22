namespace EZRestAPI.Tests;

public class AggregateCommandTests
{
    // An Order aggregate with two [Command]s: a parameterless `Cancel` with an
    // explicit route override, and a two-scalar-parameter `AddLine` whose route
    // is the kebab-cased method name. Cancel guards state with
    // InvalidOperationException (-> 409); AddLine guards its argument with
    // ArgumentOutOfRangeException (-> 422).
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
                return new Order { Customer = customer, Status = "Placed" };
            }

            public CustomerRef Customer { get; private set; } = null!;
            public string Status { get; private set; } = "";
            public IReadOnlyList<string> Lines => _lines;
            private readonly List<string> _lines = new();

            [EZRestAPI.Command("cancel")]
            public void Cancel()
            {
                if (Status == "Shipped")
                {
                    throw new System.InvalidOperationException("Cannot cancel a shipped order.");
                }
                Status = "Cancelled";
            }

            [EZRestAPI.Command]
            public void AddLine(string sku, int quantity)
            {
                if (quantity <= 0)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(quantity));
                }
                _lines.Add($"{sku} x{quantity}");
            }
        }
        """;

    // ---- Command request DTOs --------------------------------------------

    [Fact]
    public void CommandRequestDto_IsGenerated_ForParameterizedCommand()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var hintNames = result
            .Results.SelectMany(r => r.GeneratedSources)
            .Select(s => s.HintName)
            .ToArray();

        // Parameterized command -> {Command}{Name}Request body DTO.
        Assert.Contains("AddLineOrderRequest.g.cs", hintNames);
        // Parameterless command -> NO request DTO.
        Assert.DoesNotContain("CancelOrderRequest.g.cs", hintNames);
    }

    [Fact]
    public void CommandRequestDto_MirrorsMethodParameters()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var dto = GeneratorHarness.GetSource(result, "AddLineOrderRequest.g.cs");

        Assert.Contains("public class AddLineOrderRequest", dto);
        // Non-nullable reference parameter -> required + synthesized [Required].
        Assert.Contains("required string Sku { get; set; }", dto);
        Assert.Contains("[System.ComponentModel.DataAnnotations.Required]", dto);
        Assert.Contains("public int Quantity { get; set; }", dto);
        // No surrogate key on a command request.
        Assert.DoesNotContain("public int Id", dto);
    }

    // ---- Repository Execute{Command}Async loads TRACKED -------------------

    [Fact]
    public void Repository_ExecuteCommand_LoadsTracked_ParameterizedInvokesMethod()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var repo = GeneratorHarness.GetSource(result, "OrderRepository.g.cs");

        Assert.Contains(
            "public async Task<ReadOrderResponse?> ExecuteAddLineAsync(int id, AddLineOrderRequest request, CancellationToken cancellationToken)",
            repo
        );
        // Tracked load on the command path — never AsNoTracking.
        Assert.Contains(
            "var entity = await context.Orders.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);",
            repo
        );
        Assert.DoesNotContain(
            "context.Orders.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id",
            repo
        );
        // Not-found is signalled as null.
        Assert.Contains("return null;", repo);
        // The method is invoked directly with mapped arguments.
        Assert.Contains("entity.AddLine(request.Sku, request.Quantity);", repo);
        Assert.Contains("await context.SaveChangesAsync(cancellationToken);", repo);
        // Mutated read representation returned.
        Assert.Contains("return new ReadOrderResponse()", repo);
    }

    [Fact]
    public void Repository_ExecuteCommand_ParameterlessTakesNoRequest()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var repo = GeneratorHarness.GetSource(result, "OrderRepository.g.cs");

        Assert.Contains(
            "public async Task<ReadOrderResponse?> ExecuteCancelAsync(int id, CancellationToken cancellationToken)",
            repo
        );
        Assert.Contains("entity.Cancel();", repo);
    }

    // ---- Command endpoints -------------------------------------------------

    [Fact]
    public void Endpoints_Command_MapsPostToKebabOrOverrideSubResource()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var endpoints = GeneratorHarness.GetSource(result, "OrderEndpoints.g.cs");

        // Kebab-cased method name for the un-named command.
        Assert.Contains(
            "group.MapPost(\"/{id:int}/add-line\", async Task<Results<Ok<ReadOrderResponse>, ProblemHttpResult>> (",
            endpoints
        );
        // Explicit route override for the named command.
        Assert.Contains(
            "group.MapPost(\"/{id:int}/cancel\", async Task<Results<Ok<ReadOrderResponse>, ProblemHttpResult>> (",
            endpoints
        );
        // OperationId + tag.
        Assert.Contains(".WithName(\"AddLineOrder\")", endpoints);
        Assert.Contains(".WithName(\"CancelOrder\")", endpoints);
        Assert.Contains(".WithTags(\"Orders\")", endpoints);
    }

    [Fact]
    public void Endpoints_Command_MapsExceptionsToStatuses_InCorrectCatchOrder()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var endpoints = GeneratorHarness.GetSource(result, "OrderEndpoints.g.cs");

        // ArgumentException (and subclasses, e.g. ArgumentOutOfRangeException) -> 422.
        Assert.Contains("catch (System.ArgumentException", endpoints);
        Assert.Contains("EZRestAPIProblems.Unprocessable(", endpoints);
        // InvalidOperationException -> 409.
        Assert.Contains("catch (System.InvalidOperationException", endpoints);
        Assert.Contains("EZRestAPIProblems.Conflict(", endpoints);
        // The exception Message becomes the ProblemDetails detail.
        Assert.Contains(".Message", endpoints);

        // Catch ordering: ArgumentException must be caught BEFORE
        // InvalidOperationException so ArgumentOutOfRangeException hits the 422
        // arm and InvalidOperationException is never swallowed by it.
        var argIndex = endpoints.IndexOf(
            "catch (System.ArgumentException",
            System.StringComparison.Ordinal
        );
        var invOpIndex = endpoints.IndexOf(
            "catch (System.InvalidOperationException",
            System.StringComparison.Ordinal
        );
        Assert.True(argIndex >= 0 && invOpIndex >= 0);
        Assert.True(
            argIndex < invOpIndex,
            "ArgumentException must be caught before InvalidOperationException."
        );

        // Problem metadata for 404 / 409 / 422.
        Assert.Contains(
            ".ProducesProblem(StatusCodes.Status404NotFound, \"application/problem+json\")",
            endpoints
        );
        Assert.Contains(
            ".ProducesProblem(StatusCodes.Status409Conflict, \"application/problem+json\")",
            endpoints
        );
        Assert.Contains(
            ".ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity, \"application/problem+json\")",
            endpoints
        );
    }

    [Fact]
    public void Endpoints_Command_404WhenAggregateMissing()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var endpoints = GeneratorHarness.GetSource(result, "OrderEndpoints.g.cs");

        Assert.Contains("EZRestAPIProblems.NotFound(", endpoints);
        Assert.Contains("return TypedResults.Ok(response);", endpoints);
    }

    [Fact]
    public void Endpoints_Aggregate_HasNoPutRoute()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var endpoints = GeneratorHarness.GetSource(result, "OrderEndpoints.g.cs");

        Assert.DoesNotContain("MapPut", endpoints);
    }
}
