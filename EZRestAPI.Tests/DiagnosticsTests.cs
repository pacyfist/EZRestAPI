namespace EZRestAPI.Tests;

public class DiagnosticsTests
{
    [Fact]
    public void ValidModel_ReportsNoDiagnostics()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Thing", "Things")]
            public partial class ThingModel
            {
                public required string Name { get; set; }
            }
            """
        );

        Assert.Empty(GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void NotPartialModel_ReportsEZR001()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Thing", "Things")]
            public class ThingModel
            {
                public required string Name { get; set; }
            }
            """
        );

        Assert.Contains("EZR001", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void DuplicateSingularName_ReportsEZR002()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Thing", "Things")]
            public partial class ThingModel
            {
                public required string Name { get; set; }
            }

            [EZRestAPI.Model("Thing", "OtherThings")]
            public partial class OtherThingModel
            {
                public required string Name { get; set; }
            }
            """
        );

        Assert.Contains("EZR002", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void DuplicatePluralName_ReportsEZR003()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Thing", "Things")]
            public partial class ThingModel
            {
                public required string Name { get; set; }
            }

            [EZRestAPI.Model("OtherThing", "Things")]
            public partial class OtherThingModel
            {
                public required string Name { get; set; }
            }
            """
        );

        Assert.Contains("EZR003", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void ModelUsedAsNavigation_ReportsEZR004()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            using System.Collections.Generic;

            [EZRestAPI.Model("Author", "Authors")]
            public partial class AuthorModel
            {
                public required string Name { get; set; }
            }

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel
            {
                public required string Title { get; set; }

                public required AuthorModel Author { get; set; }

                public required List<AuthorModel> Reviewers { get; set; }
            }
            """
        );

        Assert.Contains("EZR004", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void NestedCycle_ReportsEZR005()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            using System.Collections.Generic;

            [EZRestAPI.Model("Node", "Nodes")]
            public partial class NodeModel
            {
                public required List<BranchModel> Branches { get; set; }
            }

            [EZRestAPI.Nested("Branch")]
            public class BranchModel
            {
                public required string Name { get; set; }

                public required List<BranchModel> Children { get; set; }
            }
            """
        );

        Assert.Contains("EZR005", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void NonIntUserDeclaredId_ReportsEZR007()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Thing", "Things")]
            public partial class ThingModel
            {
                public System.Guid Id { get; set; }

                public required string Name { get; set; }
            }
            """
        );

        Assert.Contains("EZR007", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void NameThatIsNotAnIdentifier_ReportsEZR008()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("blog post", "blog posts")]
            public partial class BlogPostModel
            {
                public required string Title { get; set; }
            }
            """
        );

        Assert.Contains("EZR008", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void UnsupportedNestedContainer_ReportsEZR009()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            using System.Collections.Generic;

            [EZRestAPI.Model("Post", "Posts")]
            public partial class PostModel
            {
                public required HashSet<CommentModel> Comments { get; set; }

                public required CommentModel[] Pinned { get; set; }
            }

            [EZRestAPI.Nested("Comment")]
            public class CommentModel
            {
                public required string Text { get; set; }
            }
            """
        );

        Assert.Contains("EZR009", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void ModelAndNestedOnSameClass_ReportsEZR010()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Author", "Authors")]
            [EZRestAPI.Nested("Author")]
            public partial class AuthorModel
            {
                public required string Name { get; set; }
            }
            """
        );

        Assert.Contains("EZR010", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void ModelArrayNavigation_ReportsEZR004()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            using System.Collections.Generic;

            [EZRestAPI.Model("Author", "Authors")]
            public partial class AuthorModel
            {
                public required string Name { get; set; }
            }

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel
            {
                public required AuthorModel[] Authors { get; set; }

                public required Dictionary<int, AuthorModel> AuthorsById { get; set; }
            }
            """
        );

        Assert.Contains("EZR004", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void CaseInsensitiveDuplicatePluralName_ReportsEZR003()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Post", "Posts")]
            public partial class PostModel
            {
                public required string Title { get; set; }
            }

            [EZRestAPI.Model("Poster", "POSTS")]
            public partial class PosterModel
            {
                public required string Name { get; set; }
            }
            """
        );

        Assert.Contains("EZR003", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void DuplicateNestedName_ReportsEZR006()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Nested("Part")]
            public class PartModel
            {
                public required string Name { get; set; }
            }

            [EZRestAPI.Nested("Part")]
            public class OtherPartModel
            {
                public required string Name { get; set; }
            }
            """
        );

        Assert.Contains("EZR006", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void ForeignKeyShapedWithoutMatchingModel_ReportsEZR011()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel
            {
                public required string Title { get; set; }
                public required int PublisherId { get; set; }
            }
            """
        );

        Assert.Contains("EZR011", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void GuidIdWithoutModel_DoesNotReportEZR011()
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

        Assert.DoesNotContain("EZR011", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void ResolvedForeignKey_DoesNotReportEZR011()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Author", "Authors")]
            public partial class AuthorModel { public required string Name { get; set; } }

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel
            {
                public required string Title { get; set; }
                public required int AuthorId { get; set; }
            }
            """
        );

        Assert.DoesNotContain("EZR011", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void ScalarOptOutForeignKeyShape_DoesNotReportEZR011()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel
            {
                public required string Title { get; set; }
                [EZRestAPI.Scalar]
                public required int PublisherId { get; set; }
            }
            """
        );

        Assert.DoesNotContain("EZR011", GeneratorHarness.DiagnosticIds(result));
    }
}
