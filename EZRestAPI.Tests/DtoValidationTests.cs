namespace EZRestAPI.Tests;

using System;

public class DtoValidationTests
{
    private const string Source = """
        namespace Tests;

        using System.ComponentModel.DataAnnotations;

        [EZRestAPI.Model("Author", "Authors")]
        public partial class AuthorModel { public required string Name { get; set; } }

        [EZRestAPI.Model("Book", "Books")]
        public partial class BookModel
        {
            [MaxLength(255)]
            public required string Title { get; set; }

            public string? Subtitle { get; set; }

            public required int AuthorId { get; set; }

            public int? Pages { get; set; }
        }
        """;

    private const string RequiredAttr = "[System.ComponentModel.DataAnnotations.Required]";
    private const string MaxLengthAttr =
        "[System.ComponentModel.DataAnnotations.MaxLengthAttribute(255)]";

    private static int Count(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    [Fact]
    public void FlatCreateRequest_CopiesDataAnnotations()
    {
        var result = GeneratorHarness.Run(Source);
        var dto = GeneratorHarness.GetSource(result, "CreateBookRequest.g.cs");
        Assert.Contains(MaxLengthAttr, dto);
    }

    [Fact]
    public void FlatUpdateRequest_CopiesDataAnnotations()
    {
        var result = GeneratorHarness.Run(Source);
        var dto = GeneratorHarness.GetSource(result, "UpdateBookRequest.g.cs");
        Assert.Contains(MaxLengthAttr, dto);
    }

    [Fact]
    public void NestedCreateRequest_CopiesDataAnnotations()
    {
        var result = GeneratorHarness.Run(Source);
        var dto = GeneratorHarness.GetSource(result, "CreateBookUnderAuthorRequest.g.cs");
        Assert.Contains(MaxLengthAttr, dto);
    }

    [Fact]
    public void NestedUpdateRequest_CopiesDataAnnotations()
    {
        var result = GeneratorHarness.Run(Source);
        var dto = GeneratorHarness.GetSource(result, "UpdateBookUnderAuthorRequest.g.cs");
        Assert.Contains(MaxLengthAttr, dto);
    }

    [Fact]
    public void ResponseDtos_DoNotCarryValidationAttributes()
    {
        var result = GeneratorHarness.Run(Source);
        var read = GeneratorHarness.GetSource(result, "ReadBookResponse.g.cs");
        var create = GeneratorHarness.GetSource(result, "CreateBookResponse.g.cs");
        Assert.DoesNotContain(MaxLengthAttr, read);
        Assert.DoesNotContain(RequiredAttr, read);
        Assert.DoesNotContain(MaxLengthAttr, create);
        Assert.DoesNotContain(RequiredAttr, create);
    }

    [Fact]
    public void SynthesizedRequired_OnNonNullableReferenceProperty()
    {
        var result = GeneratorHarness.Run(Source);
        var dto = GeneratorHarness.GetSource(result, "CreateBookRequest.g.cs");
        // Title is a non-nullable string -> a [Required] is synthesized alongside
        // the copied [MaxLength]. It is the only property that qualifies.
        Assert.Contains(RequiredAttr, dto);
        Assert.Equal(1, Count(dto, RequiredAttr));
    }

    [Fact]
    public void SynthesizedRequired_AbsentOnValueTypeProperties()
    {
        var result = GeneratorHarness.Run(Source);
        var dto = GeneratorHarness.GetSource(result, "CreateBookRequest.g.cs");
        // AuthorId (int) and Pages (int?) are value types: no [Required] leads them.
        Assert.DoesNotContain(RequiredAttr, LeadingAttributes(dto, "AuthorId"));
        Assert.DoesNotContain(RequiredAttr, LeadingAttributes(dto, "Pages"));
    }

    [Fact]
    public void SynthesizedRequired_AbsentOnNullableReferenceProperty()
    {
        var result = GeneratorHarness.Run(Source);
        var dto = GeneratorHarness.GetSource(result, "CreateBookRequest.g.cs");
        // Subtitle is a nullable string: no synthesized [Required] leads it.
        Assert.DoesNotContain(RequiredAttr, LeadingAttributes(dto, "Subtitle"));
    }

    [Fact]
    public void ValidationHelper_GeneratedExactlyOnce()
    {
        var result = GeneratorHarness.Run(Source);
        var count = result
            .Results.SelectMany(r => r.GeneratedSources)
            .Count(s => s.HintName == "EZRestAPIValidation.g.cs");
        Assert.Equal(1, count);
        var helper = GeneratorHarness.GetSource(result, "EZRestAPIValidation.g.cs");
        Assert.Contains("public static class EZRestAPIValidation", helper);
        Assert.Contains("TryValidateObject", helper);
    }

    [Fact]
    public void ProblemsHelper_GeneratedExactlyOnce()
    {
        var result = GeneratorHarness.Run(Source);
        var count = result
            .Results.SelectMany(r => r.GeneratedSources)
            .Count(s => s.HintName == "EZRestAPIProblems.g.cs");
        Assert.Equal(1, count);
        var helper = GeneratorHarness.GetSource(result, "EZRestAPIProblems.g.cs");
        Assert.Contains("public static class EZRestAPIProblems", helper);
        Assert.Contains("\"unprocessableEntity\"", helper);
        Assert.Contains("\"notFound\"", helper);
        Assert.Contains("\"conflict\"", helper);
    }

    private static string LeadingAttributes(string dto, string propertyName)
    {
        // Returns the contiguous run of attribute lines ([...]) immediately
        // above the property declaration, so a caller can assert what leads it.
        var lines = dto.Replace("\r\n", "\n").Split('\n');
        var declIndex = Array.FindIndex(lines, l => l.Contains($" {propertyName} "));
        if (declIndex < 0)
        {
            return string.Empty;
        }

        var attrs = new System.Collections.Generic.List<string>();
        for (var i = declIndex - 1; i >= 0 && lines[i].TrimStart().StartsWith("["); i--)
        {
            attrs.Add(lines[i].Trim());
        }

        return string.Join("\n", attrs);
    }
}
