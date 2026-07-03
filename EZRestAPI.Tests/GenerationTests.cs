namespace EZRestAPI.Tests;

using EZRestAPI.Utils;

public class GenerationTests
{
    [Fact]
    public void SinglePropertyModel_GeneratesDtoBasedRepository()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Tag", "Tags")]
            public partial class TagModel
            {
                public required string Name { get; set; }
            }
            """
        );

        var repository = GeneratorHarness.GetSource(result, "TagRepository.g.cs");

        Assert.Contains(
            "public async Task<ReadTagResponse?> ReadAsync(int id, CancellationToken cancellationToken)",
            repository
        );
        Assert.Contains(
            "public async Task<int> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken)",
            repository
        );
        Assert.DoesNotContain("(string Name)", repository);
    }

    [Fact]
    public void ZeroPropertyModel_GeneratesValidSignatures()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Marker", "Markers")]
            public partial class MarkerModel
            {
            }
            """
        );

        var repository = GeneratorHarness.GetSource(result, "MarkerRepository.g.cs");

        Assert.Contains(
            "CreateAsync(CreateMarkerRequest request, CancellationToken cancellationToken)",
            repository
        );
        Assert.DoesNotContain("(, ", repository);
    }

    [Fact]
    public void KeywordNamedProperty_GeneratesCompilableIdentifiers()
    {
        Assert.Equal("@event", "Event".ToCamelCase());
        Assert.Equal("@class", "Class".ToCamelCase());
        Assert.Equal("name", "Name".ToCamelCase());

        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Meeting", "Meetings")]
            public partial class MeetingModel
            {
                public required string Event { get; set; }
            }
            """
        );

        var repository = GeneratorHarness.GetSource(result, "MeetingRepository.g.cs");

        Assert.Contains("Event = request.Event", repository);
    }

    [Fact]
    public void NestedModel_GeneratesDtoMapperAndOwnedConfiguration()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            using System.Collections.Generic;

            [EZRestAPI.Model("Post", "Posts")]
            public partial class PostModel
            {
                public required string Title { get; set; }

                public required List<CommentModel> Comments { get; set; }
            }

            [EZRestAPI.Nested("Comment")]
            public class CommentModel
            {
                public required string Text { get; set; }
            }
            """
        );

        var dto = GeneratorHarness.GetSource(result, "CommentDto.g.cs");
        Assert.Contains("public class CommentDto", dto);
        Assert.Contains("public static class CommentMapper", dto);

        var dbContext = GeneratorHarness.GetSource(result, "CustomDbContext.g.cs");
        Assert.Contains("entity.OwnsMany(e => e.Comments);", dbContext);

        var repository = GeneratorHarness.GetSource(result, "PostRepository.g.cs");
        Assert.Contains("request.Comments.Select(CommentMapper.ToEntity).ToList()", repository);
    }

    [Fact]
    public void Model_GeneratesEndpointsAndBootstrap()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Tag", "Tags")]
            public partial class TagModel
            {
                public required string Name { get; set; }
            }
            """
        );

        var endpoints = GeneratorHarness.GetSource(result, "TagEndpoints.g.cs");
        Assert.Contains("app.MapGroup(\"/tags\")", endpoints);
        Assert.Contains("group.MapPost(\"/\"", endpoints);
        Assert.Contains("group.MapGet(\"/{id:int}\"", endpoints);
        Assert.Contains("group.MapPut(\"/{id:int}\"", endpoints);
        Assert.Contains("group.MapDelete(\"/{id:int}\"", endpoints);

        var bootstrap = GeneratorHarness.GetSource(result, "EZRestAPIExtensions.g.cs");
        Assert.Contains("services.AddScoped<TagRepository>();", bootstrap);
        Assert.Contains("app.MapTagEndpoints();", bootstrap);
    }
}
