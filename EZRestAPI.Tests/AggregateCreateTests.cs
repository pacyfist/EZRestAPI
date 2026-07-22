namespace EZRestAPI.Tests;

public class AggregateCreateTests
{
    // An Order aggregate created through a public static factory that takes a
    // value-object parameter (CustomerRef) — its Create request must nest the
    // VO's Dto, and the repository must INVOKE the factory (never object-init).
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
            public void Cancel() { }
        }
        """;

    // An Account aggregate created through a public constructor factory that
    // takes a scalar parameter — the repository must use `new Type(args)`.
    private const string AccountAggregate = """
        namespace Tests;

        [EZRestAPI.Aggregate("Account", "Accounts")]
        public partial class Account
        {
            private Account() { }

            [EZRestAPI.Factory]
            public Account(string owner) { Owner = owner; }

            public string Owner { get; private set; } = "";
        }
        """;

    // ---- Create request DTO mirrors factory parameters --------------------

    [Fact]
    public void CreateRequestDto_IsGeneratedForAggregate()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var hintNames = result
            .Results.SelectMany(r => r.GeneratedSources)
            .Select(s => s.HintName)
            .ToArray();

        Assert.Contains("CreateOrderRequest.g.cs", hintNames);
    }

    [Fact]
    public void CreateRequestDto_MirrorsFactoryParameters_NestingValueObjects()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var dto = GeneratorHarness.GetSource(result, "CreateOrderRequest.g.cs");

        Assert.Contains("public class CreateOrderRequest", dto);
        // Value-object parameter -> nested VO Dto, PascalCased field name.
        Assert.Contains("CustomerRefDto Customer { get; set; }", dto);
        // Non-nullable reference parameter -> `required` + synthesized [Required].
        Assert.Contains("required CustomerRefDto Customer", dto);
        Assert.Contains("[System.ComponentModel.DataAnnotations.Required]", dto);
        // No surrogate key on a create request.
        Assert.DoesNotContain("public int Id", dto);
    }

    [Fact]
    public void CreateRequestDto_MirrorsScalarConstructorParameter()
    {
        var result = GeneratorHarness.Run(AccountAggregate);
        var dto = GeneratorHarness.GetSource(result, "CreateAccountRequest.g.cs");

        Assert.Contains("public class CreateAccountRequest", dto);
        Assert.Contains("string Owner { get; set; }", dto);
    }

    // ---- Repository CreateAsync INVOKES the factory -----------------------

    [Fact]
    public void Repository_CreateAsync_InvokesStaticFactory_NoObjectInitializer()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var repo = GeneratorHarness.GetSource(result, "OrderRepository.g.cs");

        Assert.Contains(
            "public async Task<ReadOrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken)",
            repo
        );
        // Factory invocation: Type.Method(args), value object mapped via mapper.
        Assert.Contains(
            "var entity = global::Tests.Order.Place(CustomerRefMapper.ToEntity(request.Customer));",
            repo
        );
        // The aggregate is NEVER object-initialized from generated code.
        Assert.DoesNotContain("new global::Tests.Order", repo);
        // Added, saved, and mapped back to the read representation.
        Assert.Contains("context.Orders.Add(entity);", repo);
        Assert.Contains("await context.SaveChangesAsync(cancellationToken);", repo);
        Assert.Contains("return new ReadOrderResponse()", repo);
        Assert.Contains("Customer = CustomerRefMapper.ToDto(entity.Customer),", repo);
    }

    [Fact]
    public void Repository_CreateAsync_InvokesConstructorFactory()
    {
        var result = GeneratorHarness.Run(AccountAggregate);
        var repo = GeneratorHarness.GetSource(result, "AccountRepository.g.cs");

        Assert.Contains(
            "public async Task<ReadAccountResponse> CreateAsync(CreateAccountRequest request, CancellationToken cancellationToken)",
            repo
        );
        // Constructor factory: new Type(args).
        Assert.Contains("var entity = new global::Tests.Account(request.Owner);", repo);
        Assert.Contains("context.Accounts.Add(entity);", repo);
    }

    // ---- POST endpoint ----------------------------------------------------

    [Fact]
    public void Endpoints_CreatePost_Has201UnionAndValidationMetadata()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var endpoints = GeneratorHarness.GetSource(result, "OrderEndpoints.g.cs");

        Assert.Contains(
            "group.MapPost(\"/\", async Task<Results<Created<ReadOrderResponse>, ProblemHttpResult>> (",
            endpoints
        );
        // 422 validation guard on the request body.
        Assert.Contains("var validationErrors = EZRestAPIValidation.Validate(request);", endpoints);
        Assert.Contains(
            ".ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity, \"application/problem+json\")",
            endpoints
        );
        Assert.Contains(".WithName(\"CreateOrder\")", endpoints);
        Assert.Contains(".WithTags(\"Orders\")", endpoints);
        // 201 + flat Location.
        Assert.Contains(
            "return TypedResults.Created($\"/orders/{response.Id}\", response);",
            endpoints
        );
    }

    [Fact]
    public void Bootstrap_And_NoUpdate_StillHold()
    {
        var result = GeneratorHarness.Run(OrderAggregate);
        var endpoints = GeneratorHarness.GetSource(result, "OrderEndpoints.g.cs");

        // Create is added alongside T2, but still no blind PUT / Update.
        Assert.DoesNotContain("MapPut", endpoints);
        Assert.DoesNotContain(".WithName(\"UpdateOrder\")", endpoints);
    }
}
