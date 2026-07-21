namespace EZRestAPI.Tests;

public class EndpointContractTests
{
    // Author is a parent (has Books); Book carries a foreign key to Author.
    private const string RelatedSource = """
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

    private const string UnrelatedSource = """
        namespace Tests;

        [EZRestAPI.Model("Tag", "Tags")]
        public partial class TagModel
        {
            public required string Name { get; set; }
        }
        """;

    [Fact]
    public void FlatHandlers_HaveNoBareResults()
    {
        var result = GeneratorHarness.Run(RelatedSource);
        var book = GeneratorHarness.GetSource(result, "BookEndpoints.g.cs");
        var author = GeneratorHarness.GetSource(result, "AuthorEndpoints.g.cs");

        foreach (var endpoints in new[] { book, author })
        {
            Assert.DoesNotContain("Results.NotFound()", endpoints);
            Assert.DoesNotContain("Results.Conflict()", endpoints);
            Assert.DoesNotContain("Results.BadRequest(", endpoints);
            Assert.DoesNotContain("return Results.Ok(", endpoints);
            Assert.DoesNotContain("return Results.Created(", endpoints);
            Assert.DoesNotContain("return Results.NoContent()", endpoints);
        }
    }

    [Fact]
    public void FlatPost_ValidatesAndReturns422ValidationProblem()
    {
        var result = GeneratorHarness.Run(UnrelatedSource);
        var endpoints = GeneratorHarness.GetSource(result, "TagEndpoints.g.cs");

        Assert.Contains("var validationErrors = EZRestAPIValidation.Validate(request);", endpoints);
        Assert.Contains("if (validationErrors is not null)", endpoints);
        Assert.Contains(
            "new Microsoft.AspNetCore.Http.HttpValidationProblemDetails(validationErrors)",
            endpoints
        );
        Assert.Contains("Status = StatusCodes.Status422UnprocessableEntity,", endpoints);
        Assert.Contains("return TypedResults.Problem(problem);", endpoints);
    }

    [Fact]
    public void FlatPost_BadForeignKey_Returns422NotConflict()
    {
        var result = GeneratorHarness.Run(RelatedSource);
        var endpoints = GeneratorHarness.GetSource(result, "BookEndpoints.g.cs");

        // The shipped 409 for a bad body FK is replaced by 422.
        Assert.Contains(
            "return EZRestAPIProblems.Unprocessable(\"The referenced Author does not exist.\");",
            endpoints
        );
        Assert.DoesNotContain("Results.Conflict()", endpoints);
    }

    [Fact]
    public void FlatRead_NotFound_IsProblemDetails404()
    {
        var result = GeneratorHarness.Run(RelatedSource);
        var endpoints = GeneratorHarness.GetSource(result, "BookEndpoints.g.cs");

        Assert.Contains("return EZRestAPIProblems.NotFound(", endpoints);
        Assert.Contains(
            "async Task<Results<Ok<ReadBookResponse>, ProblemHttpResult>> (",
            endpoints
        );
        Assert.Contains(
            ".ProducesProblem(StatusCodes.Status404NotFound, \"application/problem+json\")",
            endpoints
        );
    }

    [Fact]
    public void FlatDeleteParent_WithChildren_IsConflict409()
    {
        var result = GeneratorHarness.Run(RelatedSource);
        var endpoints = GeneratorHarness.GetSource(result, "AuthorEndpoints.g.cs");

        Assert.Contains("WriteResult.Conflict => EZRestAPIProblems.Conflict(", endpoints);
        Assert.Contains("dependent Books and cannot be deleted", endpoints);
        Assert.Contains(
            ".ProducesProblem(StatusCodes.Status409Conflict, \"application/problem+json\")",
            endpoints
        );
        Assert.Contains(
            ".ProducesProblem(StatusCodes.Status404NotFound, \"application/problem+json\")",
            endpoints
        );
    }

    [Fact]
    public void FlatList_BadPaging_IsUnprocessable422()
    {
        var result = GeneratorHarness.Run(UnrelatedSource);
        var endpoints = GeneratorHarness.GetSource(result, "TagEndpoints.g.cs");

        Assert.Contains(
            "return EZRestAPIProblems.Unprocessable(\"page and pageSize must be >= 1.\");",
            endpoints
        );
        Assert.Contains(
            "async Task<Results<Ok<PagedResponse<ReadTagResponse>>, ProblemHttpResult>> (",
            endpoints
        );
    }

    [Fact]
    public void FlatRoutes_CarryTagsAndKeepOperationIds()
    {
        var result = GeneratorHarness.Run(RelatedSource);
        var book = GeneratorHarness.GetSource(result, "BookEndpoints.g.cs");
        var author = GeneratorHarness.GetSource(result, "AuthorEndpoints.g.cs");

        Assert.Contains(".WithName(\"CreateBook\")", book);
        Assert.Contains(".WithTags(\"Books\")", book);
        Assert.Contains(".WithName(\"DeleteAuthor\")", author);
        Assert.Contains(".WithTags(\"Authors\")", author);
    }

    [Fact]
    public void FlatHandlers_DeclareUnionReturnTypes()
    {
        var result = GeneratorHarness.Run(UnrelatedSource);
        var endpoints = GeneratorHarness.GetSource(result, "TagEndpoints.g.cs");

        Assert.Contains(
            "async Task<Results<Created<CreateTagResponse>, ProblemHttpResult>> (",
            endpoints
        );
        Assert.Contains("async Task<Results<NoContent, ProblemHttpResult>> (", endpoints);
        Assert.Contains(
            "async Task<Results<Ok<ReadTagResponse>, ProblemHttpResult>> (",
            endpoints
        );
    }

    [Fact]
    public void UnrelatedUpdate_ProducesProblem422And404()
    {
        var result = GeneratorHarness.Run(UnrelatedSource);
        var endpoints = GeneratorHarness.GetSource(result, "TagEndpoints.g.cs");

        // Update maps validation (422) + not-found (404); no conflict on an unrelated model.
        Assert.Contains(".WithName(\"UpdateTag\")", endpoints);
        Assert.Contains(
            ".ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity, \"application/problem+json\")",
            endpoints
        );
        Assert.Contains(
            ".ProducesProblem(StatusCodes.Status404NotFound, \"application/problem+json\")",
            endpoints
        );
        // An unrelated model never deletes with children, so no 409 anywhere.
        Assert.DoesNotContain("Status409Conflict", endpoints);
    }
}
