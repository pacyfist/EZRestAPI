namespace EZRestAPI.Tests;

public class NestedDtoTests
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
    public void NestedCreateDto_OmitsForeignKey()
    {
        var result = GeneratorHarness.Run(Source);
        var dto = GeneratorHarness.GetSource(result, "CreateBookUnderAuthorRequest.g.cs");
        Assert.Contains("public class CreateBookUnderAuthorRequest", dto);
        Assert.Contains("Title", dto);
        Assert.DoesNotContain("AuthorId", dto);
    }

    [Fact]
    public void NestedUpdateDto_OmitsForeignKeyAndId()
    {
        var result = GeneratorHarness.Run(Source);
        var dto = GeneratorHarness.GetSource(result, "UpdateBookUnderAuthorRequest.g.cs");
        Assert.Contains("public class UpdateBookUnderAuthorRequest", dto);
        Assert.Contains("Title", dto);
        Assert.DoesNotContain("AuthorId", dto);
        Assert.DoesNotContain("public int Id", dto);
    }
}
