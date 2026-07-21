namespace EZRestAPI.Tests;

public class NestedEndpointTests
{
    private const string Source = """
        namespace Tests;

        [EZRestAPI.Model("Author", "Authors")]
        public partial class AuthorModel { public required string Name { get; set; } }

        [EZRestAPI.Model("Book", "Books")]
        public partial class BookModel
        {
            public required string Title { get; set; }
            public required int AuthorId { get; set; }
        }
        """;

    [Fact]
    public void NestedRouteGroup_IsGenerated()
    {
        var result = GeneratorHarness.Run(Source);
        var endpoints = GeneratorHarness.GetSource(result, "BookEndpoints.g.cs");

        Assert.Contains("app.MapGroup(\"/authors/{parentId:int}/books\")", endpoints);
        Assert.Contains("ListBookByAuthorAsync", endpoints);
        Assert.Contains("CreateBookUnderAuthorAsync", endpoints);
        Assert.Contains("ReadBookUnderAuthorAsync", endpoints);
        Assert.Contains("UpdateBookUnderAuthorAsync", endpoints);
        Assert.Contains("DeleteBookUnderAuthorAsync", endpoints);
    }

    [Fact]
    public void NestedHandlers_UseTypedResultsAndProblemDetails()
    {
        var result = GeneratorHarness.Run(Source);
        var endpoints = GeneratorHarness.GetSource(result, "BookEndpoints.g.cs");

        // No bare Results.* survive on the nested handlers (TypedResults.* is fine).
        Assert.DoesNotContain("Results.Conflict()", endpoints);
        Assert.DoesNotContain("Results.NotFound()", endpoints);
        Assert.DoesNotContain("return Results.Ok(", endpoints);
        Assert.DoesNotContain("return Results.Created(", endpoints);
        Assert.DoesNotContain("return Results.NoContent()", endpoints);

        // Nested delete conflict now flows through the shared problem factory (409).
        Assert.Contains("WriteResult.Conflict => EZRestAPIProblems.Conflict(", endpoints);
        Assert.Contains("TypedResults.NoContent()", endpoints);
        // Missing path parent on nested create -> 404.
        Assert.Contains("EZRestAPIProblems.NotFound(", endpoints);
    }

    [Fact]
    public void NestedCreateAndUpdate_ValidateBeforeRepository()
    {
        var result = GeneratorHarness.Run(Source);
        var endpoints = GeneratorHarness.GetSource(result, "BookEndpoints.g.cs");

        Assert.Contains("EZRestAPIValidation.Validate(request)", endpoints);
        Assert.Contains(
            "new Microsoft.AspNetCore.Http.HttpValidationProblemDetails(validationErrors)",
            endpoints
        );
        Assert.Contains("StatusCodes.Status422UnprocessableEntity", endpoints);
    }

    [Fact]
    public void NestedRoutes_CarryTagsAndProducesProblem()
    {
        var result = GeneratorHarness.Run(Source);
        var endpoints = GeneratorHarness.GetSource(result, "BookEndpoints.g.cs");

        // Nested resource is tagged by the child plural name and keeps its operationId.
        Assert.Contains(".WithName(\"CreateBookUnderAuthor\")", endpoints);
        Assert.Contains(".WithTags(\"Books\")", endpoints);
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
    public void NestedHandlers_DeclareUnionReturnTypes()
    {
        var result = GeneratorHarness.Run(Source);
        var endpoints = GeneratorHarness.GetSource(result, "BookEndpoints.g.cs");

        Assert.Contains(
            "async Task<Results<Created<CreateBookResponse>, ProblemHttpResult>> (",
            endpoints
        );
        Assert.Contains(
            "async Task<Results<Ok<PagedResponse<ReadBookResponse>>, ProblemHttpResult>> (",
            endpoints
        );
        Assert.Contains("async Task<Results<NoContent, ProblemHttpResult>> (", endpoints);
    }
}
