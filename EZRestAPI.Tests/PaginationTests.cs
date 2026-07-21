namespace EZRestAPI.Tests;

public class PaginationTests
{
    private const string Source = """
        namespace Tests;

        [EZRestAPI.Model("Widget", "Widgets")]
        public partial class WidgetModel
        {
            public required string Name { get; set; }
        }
        """;

    [Fact]
    public void PagedResponseType_IsGeneratedOnce()
    {
        var result = GeneratorHarness.Run(Source);
        var dto = GeneratorHarness.GetSource(result, "PagedResponse.g.cs");

        Assert.Contains("public class PagedResponse<T>", dto);
        Assert.Contains("public int TotalCount { get; set; }", dto);
        Assert.Contains("public int Page { get; set; }", dto);
        Assert.Contains("public int PageSize { get; set; }", dto);
    }

    [Fact]
    public void Repository_HasPaginatedListAsync()
    {
        var result = GeneratorHarness.Run(Source);
        var repo = GeneratorHarness.GetSource(result, "WidgetRepository.g.cs");

        Assert.Contains(
            "public async Task<PagedResponse<ReadWidgetResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)",
            repo);
        Assert.Contains(".Skip((page - 1) * pageSize)", repo);
        Assert.Contains(".Take(pageSize)", repo);
        Assert.Contains(".OrderBy(e => e.Id)", repo);
    }

    [Fact]
    public void FlatListEndpoint_ValidatesAndClampsPaging()
    {
        var result = GeneratorHarness.Run(Source);
        var endpoints = GeneratorHarness.GetSource(result, "WidgetEndpoints.g.cs");

        Assert.Contains(
            "group.MapGet(\"/\", async Task<Results<Ok<PagedResponse<ReadWidgetResponse>>, ProblemHttpResult>> (",
            endpoints
        );
        Assert.Contains("if (page < 1 || pageSize < 1)", endpoints);
        Assert.Contains(
            "return EZRestAPIProblems.Unprocessable(\"page and pageSize must be >= 1.\");",
            endpoints
        );
        Assert.Contains("pageSize = System.Math.Min(pageSize, 100);", endpoints);
    }
}
