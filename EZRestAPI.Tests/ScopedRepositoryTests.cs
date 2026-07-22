namespace EZRestAPI.Tests;

public class ScopedRepositoryTests
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
    public void ScopedMethods_AreGenerated()
    {
        var result = GeneratorHarness.Run(Source);
        var repo = GeneratorHarness.GetSource(result, "BookRepository.g.cs");

        Assert.Contains(
            "Task<PagedResponse<ReadBookResponse>?> ListBookByAuthorAsync(int parentId, int page, int pageSize, CancellationToken cancellationToken)",
            repo
        );
        Assert.Contains(
            "Task<int?> CreateBookUnderAuthorAsync(int parentId, CreateBookUnderAuthorRequest request, CancellationToken cancellationToken)",
            repo
        );
        Assert.Contains(
            "Task<ReadBookResponse?> ReadBookUnderAuthorAsync(int parentId, int id, CancellationToken cancellationToken)",
            repo
        );
        Assert.Contains(
            "Task<bool> UpdateBookUnderAuthorAsync(int parentId, int id, UpdateBookUnderAuthorRequest request, CancellationToken cancellationToken)",
            repo
        );
        Assert.Contains(
            "Task<WriteResult> DeleteBookUnderAuthorAsync(int parentId, int id, CancellationToken cancellationToken)",
            repo
        );
        Assert.Contains("e.AuthorId == parentId", repo);
        Assert.Contains("AuthorId = parentId,", repo);
    }
}
