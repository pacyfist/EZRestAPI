namespace EZRestAPI.Tests;

public class NestedDtoTests
{
    private const string Source = """
        namespace Tests;

        [EZRestAPI.Model("Author", "Authors", Endpoints = EZRestAPI.Endpoints.All)]
        public partial class AuthorModel { public required string Name { get; set; } }

        [EZRestAPI.Model("Book", "Books", Endpoints = EZRestAPI.Endpoints.All)]
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

    /// <summary>
    /// A read-only collection interface is a supported nested shape on a plain
    /// [Model], mapped as OwnsMany with the element exposed as its {Nested}Dto.
    /// It needs a setter like any other model property.
    /// </summary>
    [Fact]
    public void ReadOnlyListOfNested_OnAModel_MapsAsOwnsManyOfTheDto()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            using System.Collections.Generic;

            [EZRestAPI.Nested("Line")]
            public class LineModel
            {
                public required string Sku { get; set; }
            }

            [EZRestAPI.Model("Basket", "Baskets", Endpoints = EZRestAPI.Endpoints.All)]
            public partial class BasketModel
            {
                public required IReadOnlyList<LineModel> Lines { get; set; }
            }
            """
        );

        Assert.Contains(
            "entity.OwnsMany(e => e.Lines);",
            GeneratorHarness.GetSource(result, "CustomDbContext.g.cs")
        );

        var read = GeneratorHarness.GetSource(result, "ReadBasketResponse.g.cs");
        Assert.Contains("LineDto", read);
        Assert.DoesNotContain("LineModel", read);
    }
}
