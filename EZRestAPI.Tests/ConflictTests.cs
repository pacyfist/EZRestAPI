namespace EZRestAPI.Tests;

public class ConflictTests
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
    public void WriteResultEnum_IsGenerated()
    {
        var result = GeneratorHarness.Run(Source);
        var enumSrc = GeneratorHarness.GetSource(result, "WriteResult.g.cs");
        Assert.Contains("enum WriteResult", enumSrc);
        Assert.Contains("Conflict", enumSrc);
    }

    [Fact]
    public void ChildCreate_PrechecksParentAndReturnsNullableInt()
    {
        var result = GeneratorHarness.Run(Source);
        var repo = GeneratorHarness.GetSource(result, "BookRepository.g.cs");
        Assert.Contains(
            "public async Task<int?> CreateAsync(CreateBookRequest request, CancellationToken cancellationToken)",
            repo
        );
        Assert.Contains("context.Authors.AnyAsync(p => p.Id == request.AuthorId", repo);
    }

    [Fact]
    public void ParentDelete_ChecksDependentsAndReturnsWriteResult()
    {
        var result = GeneratorHarness.Run(Source);
        var repo = GeneratorHarness.GetSource(result, "AuthorRepository.g.cs");
        Assert.Contains(
            "public async Task<WriteResult> DeleteAsync(int id, CancellationToken cancellationToken)",
            repo
        );
        Assert.Contains("context.Books.AnyAsync(c => c.AuthorId == id", repo);
    }

    [Fact]
    public void UnrelatedModel_KeepsSimpleSignatures()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;
            [EZRestAPI.Model("Tag", "Tags")]
            public partial class TagModel { public required string Name { get; set; } }
            """
        );
        var repo = GeneratorHarness.GetSource(result, "TagRepository.g.cs");
        Assert.Contains(
            "public async Task<int> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken)",
            repo
        );
        Assert.Contains(
            "public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)",
            repo
        );
    }
}
