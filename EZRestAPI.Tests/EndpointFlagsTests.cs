namespace EZRestAPI.Tests;

using System.Text.RegularExpressions;
using EZRestAPI.Providers;
using Microsoft.CodeAnalysis;

public class EndpointFlagsTests
{
    private static string Squash(string source) =>
        new string(source.Where(c => !char.IsWhiteSpace(c)).ToArray());

    [Fact]
    public void EndpointsEnum_IsGenerated()
    {
        var result = GeneratorHarness.Run("namespace Tests;");
        var emitted = Squash(GeneratorHarness.GetSource(result, "EZRestAPIEndpoints.g.cs"));

        Assert.Contains("[Flags]", GeneratorHarness.GetSource(result, "EZRestAPIEndpoints.g.cs"));
        Assert.Contains("publicenumEndpoints", emitted);
        Assert.Contains("Crud=List|Create|Read|Update|Delete,", emitted);
        Assert.Contains("ReadOnly=List|Read|Nested,", emitted);
        Assert.Contains("All=Crud|Nested,", emitted);
    }

    [Fact]
    public void ModelAttribute_ExposesEndpointsProperty()
    {
        var result = GeneratorHarness.Run("namespace Tests;");
        var emitted = Squash(GeneratorHarness.GetSource(result, "EZRestAPIModel.g.cs"));

        Assert.Contains("publicEndpointsEndpoints{get;set;}", emitted);
    }

    /// <summary>
    /// The generator cannot reference the enum it emits, so EndpointFeatures is
    /// a hand-maintained mirror. Drift would misroute every flag while still
    /// compiling, so the two declarations are pinned to each other here: every
    /// member name is derived from reflection on one side and by parsing the
    /// emitted source on the other, and checked in both directions, so a
    /// member added, renamed, or reassigned on either side without the other
    /// fails this test. No member name is hardcoded.
    /// </summary>
    [Fact]
    public void EndpointFeatures_MirrorsGeneratedEnum()
    {
        var result = GeneratorHarness.Run("namespace Tests;");
        var source = GeneratorHarness.GetSource(result, "EZRestAPIEndpoints.g.cs");
        var emitted = ParseEmittedEndpointsEnum(source);

        var mirrorNames = Enum.GetNames(typeof(ProviderExtensions.EndpointFeatures));

        // Every mirror member must be declared in the emitted enum, with the same value.
        foreach (var name in mirrorNames)
        {
            Assert.True(
                emitted.ContainsKey(name),
                $"'{name}' is declared on EndpointFeatures but is missing from the emitted Endpoints enum."
            );

            var mirrorValue = (int)Enum.Parse<ProviderExtensions.EndpointFeatures>(name);
            Assert.Equal(mirrorValue, emitted[name]);
        }

        // Every emitted member must be mirrored — this direction catches a
        // member added to the generated enum but never added to the mirror.
        foreach (var name in emitted.Keys)
        {
            Assert.True(
                mirrorNames.Contains(name),
                $"'{name}' is declared on the emitted Endpoints enum but is missing from EndpointFeatures."
            );
        }
    }

    /// <summary>
    /// Parses the member list out of the emitted <c>enum Endpoints { ... }</c>
    /// body, resolving <c>|</c>-composed presets (e.g.
    /// <c>Crud = List | Create | Read | Update | Delete</c>) against
    /// already-parsed members so every member — atomic or composite — ends up
    /// with a concrete int value. Nothing here is hardcoded against a known
    /// member list, so a member added to the emitted enum shows up too.
    /// </summary>
    private static Dictionary<string, int> ParseEmittedEndpointsEnum(string source)
    {
        var enumKeyword = source.IndexOf("enum Endpoints", StringComparison.Ordinal);
        var openBrace = source.IndexOf('{', enumKeyword);
        var closeBrace = source.IndexOf('}', openBrace);
        var body = source[(openBrace + 1)..closeBrace];

        var values = new Dictionary<string, int>();
        foreach (var rawMember in body.Split(','))
        {
            var member = rawMember.Trim();
            if (member.Length == 0)
            {
                continue;
            }

            var parts = member.Split('=', 2);
            var name = parts[0].Trim();
            var value = parts[1]
                .Split('|')
                .Select(token => token.Trim())
                .Select(token => int.TryParse(token, out var literal) ? literal : values[token])
                .Aggregate(0, (accumulated, next) => accumulated | next);

            values[name] = value;
        }

        return values;
    }

    private const string TwoModels = """
        namespace Tests;

        [EZRestAPI.Model("Author", "Authors", Endpoints = EZRestAPI.Endpoints.All)]
        public partial class AuthorModel { public required string Name { get; set; } }

        [EZRestAPI.Model("Book", "Books", Endpoints = {0})]
        public partial class BookModel
        {
            public required string Title { get; set; }
            public required int AuthorId { get; set; }
        }
        """;

    private static string BookEndpoints(string flags) =>
        GeneratorHarness.GetSource(
            GeneratorHarness.Run(TwoModels.Replace("{0}", flags)),
            "BookEndpoints.g.cs"
        );

    private static bool HasSource(GeneratorDriverRunResult result, string hintName) =>
        result.Results.SelectMany(r => r.GeneratedSources).Any(s => s.HintName == hintName);

    [Fact]
    public void DefaultIsClosed_NoEndpointClassIsGenerated()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel { public required string Title { get; set; } }
            """
        );

        Assert.False(HasSource(result, "BookEndpoints.g.cs"));
    }

    private static string[] HintNames(GeneratorDriverRunResult result) =>
        result
            .Results.SelectMany(r => r.GeneratedSources)
            .Select(s => s.HintName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The spec's claim is that the omitted-argument and explicit-<c>None</c>
    /// cases are the same value by construction (the attribute property's
    /// default is <c>Endpoints.None</c>), not merely that both happen to skip
    /// the endpoint class. Comparing the full generated hint-name set (every
    /// file, for every model/DTO/repository/etc. generator) pins that
    /// equivalence; asserting only on <c>BookEndpoints.g.cs</c>'s absence would
    /// pass even if some other generator treated the two cases differently.
    /// </summary>
    [Fact]
    public void ExplicitNone_MatchesOmittedArgument()
    {
        var omitted = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel { public required string Title { get; set; } }
            """
        );

        var explicitNone = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Book", "Books", Endpoints = EZRestAPI.Endpoints.None)]
            public partial class BookModel { public required string Title { get; set; } }
            """
        );

        Assert.Equal(HintNames(omitted), HintNames(explicitNone));
        Assert.False(HasSource(omitted, "BookEndpoints.g.cs"));
    }

    [Fact]
    public void All_EmitsEveryFlatVerbAndTheNestedGroup()
    {
        var endpoints = BookEndpoints("EZRestAPI.Endpoints.All");

        Assert.Contains("group.MapGet(\"/\"", endpoints);
        Assert.Contains("group.MapPost(\"/\"", endpoints);
        Assert.Contains("group.MapGet(\"/{id:int}\"", endpoints);
        Assert.Contains("group.MapPut(\"/{id:int}\"", endpoints);
        Assert.Contains("group.MapDelete(\"/{id:int}\"", endpoints);
        Assert.Contains("app.MapGroup(\"/authors/{parentId:int}/books\")", endpoints);
    }

    [Theory]
    [InlineData("List", "group.MapGet(\"/\"")]
    [InlineData("Create", "group.MapPost(\"/\"")]
    [InlineData("Read", "group.MapGet(\"/{id:int}\"")]
    [InlineData("Update", "group.MapPut(\"/{id:int}\"")]
    [InlineData("Delete", "group.MapDelete(\"/{id:int}\"")]
    public void SingleVerb_EmitsOnlyThatRoute(string flag, string expected)
    {
        var endpoints = BookEndpoints($"EZRestAPI.Endpoints.{flag}");

        Assert.Contains(expected, endpoints);
        // No nested group: Nested is not part of any single verb.
        Assert.DoesNotContain("app.MapGroup(\"/authors/", endpoints);

        string[] all =
        [
            "group.MapGet(\"/\"",
            "group.MapPost(\"/\"",
            "group.MapGet(\"/{id:int}\"",
            "group.MapPut(\"/{id:int}\"",
            "group.MapDelete(\"/{id:int}\"",
        ];
        foreach (var route in all.Where(r => r != expected))
        {
            Assert.DoesNotContain(route, endpoints);
        }
    }

    [Fact]
    public void ReadOnly_EmitsGetsOnlyIncludingNested()
    {
        var endpoints = BookEndpoints("EZRestAPI.Endpoints.ReadOnly");

        Assert.Contains("group.MapGet(\"/\"", endpoints);
        Assert.Contains("group.MapGet(\"/{id:int}\"", endpoints);
        Assert.Contains("app.MapGroup(\"/authors/{parentId:int}/books\")", endpoints);
        Assert.DoesNotContain("MapPost", endpoints);
        Assert.DoesNotContain("MapPut", endpoints);
        Assert.DoesNotContain("MapDelete", endpoints);
    }

    [Fact]
    public void Crud_EmitsAllFlatVerbsAndNoNestedGroup()
    {
        var endpoints = BookEndpoints("EZRestAPI.Endpoints.Crud");

        Assert.Contains("group.MapPost(\"/\"", endpoints);
        Assert.Contains("group.MapDelete(\"/{id:int}\"", endpoints);
        Assert.DoesNotContain("app.MapGroup(\"/authors/", endpoints);
    }

    [Fact]
    public void ListAndNested_EmitsBothListRoutesAndNothingElse()
    {
        var endpoints = BookEndpoints("EZRestAPI.Endpoints.List | EZRestAPI.Endpoints.Nested");

        Assert.Contains("group.MapGet(\"/\"", endpoints);
        Assert.Contains("app.MapGroup(\"/authors/{parentId:int}/books\")", endpoints);
        Assert.Contains("ListBookByAuthorAsync", endpoints);
        Assert.DoesNotContain("MapPost", endpoints);
        Assert.DoesNotContain("MapPut", endpoints);
        Assert.DoesNotContain("MapDelete", endpoints);
        Assert.DoesNotContain("group.MapGet(\"/{id:int}\"", endpoints);
        Assert.DoesNotContain("ReadBookUnderAuthorAsync", endpoints);
    }

    [Fact]
    public void NestedChild_UnderNoneParent_StillGeneratesNestedRoutes()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Author", "Authors")]
            public partial class AuthorModel { public required string Name { get; set; } }

            [EZRestAPI.Model("Book", "Books", Endpoints = EZRestAPI.Endpoints.ReadOnly)]
            public partial class BookModel
            {
                public required string Title { get; set; }
                public required int AuthorId { get; set; }
            }
            """
        );

        var endpoints = GeneratorHarness.GetSource(result, "BookEndpoints.g.cs");
        Assert.Contains("app.MapGroup(\"/authors/{parentId:int}/books\")", endpoints);

        // The parent is persistence-only but still has a DbSet, which is what
        // the child's parent-existence check queries.
        Assert.False(HasSource(result, "AuthorEndpoints.g.cs"));
        Assert.Contains(
            "DbSet<global::Tests.AuthorModel>",
            GeneratorHarness.GetSource(result, "CustomDbContext.g.cs")
        );
    }

    /// <summary>
    /// A "blank" line inside the generated method body still carries the
    /// pending indent (<see cref="System.CodeDom.Compiler.IndentedTextWriter"/>
    /// writes the indent before the newline), so a doubled separator never
    /// shows up as a literal "\n\n\n" — it shows up as a line of only
    /// whitespace sandwiched between two newlines. This regex catches that;
    /// a literal-newline check would not.
    /// </summary>
    private static void AssertNoRaggedBlankLines(string source) =>
        Assert.False(
            Regex.IsMatch(source.Replace("\r\n", "\n"), "\n[ \t]*\n[ \t]*\n"),
            "Generated file has a doubled (or ragged) blank-line separator."
        );

    [Fact]
    public void GeneratedEndpointFile_HasNoDoubleBlankLines()
    {
        AssertNoRaggedBlankLines(
            BookEndpoints("EZRestAPI.Endpoints.List | EZRestAPI.Endpoints.Delete")
        );
    }

    [Fact]
    public void GeneratedEndpointFile_HasNoDoubleBlankLines_NestedOnly()
    {
        AssertNoRaggedBlankLines(BookEndpoints("EZRestAPI.Endpoints.Nested"));
    }

    [Fact]
    public void GeneratedEndpointFile_HasNoDoubleBlankLines_ReadAndNested()
    {
        AssertNoRaggedBlankLines(
            BookEndpoints("EZRestAPI.Endpoints.Read | EZRestAPI.Endpoints.Nested")
        );
    }

    [Fact]
    public void NestedAlone_EmitsNoEmptyFlatMapGroup()
    {
        var endpoints = BookEndpoints("EZRestAPI.Endpoints.Nested");

        // No verb flag survived, so no flat group is ever read; it must not
        // be emitted either. (The nested group is also absent here, since
        // Nested alone selects no verb for it — see EmitsNestedGroup.)
        Assert.DoesNotContain("app.MapGroup(\"/books\")", endpoints);
        Assert.DoesNotContain("app.MapGroup(\"/authors/", endpoints);
    }

    /// <summary>
    /// "Only" would overstate this: Update | Nested also emits the flat
    /// PUT /books/{id} (see <see cref="SingleVerb_EmitsOnlyThatRoute"/> for
    /// that coverage). This test's actual claim is narrower — of the nested
    /// routes specifically, only the nested Update route is emitted.
    /// </summary>
    [Fact]
    public void UpdateAndNested_EmitsNoOtherNestedVerbs()
    {
        var endpoints = BookEndpoints("EZRestAPI.Endpoints.Update | EZRestAPI.Endpoints.Nested");

        Assert.Contains("UpdateBookUnderAuthorAsync", endpoints);
        Assert.DoesNotContain("CreateBookUnderAuthorAsync", endpoints);
        Assert.DoesNotContain("DeleteBookUnderAuthorAsync", endpoints);
    }

    /// <summary>
    /// Endpoints controls routes and nothing else, so None keeps the full
    /// repository and DTO surface. Withholding them would leave a stored model
    /// with no typed way to reach its own table.
    /// </summary>
    [Fact]
    public void None_StillGeneratesTheRepositoryAndDtos()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel { public required string Title { get; set; } }
            """
        );

        Assert.True(HasSource(result, "BookRepository.g.cs"));
        Assert.True(HasSource(result, "CreateBookRequest.g.cs"));
        Assert.True(HasSource(result, "CreateBookResponse.g.cs"));
        Assert.True(HasSource(result, "ReadBookResponse.g.cs"));
        Assert.True(HasSource(result, "UpdateBookRequest.g.cs"));

        // The routes are the part that None withholds.
        Assert.False(HasSource(result, "BookEndpoints.g.cs"));
    }

    [Fact]
    public void None_StillGeneratesDbContextEntryAndIdPartial()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel { public required string Title { get; set; } }
            """
        );

        Assert.True(HasSource(result, "BookModel.g.cs"));
        Assert.Contains(
            "DbSet<global::Tests.BookModel>",
            GeneratorHarness.GetSource(result, "CustomDbContext.g.cs")
        );
    }

    /// <summary>
    /// Partial flags still generate the complete building blocks. The reason to
    /// withhold POST /books is usually that you are hand-writing it, and that
    /// replacement needs CreateAsync and CreateBookRequest to build on.
    /// </summary>
    [Fact]
    public void PartialFlags_StillGenerateTheCompleteBuildingBlocks()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Book", "Books", Endpoints = EZRestAPI.Endpoints.List)]
            public partial class BookModel { public required string Title { get; set; } }
            """
        );

        Assert.True(HasSource(result, "CreateBookRequest.g.cs"));
        Assert.True(HasSource(result, "UpdateBookRequest.g.cs"));

        var repository = GeneratorHarness.GetSource(result, "BookRepository.g.cs");
        Assert.Contains("CreateAsync", repository);
        Assert.Contains("UpdateAsync", repository);
        Assert.Contains("DeleteAsync", repository);
    }

    [Fact]
    public void SharedDtoInfrastructure_SurvivesAnAllNoneCompilation()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel { public required string Title { get; set; } }
            """
        );

        Assert.True(HasSource(result, "PagedResponse.g.cs"));
        Assert.True(HasSource(result, "WriteResult.g.cs"));
    }

    /// <summary>
    /// A None child still gets its nested DTOs, because its repository still
    /// gets the scoped Create{Child}Under{Parent}Async methods that consume
    /// them. Withholding the DTOs would leave that repository uncompilable.
    /// </summary>
    [Fact]
    public void NoneChild_StillGeneratesItsNestedDtos()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Author", "Authors", Endpoints = EZRestAPI.Endpoints.All)]
            public partial class AuthorModel { public required string Name { get; set; } }

            [EZRestAPI.Model("Book", "Books", Endpoints = EZRestAPI.Endpoints.None)]
            public partial class BookModel
            {
                public required string Title { get; set; }
                public required int AuthorId { get; set; }
            }
            """
        );

        Assert.True(HasSource(result, "CreateBookUnderAuthorRequest.g.cs"));
        Assert.True(HasSource(result, "UpdateBookUnderAuthorRequest.g.cs"));
    }

    /// <summary>
    /// A None parent does not suppress a child's nested DTOs either. This is the
    /// "child under a None parent" consequence —
    /// <see cref="NestedChild_UnderNoneParent_StillGeneratesNestedRoutes"/>
    /// covers it for routes, this covers it for DTOs.
    /// </summary>
    [Fact]
    public void AllChild_UnderNoneParent_StillGeneratesItsNestedDtos()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Author", "Authors", Endpoints = EZRestAPI.Endpoints.None)]
            public partial class AuthorModel { public required string Name { get; set; } }

            [EZRestAPI.Model("Book", "Books", Endpoints = EZRestAPI.Endpoints.All)]
            public partial class BookModel
            {
                public required string Title { get; set; }
                public required int AuthorId { get; set; }
            }
            """
        );

        Assert.True(HasSource(result, "CreateBookUnderAuthorRequest.g.cs"));
        Assert.True(HasSource(result, "UpdateBookUnderAuthorRequest.g.cs"));
    }

    /// <summary>
    /// The bootstrap splits: every model's repository is registered so it can be
    /// injected, but only non-None models are mapped. Mapping a None model would
    /// call a Map{Name}Endpoints() that was never generated.
    /// </summary>
    [Fact]
    public void Bootstrap_RegistersEveryRepositoryButMapsOnlyNonNone()
    {
        var result = GeneratorHarness.Run(
            """
            namespace Tests;

            [EZRestAPI.Model("Author", "Authors")]
            public partial class AuthorModel { public required string Name { get; set; } }

            [EZRestAPI.Model("Book", "Books", Endpoints = EZRestAPI.Endpoints.All)]
            public partial class BookModel { public required string Title { get; set; } }
            """
        );

        var bootstrap = GeneratorHarness.GetSource(result, "EZRestAPIExtensions.g.cs");

        Assert.Contains("services.AddScoped<BookRepository>();", bootstrap);
        Assert.Contains("services.AddScoped<AuthorRepository>();", bootstrap);

        Assert.Contains("app.MapBookEndpoints();", bootstrap);
        Assert.DoesNotContain("MapAuthorEndpoints", bootstrap);
    }

    private static string[] DiagnosticsFor(string flags)
    {
        var source = $$"""
            namespace Tests;

            [EZRestAPI.Model("Book", "Books"{{flags}})]
            public partial class BookModel { public required string Title { get; set; } }
            """;

        return GeneratorHarness.DiagnosticIds(GeneratorHarness.Run(source));
    }

    /// <summary>
    /// Like <see cref="DiagnosticsFor"/>, but returns the raw <see cref="Diagnostic"/>
    /// objects so callers can force <c>.GetMessage()</c>. Roslyn formats a
    /// diagnostic's message lazily via <c>string.Format</c> against
    /// <c>DiagnosticDescriptor.MessageFormat</c>, so asserting on
    /// <see cref="DiagnosticsFor"/>'s ids alone would never catch a malformed
    /// format string (e.g. an unescaped literal <c>{id}</c> token).
    /// </summary>
    private static Diagnostic[] RawDiagnosticsFor(string flags)
    {
        var source = $$"""
            namespace Tests;

            [EZRestAPI.Model("Book", "Books"{{flags}})]
            public partial class BookModel { public required string Title { get; set; } }
            """;

        return GeneratorHarness.Run(source).Diagnostics.ToArray();
    }

    [Fact]
    public void EZR012_FiresWhenCreateIsSetWithoutRead()
    {
        Assert.Contains("EZR012", DiagnosticsFor(", Endpoints = EZRestAPI.Endpoints.Create"));
    }

    [Theory]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.Create")]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.Create | EZRestAPI.Endpoints.Update")]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.Create | EZRestAPI.Endpoints.Delete")]
    public void EZR012_FiresForAnyCombinationWithCreateAndWithoutRead(string flags)
    {
        Assert.Contains("EZR012", DiagnosticsFor(flags));
    }

    [Theory]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.All")]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.ReadOnly")]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.None")]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.Create | EZRestAPI.Endpoints.Read")]
    [InlineData("")]
    public void EZR012_DoesNotFireOtherwise(string flags)
    {
        Assert.DoesNotContain("EZR012", DiagnosticsFor(flags));
    }

    /// <summary>
    /// Forces <c>Diagnostic.GetMessage()</c> to run <c>string.Format</c> against
    /// the EZR012 message format, which is the only way to catch the
    /// unescaped-<c>{id}</c> hazard: an id-only assertion would still pass
    /// even if <c>string.Format</c> threw when a real consumer (an IDE, or
    /// <c>msbuild</c>) rendered the message. This also pins the route segment
    /// to the same lowercased plural casing <c>EndpointsGenerator.Route</c>
    /// uses for the real <c>TypedResults.Created</c> call, so the message
    /// text can't drift from the actual generated URL.
    /// </summary>
    [Fact]
    public void EZR012_MessageIsFormattedCorrectlyWithLiteralIdToken()
    {
        var diagnostic = RawDiagnosticsFor(", Endpoints = EZRestAPI.Endpoints.Create")
            .Single(d => d.Id == "EZR012");

        Assert.Equal(
            "Model 'BookModel' generates a Create endpoint whose Location header points at '/books/{id}', but Endpoints.Read is not set",
            diagnostic.GetMessage()
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.None")]
    public void EZR013_FiresWhenNoEndpointsAreSelected(string flags)
    {
        Assert.Contains("EZR013", DiagnosticsFor(flags));
    }

    /// <summary>
    /// Same rationale as <see cref="EZR012_MessageIsFormattedCorrectlyWithLiteralIdToken"/>:
    /// forces <c>string.Format</c> to actually run against the EZR013 message
    /// format rather than trusting an id-only assertion.
    /// </summary>
    [Fact]
    public void EZR013_MessageIsFormattedCorrectly()
    {
        var diagnostic = RawDiagnosticsFor("").Single(d => d.Id == "EZR013");

        Assert.Equal(
            "Model 'BookModel' generates its table, DTOs and repository but no routes; set Endpoints on [EZRestAPI.Model] to publish some",
            diagnostic.GetMessage()
        );
    }

    [Theory]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.All")]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.ReadOnly")]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.List")]
    public void EZR013_DoesNotFireForAnyNonZeroCombination(string flags)
    {
        Assert.DoesNotContain("EZR013", DiagnosticsFor(flags));
    }

    /// <summary>
    /// Endpoints.Nested alone sets no flat verb, and EmitsNestedGroup requires
    /// Nested AND at least one verb, so this selects no route at all — yet the
    /// endpoint CLASS is still generated, since flags != None. That is the gap
    /// EZR014 exists to flag; EZR013 only fires on flags == None.
    /// </summary>
    [Fact]
    public void EZR014_FiresWhenNestedIsSetAloneWithNoVerb()
    {
        Assert.Contains("EZR014", DiagnosticsFor(", Endpoints = EZRestAPI.Endpoints.Nested"));
    }

    /// <summary>
    /// Endpoints.List selects a verb, so a route is generated and EZR014 must
    /// not fire. This guards against a broadened condition that accidentally
    /// fires whenever Nested is absent, rather than only when no verb survives.
    /// </summary>
    [Fact]
    public void EZR014_DoesNotFireWhenAVerbIsSelected()
    {
        Assert.DoesNotContain("EZR014", DiagnosticsFor(", Endpoints = EZRestAPI.Endpoints.List"));
    }

    /// <summary>
    /// flags == None must keep firing EZR013, not EZR014: the two are mutually
    /// exclusive branches over the same "no routes generated" family, and None
    /// is the pre-existing, already-tested case (no repository/DTOs either).
    /// </summary>
    [Fact]
    public void EZR013_StillFiresForNoneNotEZR014()
    {
        var ids = DiagnosticsFor(", Endpoints = EZRestAPI.Endpoints.None");

        Assert.Contains("EZR013", ids);
        Assert.DoesNotContain("EZR014", ids);
    }

    /// <summary>
    /// Same rationale as <see cref="EZR013_MessageIsFormattedCorrectly"/>:
    /// forces <c>string.Format</c> to actually run against the EZR014 message
    /// format rather than trusting an id-only assertion.
    /// </summary>
    [Fact]
    public void EZR014_MessageIsFormattedCorrectly()
    {
        var diagnostic = RawDiagnosticsFor(", Endpoints = EZRestAPI.Endpoints.Nested")
            .Single(d => d.Id == "EZR014");

        Assert.Equal(
            "Model 'BookModel' selects no endpoint verbs, so no routes are generated; add at least one of List, Create, Read, Update or Delete",
            diagnostic.GetMessage()
        );
    }
}
