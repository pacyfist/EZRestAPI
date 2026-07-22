namespace EZRestAPI.Tests;

public class RelationshipTests
{
    private const string AuthorBookSource = """
        namespace Tests;

        [EZRestAPI.Model("Author", "Authors")]
        public partial class AuthorModel
        {
            public required string Name { get; set; }
        }

        [EZRestAPI.Model("Book", "Books")]
        public partial class BookModel
        {
            public required string Title { get; set; }
            public required int AuthorId { get; set; }
        }
        """;

    [Fact]
    public void ForeignKeyByConvention_ConfiguresRestrictedRelationship()
    {
        var result = GeneratorHarness.Run(AuthorBookSource);
        var context = GeneratorHarness.GetSource(result, "CustomDbContext.g.cs");

        Assert.Contains(".HasOne<global::Tests.AuthorModel>()", context);
        Assert.Contains(".WithMany()", context);
        Assert.Contains(".HasForeignKey(e => e.AuthorId)", context);
        Assert.Contains(".OnDelete(DeleteBehavior.Restrict)", context);
    }

    [Fact]
    public void NonIntIdNamedProperty_IsNotAForeignKey()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Author", "Authors")]
            public partial class AuthorModel
            {
                public required string Name { get; set; }
                public System.Guid OrderId { get; set; }
            }
            """
        );
        var context = GeneratorHarness.GetSource(result, "CustomDbContext.g.cs");

        Assert.DoesNotContain("HasForeignKey", context);
    }

    [Fact]
    public void ScalarOptOut_SuppressesForeignKey()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Author", "Authors")]
            public partial class AuthorModel
            {
                public required string Name { get; set; }
            }

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel
            {
                public required string Title { get; set; }
                [EZRestAPI.Scalar]
                public required int AuthorId { get; set; }
            }
            """
        );
        var context = GeneratorHarness.GetSource(result, "CustomDbContext.g.cs");

        Assert.DoesNotContain("HasForeignKey", context);
    }
}
