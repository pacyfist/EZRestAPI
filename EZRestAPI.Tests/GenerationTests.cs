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
    public void AssemblyNameWithInvalidCharacters_IsSanitizedIntoValidNamespace()
    {
        Assert.Equal("my_api", "my-api".ToValidNamespace());
        Assert.Equal("My.Api_2", "My.Api 2".ToValidNamespace());
        Assert.Equal("_3d.Engine", "3d.Engine".ToValidNamespace());
        Assert.Equal("@event", "event".ToValidNamespace());
        Assert.Equal("Example", "Example".ToValidNamespace());
    }

    [Fact]
    public void UserDeclaredIntId_DoesNotDuplicateMembers()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Tag", "Tags")]
            public partial class TagModel
            {
                public int Id { get; set; }

                public required string Name { get; set; }
            }
            """
        );

        Assert.Empty(GeneratorHarness.DiagnosticIds(result));

        // No generated partial re-declares Id...
        Assert.DoesNotContain(
            result.Results.SelectMany(r => r.GeneratedSources),
            s => s.HintName == "TagModel.g.cs"
        );

        // ...and the DTOs carry exactly one Id.
        var response = GeneratorHarness.GetSource(result, "CreateTagResponse.g.cs");
        Assert.Equal(1, CountOccurrences(response, " Id "));
    }

    [Fact]
    public void NullableNestedCollection_KeepsNullabilityAndGuards()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            using System.Collections.Generic;

            [EZRestAPI.Model("Post", "Posts")]
            public partial class PostModel
            {
                public required string Title { get; set; }

                public List<CommentModel>? Comments { get; set; }
            }

            [EZRestAPI.Nested("Comment")]
            public class CommentModel
            {
                public required string Text { get; set; }
            }
            """
        );

        Assert.Empty(GeneratorHarness.DiagnosticIds(result));

        var request = GeneratorHarness.GetSource(result, "CreatePostRequest.g.cs");
        Assert.Contains("System.Collections.Generic.List<CommentDto>? Comments", request);

        var repository = GeneratorHarness.GetSource(result, "PostRepository.g.cs");
        Assert.Contains(
            "request.Comments is null ? null : request.Comments.Select(CommentMapper.ToEntity).ToList()",
            repository
        );
        Assert.Contains(
            "entity.Comments is null ? null : entity.Comments.Select(CommentMapper.ToDto).ToList()",
            repository
        );
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    [Fact]
    public void IncompleteAttributeArguments_DoNotCrashGenerators()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model]
            public partial class ThingModel
            {
                public required string Name { get; set; }
            }
            """
        );

        Assert.All(result.Results, r => Assert.Null(r.Exception));
    }

    [Fact]
    public void ComputedStaticAndIndexerProperties_AreExcluded()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Person", "People")]
            public partial class PersonModel
            {
                public required string First { get; set; }

                public string DisplayName => First.ToUpperInvariant();

                public static int InstanceCount { get; set; }

                public string this[int index]
                {
                    get => First;
                    set => First = value;
                }

                private int Secret { get; set; }
            }
            """
        );

        Assert.All(result.Results, r => Assert.Null(r.Exception));

        var repository = GeneratorHarness.GetSource(result, "PersonRepository.g.cs");
        Assert.DoesNotContain("DisplayName", repository);
        Assert.DoesNotContain("InstanceCount", repository);
        Assert.DoesNotContain("this[]", repository);
        Assert.DoesNotContain("Secret", repository);
        Assert.Contains("First = request.First", repository);
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
