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
        Assert.Contains("Results.Conflict()", endpoints);
    }
}
