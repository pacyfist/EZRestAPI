# Endpoint Flags for `[Model]` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `[EZRestAPI.Model]` an opt-in `[Flags] Endpoints` property that selects which endpoints the generator emits, where `Endpoints.None` (the default) generates DbContext registration only.

**Architecture:** `AttributesGenerator` emits a new public `EZRestAPI.Endpoints` enum and an `Endpoints` property on `ModelAttribute`. `ProviderExtensions` declares a mirror enum (`EndpointFeatures`), parses the attribute's named argument into it, and carries it on the `Model` record. `EndpointsGenerator` gates each route on the flags; `RepositoryGenerator`, `DtoGenerator` and `BootstrapGenerator` skip `None` models entirely. `ModelGenerator`, `DbContextGenerator` and `NestedGenerator` are untouched, so a `None` model keeps its `DbSet`, EF configuration and generated `Id`.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generators (netstandard2.0 analyzer), xUnit, EF Core, ASP.NET Core Minimal APIs.

**Spec:** `docs/superpowers/specs/2026-07-25-endpoint-flags-design.md` — read it before Task 1.

## Global Constraints

- **Build and test commands** (the sandbox needs both workarounds or `dotnet` fails):
  ```bash
  export NUGET_PACKAGES="$PWD/.nuget-cache"
  FLAGS="-p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q"
  dotnet test  EZRestAPI.Tests $FLAGS          # fast generator suite, ~1s, no Docker
  dotnet build Example/Example.csproj $FLAGS   # REAL codegen gate: compiles generated output
  ```
- `dotnet build Example.Tests` needs `-m:1` (multi-node `GetTargetFrameworks` race).
- `Example.Tests` (Testcontainers/SQL Server) **cannot run in the sandbox** — no Docker. Write those tests, do not attempt to run them, and say so when reporting.
- The generator project targets **netstandard2.0**. No `System.Text.Json`, no file I/O, no `Enum.Parse<T>()` in generator code (it is fine in tests, which target .NET 10).
- The generator **cannot reference** `EZRestAPI.Endpoints` — that type exists only in the consuming compilation. All generator-side flag logic uses the `EndpointFeatures` mirror.
- Never use `flags.HasFlag(...)` in generator code (boxes on every call, runs per model per generator). Use the `Has` helper defined in Task 1.
- Existing test count is **104 passing** fast tests. Every task must leave that suite green (the number grows as tasks add tests; it must never shrink).
- Follow the existing file style: file-scoped namespaces, `using` directives *after* the namespace declaration, four-space indent.

---

### Task 1: Emit the `Endpoints` enum, the attribute property, and parse it

**Files:**
- Modify: `EZRestAPI/Generators/AttributesGenerator.cs:12-36` (the `EZRestAPIModel.g.cs` block) and add a new `AddSource` block
- Modify: `EZRestAPI/Providers/ProviderExtensions.cs:36-54` (near `NestedKind` and the `Model` record) and `:390-423` (`CreateModel`)
- Test: `EZRestAPI.Tests/EndpointFlagsTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `EZRestAPI.Endpoints` — public `[Flags]` enum in the *generated* source, members `None=0, List=1, Create=2, Read=4, Update=8, Delete=16, Nested=32, Crud, ReadOnly, All`.
  - `EZRestAPI.ModelAttribute.Endpoints` — settable property of that type.
  - `ProviderExtensions.EndpointFeatures` — generator-side mirror enum, same names and values. **Deviation from the spec:** the spec writes this `internal`; make it `public`, matching the neighbouring `NestedKind` enum and the public `Model` record, so the drift test in `EZRestAPI.Tests` can reflect over it without an `InternalsVisibleTo`.
  - `ProviderExtensions.Has(EndpointFeatures flags, EndpointFeatures flag)` → `bool`, non-boxing flag test.
  - `ProviderExtensions.Model.Endpoints` — new trailing record parameter of type `EndpointFeatures`, defaulting to `EndpointFeatures.None`.

- [ ] **Step 1: Write the failing test**

Create `EZRestAPI.Tests/EndpointFlagsTests.cs`:

```csharp
namespace EZRestAPI.Tests;

using EZRestAPI.Providers;

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
    /// compiling, so the two declarations are pinned to each other here.
    /// </summary>
    [Fact]
    public void EndpointFeatures_MirrorsGeneratedEnum()
    {
        var result = GeneratorHarness.Run("namespace Tests;");
        var emitted = Squash(GeneratorHarness.GetSource(result, "EZRestAPIEndpoints.g.cs"));

        string[] atomic = ["None", "List", "Create", "Read", "Update", "Delete", "Nested"];
        foreach (var name in atomic)
        {
            var value = (int)Enum.Parse<ProviderExtensions.EndpointFeatures>(name);
            Assert.Contains($"{name}={value},", emitted);
        }

        Assert.Equal(1 | 2 | 4 | 8 | 16, (int)ProviderExtensions.EndpointFeatures.Crud);
        Assert.Equal(1 | 4 | 32, (int)ProviderExtensions.EndpointFeatures.ReadOnly);
        Assert.Equal(1 | 2 | 4 | 8 | 16 | 32, (int)ProviderExtensions.EndpointFeatures.All);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
export NUGET_PACKAGES="$PWD/.nuget-cache"
dotnet test EZRestAPI.Tests -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q --filter "FullyQualifiedName~EndpointFlagsTests"
```

Expected: compile error — `EndpointFeatures` does not exist in `ProviderExtensions`.

- [ ] **Step 3: Add the mirror enum and the `Has` helper**

In `EZRestAPI/Providers/ProviderExtensions.cs`, directly after the `NestedKind` enum (around line 41), add:

```csharp
    /// <summary>
    /// Generator-side mirror of the emitted <c>EZRestAPI.Endpoints</c> enum.
    /// The generator runs inside the compilation that defines that type, so it
    /// cannot reference it; these values must stay bit-for-bit identical, which
    /// <c>EndpointFlagsTests.EndpointFeatures_MirrorsGeneratedEnum</c> pins.
    /// </summary>
    [System.Flags]
    public enum EndpointFeatures
    {
        None = 0,
        List = 1,
        Create = 2,
        Read = 4,
        Update = 8,
        Delete = 16,
        Nested = 32,

        Crud = List | Create | Read | Update | Delete,
        ReadOnly = List | Read | Nested,
        All = Crud | Nested,
    }

    /// <summary>
    /// Non-boxing flag test. <c>Enum.HasFlag</c> boxes both operands and this
    /// runs once per route per model per generator pass.
    /// </summary>
    public static bool Has(EndpointFeatures flags, EndpointFeatures flag) =>
        (flags & flag) == flag;
```

- [ ] **Step 4: Add the `Endpoints` parameter to the `Model` record**

In the same file, change the `Model` record (line 43-54) so it ends with the new trailing parameter:

```csharp
    public record Model(
        string AssemblyName,
        string ModelNamespace,
        string ModelName,
        string ClassName,
        string SingularName,
        string PluralName,
        EquatableArray<Property> Properties,
        string? UserIdTypeName = null,
        EquatableArray<RelationshipInfo> ParentRelationships = default,
        EquatableArray<RelationshipInfo> ChildRelationships = default,
        EndpointFeatures Endpoints = EndpointFeatures.None
    );
```

It must be **last** and **defaulted** so the `with` expressions in `ResolveRelationships` and every existing construction site keep compiling unchanged.

- [ ] **Step 5: Parse the named argument in `CreateModel`**

In `CreateModel` (line 390), after the `pluralName` line, add:

```csharp
        var endpoints = GetEndpoints(attribute);
```

and add `Endpoints: endpoints,` as the last argument of the `new Model(...)` call (after `UserIdTypeName:`).

Then add this private helper next to `GetArgument` (around line 782):

```csharp
    /// <summary>
    /// Reads the <c>Endpoints</c> named argument off [Model]. Absent, or any
    /// value that is not an int, resolves to <see cref="EndpointFeatures.None"/>
    /// — exposure is opt-in, so an unreadable flag must fail closed.
    /// </summary>
    private static EndpointFeatures GetEndpoints(AttributeData attribute)
    {
        foreach (var named in attribute.NamedArguments)
        {
            if (named.Key != "Endpoints")
            {
                continue;
            }

            return named.Value.Value is int value
                ? (EndpointFeatures)value
                : EndpointFeatures.None;
        }

        return EndpointFeatures.None;
    }
```

- [ ] **Step 6: Emit the `Endpoints` enum from `AttributesGenerator`**

In `EZRestAPI/Generators/AttributesGenerator.cs`, add a new `ctx.AddSource` block immediately before the existing `EZRestAPIModel.g.cs` block:

```csharp
            ctx.AddSource(
                "EZRestAPIEndpoints.g.cs",
                SourceText.From(
                    """
                    // <auto-generated/>
                    #nullable enable

                    namespace EZRestAPI;

                    using System;

                    /// <summary>
                    /// Selects which endpoints EZRestAPI generates for a [Model].
                    /// Exposure is opt-in: the default is <see cref="None"/>, which
                    /// registers the model in the DbContext and generates nothing else.
                    /// </summary>
                    [Flags]
                    public enum Endpoints
                    {
                        None = 0,
                        List = 1,
                        Create = 2,
                        Read = 4,
                        Update = 8,
                        Delete = 16,
                        Nested = 32,

                        Crud = List | Create | Read | Update | Delete,
                        ReadOnly = List | Read | Nested,
                        All = Crud | Nested,
                    }
                    """,
                    Encoding.UTF8
                )
            );
```

Every member must carry a trailing comma — the drift test asserts on `"{name}={value},"`.

- [ ] **Step 7: Add the property to `ModelAttribute`**

In the same file, change the `EZRestAPIModel.g.cs` raw string body to:

```csharp
                    [AttributeUsage(AttributeTargets.Class)]
                    public partial class ModelAttribute(string singularName, string pluralName)
                        : Attribute
                    {
                        public string SingularName { get; } = singularName;

                        public string PluralName { get; } = pluralName;

                        /// <summary>
                        /// Which endpoints to generate. Defaults to
                        /// <see cref="Endpoints.None"/>: DbContext registration only.
                        /// </summary>
                        public Endpoints Endpoints { get; set; }
                    }
```

No initializer — the CLR default for the enum is already `None`, so the absent-argument and explicit-`None` cases are the same value by construction.

- [ ] **Step 8: Run the tests**

```bash
dotnet test EZRestAPI.Tests -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: PASS, including all 104 pre-existing tests. Nothing reads the flag yet, so behaviour is unchanged.

- [ ] **Step 9: Verify the Example project still builds**

```bash
dotnet build Example/Example.csproj -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: build succeeds.

- [ ] **Step 10: Commit**

```bash
git add EZRestAPI/Generators/AttributesGenerator.cs EZRestAPI/Providers/ProviderExtensions.cs EZRestAPI.Tests/EndpointFlagsTests.cs
git commit -m "Add Endpoints flag enum to [Model] and parse it into the model provider.

The generator cannot reference the enum it emits into the consuming
compilation, so EndpointFeatures mirrors it generator-side and a drift test
pins the two declarations together. Nothing consumes the flag yet."
```

---

### Task 2: Annotate every existing `[Model]` with `Endpoints.All`

This is a **no-op migration** that must land *before* any gating, so the suite and the Example project stay green at every commit. Until Task 3, nothing reads the flag, so adding it changes no output.

**Files:**
- Modify: all 13 files in `Example/Models/*.cs` that carry `[EZRestAPI.Model(...)]`
- Modify: every file in `EZRestAPI.Tests/` with an inline `[EZRestAPI.Model(...)]` source

**Interfaces:**
- Consumes: `EZRestAPI.Endpoints.All` from Task 1.
- Produces: a codebase where every `[Model]` names its endpoints explicitly.

- [ ] **Step 1: List the files that need changing**

```bash
grep -rln 'EZRestAPI\.Model(' Example/Models EZRestAPI.Tests --include=*.cs
```

Expected: the 13 `Example/Models` files plus roughly a dozen test files.

- [ ] **Step 2: Apply the rewrite**

```bash
grep -rlZ 'EZRestAPI\.Model(' Example/Models EZRestAPI.Tests --include=*.cs \
  | xargs -0 perl -0pi -e 's/(\[EZRestAPI\.Model\("[^"]*",\s*"[^"]*")\)/$1, Endpoints = EZRestAPI.Endpoints.All)/g'
```

This matches only the two-argument form and leaves any `[Model]` that already names `Endpoints` alone.

- [ ] **Step 3: Verify no bare two-argument `[Model]` survives**

```bash
grep -rn 'EZRestAPI\.Model("[^"]*", *"[^"]*")' Example/Models EZRestAPI.Tests --include=*.cs
```

Expected: no output. If a match remains it is a multi-line attribute the regex missed — edit it by hand.

- [ ] **Step 4: Run the tests**

```bash
dotnet test EZRestAPI.Tests -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: PASS, same count as Task 1. Test sources that assert on generated *text* are unaffected because the attribute argument does not appear in generated output.

- [ ] **Step 5: Verify the Example project still builds**

```bash
dotnet build Example/Example.csproj -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add Example/Models EZRestAPI.Tests
git commit -m "Annotate every [Model] with Endpoints.All ahead of the opt-in flip.

A no-op today — nothing reads the flag yet — so the suite and the Example
project stay green when the default flips to None in the next commit."
```

---

### Task 3: Gate route emission in `EndpointsGenerator`

**Files:**
- Modify: `EZRestAPI/Generators/EndpointsGenerator.cs:317-333` (`InsertNestedGroup`) and `:737-788` (the model `RegisterSourceOutput`)
- Test: `EZRestAPI.Tests/EndpointFlagsTests.cs`

**Interfaces:**
- Consumes: `ProviderExtensions.EndpointFeatures`, `ProviderExtensions.Has(...)`, `Model.Endpoints` from Task 1.
- Produces:
  - `{Singular}Endpoints.g.cs` is **not generated at all** for a `None` model; for non-`None` models it contains only the selected routes.
  - `EndpointFlagsTests.HasSource(GeneratorDriverRunResult, string hintName)` → `bool` — test helper asserting on a generated file's *absence*, reused by Tasks 4 and 5.

- [ ] **Step 1: Write the failing tests**

Append to `EZRestAPI.Tests/EndpointFlagsTests.cs`:

```csharp
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
        var result = GeneratorHarness.Run("""
            namespace Tests;

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel { public required string Title { get; set; } }
            """);

        Assert.False(HasSource(result, "BookEndpoints.g.cs"));
    }

    [Fact]
    public void ExplicitNone_MatchesOmittedArgument()
    {
        var result = GeneratorHarness.Run("""
            namespace Tests;

            [EZRestAPI.Model("Book", "Books", Endpoints = EZRestAPI.Endpoints.None)]
            public partial class BookModel { public required string Title { get; set; } }
            """);

        Assert.False(HasSource(result, "BookEndpoints.g.cs"));
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
        var endpoints = BookEndpoints(
            "EZRestAPI.Endpoints.List | EZRestAPI.Endpoints.Nested"
        );

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
        var result = GeneratorHarness.Run("""
            namespace Tests;

            [EZRestAPI.Model("Author", "Authors")]
            public partial class AuthorModel { public required string Name { get; set; } }

            [EZRestAPI.Model("Book", "Books", Endpoints = EZRestAPI.Endpoints.ReadOnly)]
            public partial class BookModel
            {
                public required string Title { get; set; }
                public required int AuthorId { get; set; }
            }
            """);

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

    [Fact]
    public void GeneratedEndpointFile_HasNoDoubleBlankLines()
    {
        var endpoints = BookEndpoints(
            "EZRestAPI.Endpoints.List | EZRestAPI.Endpoints.Delete"
        );

        Assert.DoesNotContain("\n\n\n", endpoints.Replace("\r\n", "\n"));
    }
```

Add `using Microsoft.CodeAnalysis;` to the file's using block for `GeneratorDriverRunResult`.

> **Note on `CustomDbContext.g.cs`:** confirm the hint name and the exact `DbSet<...>` rendering by running
> `dotnet test EZRestAPI.Tests --filter "FullyQualifiedName~GenerationTests" -v n` or by reading
> `EZRestAPI/Generators/DbContextGenerator.cs`. Adjust that one assertion to match what the generator actually emits.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test EZRestAPI.Tests -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q --filter "FullyQualifiedName~EndpointFlagsTests"
```

Expected: FAIL. `DefaultIsClosed_NoEndpointClassIsGenerated` fails because the file is still generated; the verb tests fail because every route is still emitted.

- [ ] **Step 3a: Extract the five nested routes into their own methods (pure move, no logic change)**

`InsertNestedGroup` currently writes the `MapGroup` line plus all five nested routes in one body. Split the five route blocks out so they can be selected individually. Create five private methods:

```csharp
    private static void InsertNestedListEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model,
        ProviderExtensions.RelationshipInfo rel
    )
    { /* moved verbatim from InsertNestedGroup — see the line table below */ }
```

…and the same signature for `InsertNestedCreateEndpoint`, `InsertNestedReadEndpoint`, `InsertNestedUpdateEndpoint`, `InsertNestedDeleteEndpoint`.

Move the existing code verbatim, no edits to the emitted strings:

| New method | Source lines in the current `InsertNestedGroup` |
| --- | --- |
| `InsertNestedListEndpoint` | 336-378 (`// GET / -> paged list…` through its `EmitMetadata`) |
| `InsertNestedCreateEndpoint` | 379-427 |
| `InsertNestedReadEndpoint` | 428-458 |
| `InsertNestedUpdateEndpoint` | 459-484 |
| `InsertNestedDeleteEndpoint` | 485-524 |

Each method needs its own copy of whichever of these locals its body references — they are cheap string builds, so duplicating them is preferable to threading a parameter object:

```csharp
        var parentRoute = rel.ParentPluralName.ToLowerInvariant();
        var childRoute = rel.ChildPluralName.ToLowerInvariant();
        var name = $"{rel.ChildSingularName}Under{rel.ParentSingularName}";
        var tag = rel.ChildPluralName;
        var groupVar = $"{rel.ParentSingularName.ToLowerInvariant()}{rel.ChildSingularName}Group";
        var createRequest = $"Create{name}Request";
        var updateRequest = $"Update{name}Request";
```

Verify the move changed nothing before going further:

```bash
dotnet test EZRestAPI.Tests -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: the pre-existing tests still pass (the new flag tests still fail). If any pre-existing test now fails, the move was not verbatim — fix it before Step 3b.

- [ ] **Step 3b: Gate the nested group**

Replace what remains of `InsertNestedGroup` with:

```csharp
    /// <summary>
    /// Emits the parent-scoped route group for one relationship, containing
    /// only the verbs the model's flags select. Writes nothing at all — not
    /// even the MapGroup line — when Nested is unset or no verb survives.
    /// </summary>
    private static void InsertNestedGroup(
        IndentedTextWriter writer,
        ProviderExtensions.Model model,
        ProviderExtensions.RelationshipInfo rel
    )
    {
        var flags = model.Endpoints;

        if (!ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Nested))
        {
            return;
        }

        var sections = new List<Action>();
        if (ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.List))
        {
            sections.Add(() => InsertNestedListEndpoint(writer, model, rel));
        }
        if (ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Create))
        {
            sections.Add(() => InsertNestedCreateEndpoint(writer, model, rel));
        }
        if (ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Read))
        {
            sections.Add(() => InsertNestedReadEndpoint(writer, model, rel));
        }
        if (ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Update))
        {
            sections.Add(() => InsertNestedUpdateEndpoint(writer, model, rel));
        }
        if (ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Delete))
        {
            sections.Add(() => InsertNestedDeleteEndpoint(writer, model, rel));
        }

        if (sections.Count == 0)
        {
            return;
        }

        var groupVar = $"{rel.ParentSingularName.ToLowerInvariant()}{rel.ChildSingularName}Group";
        writer.WriteLine(
            $"var {groupVar} = app.MapGroup(\"/{rel.ParentPluralName.ToLowerInvariant()}/{{parentId:int}}/{rel.ChildPluralName.ToLowerInvariant()}\");"
        );

        foreach (var section in sections)
        {
            writer.WriteLine();
            section();
        }
    }
```

Add `using System;` and `using System.Collections.Generic;` to the file's using block (for `Action` and `List<T>`).

- [ ] **Step 3c: Add the caller-side emptiness check**

`InsertNestedGroup` returning without writing is not enough on its own — the *caller* writes a blank-line separator before calling it, which would leave a ragged gap. Add this helper next to `Route`:

```csharp
    /// <summary>
    /// True when the flags would make <see cref="InsertNestedGroup"/> emit
    /// something. The caller needs this before writing its separator.
    /// </summary>
    private static bool EmitsNestedGroup(ProviderExtensions.EndpointFeatures flags) =>
        ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Nested)
        && (
            ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.List)
            || ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Create)
            || ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Read)
            || ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Update)
            || ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Delete)
        );
```

- [ ] **Step 4: Gate the flat routes and skip `None` models entirely**

Replace the model `RegisterSourceOutput` body (lines 739-787) with:

```csharp
            (ctx, model) =>
            {
                var flags = model.Endpoints;

                // Endpoints.None is persistence only: no endpoint class at all.
                // BootstrapGenerator applies the matching filter so nothing
                // references the class that is not emitted here.
                if (flags == ProviderExtensions.EndpointFeatures.None)
                {
                    return;
                }

                var writer = SourceWriter.Create();

                writer.WriteLine($"namespace {model.AssemblyName};");
                writer.WriteLine();
                writer.WriteLine("using System.Threading;");
                writer.WriteLine("using Microsoft.AspNetCore.Builder;");
                writer.WriteLine("using Microsoft.AspNetCore.Http;");
                writer.WriteLine("using Microsoft.AspNetCore.Http.HttpResults;");
                writer.WriteLine("using Microsoft.AspNetCore.Mvc;");
                writer.WriteLine("using Microsoft.AspNetCore.Routing;");
                writer.WriteLine();
                writer.WriteLine($"public static class {model.SingularName}Endpoints");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine(
                    $"public static IEndpointRouteBuilder Map{model.SingularName}Endpoints(this IEndpointRouteBuilder app)"
                );
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine($"var group = app.MapGroup(\"/{Route(model)}\");");

                var flat = new System.Collections.Generic.List<System.Action>();
                if (ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.List))
                {
                    flat.Add(() => InsertListEndpoint(writer, model));
                }
                if (ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Create))
                {
                    flat.Add(() => InsertCreateEndpoint(writer, model));
                }
                if (ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Read))
                {
                    flat.Add(() => InsertReadEndpoint(writer, model));
                }
                if (ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Update))
                {
                    flat.Add(() => InsertUpdateEndpoint(writer, model));
                }
                if (ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Delete))
                {
                    flat.Add(() => InsertDeleteEndpoint(writer, model));
                }

                foreach (var section in flat)
                {
                    writer.WriteLine();
                    section();
                }

                if (EmitsNestedGroup(flags))
                {
                    foreach (var rel in model.ParentRelationships)
                    {
                        writer.WriteLine();
                        InsertNestedGroup(writer, model, rel);
                    }
                }

                writer.WriteLine();
                writer.WriteLine("return app;");
                writer.Indent--;
                writer.WriteLine("}");
                writer.Indent--;
                writer.WriteLine("}");

                ctx.AddSource(
                    $"{model.SingularName}Endpoints.g.cs",
                    SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
                );
            }
```

- [ ] **Step 5: Run the tests**

```bash
dotnet test EZRestAPI.Tests -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: PASS — the new flag tests plus all pre-existing tests, which now exercise `Endpoints.All` thanks to Task 2.

This is also how the spec's "`Endpoints.All` reproduces today's surface byte-for-byte" requirement is met: the 104 pre-existing tests assert on generated text and all now run under `Endpoints.All`, so they collectively pin the full surface far more tightly than a single snapshot comparison would.

- [ ] **Step 6: Verify the Example project still builds**

```bash
dotnet build Example/Example.csproj -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: build succeeds — every Example model is `Endpoints.All`, so every endpoint class still exists for `BootstrapGenerator` to reference.

- [ ] **Step 7: Commit**

```bash
git add EZRestAPI/Generators/EndpointsGenerator.cs EZRestAPI.Tests/EndpointFlagsTests.cs
git commit -m "Gate route emission on the Endpoints flags.

Each flat verb and the nested group are emitted only when selected, and a
None model produces no endpoint class at all. Separators are written only
ahead of a section that will emit, so partial flags do not leave ragged
blank lines in the generated file."
```

---

### Task 4: Skip repositories and per-model DTOs for `None` models

**Files:**
- Modify: `EZRestAPI/Generators/RepositoryGenerator.cs:710-749` (the model `RegisterSourceOutput`)
- Modify: `EZRestAPI/Generators/DtoGenerator.cs:150-169` (nested-DTO output) and `:420-457` (`RegisterDto`)
- Test: `EZRestAPI.Tests/EndpointFlagsTests.cs`

**Interfaces:**
- Consumes: `Model.Endpoints`, `ProviderExtensions.Has(...)` from Task 1.
- Produces: for a `None` model, no `{Singular}Repository.g.cs` and none of `Create{S}Request/Response`, `Read{S}Response`, `Update{S}Request`. Shared infrastructure (`PagedResponse.g.cs`, `WriteResult.g.cs`, the validation and problem helpers) is unaffected.

- [ ] **Step 1: Write the failing tests**

Append to `EZRestAPI.Tests/EndpointFlagsTests.cs`:

```csharp
    [Fact]
    public void None_GeneratesNoRepositoryAndNoDtos()
    {
        var result = GeneratorHarness.Run("""
            namespace Tests;

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel { public required string Title { get; set; } }
            """);

        Assert.False(HasSource(result, "BookRepository.g.cs"));
        Assert.False(HasSource(result, "CreateBookRequest.g.cs"));
        Assert.False(HasSource(result, "CreateBookResponse.g.cs"));
        Assert.False(HasSource(result, "ReadBookResponse.g.cs"));
        Assert.False(HasSource(result, "UpdateBookRequest.g.cs"));
    }

    [Fact]
    public void None_StillGeneratesDbContextEntryAndIdPartial()
    {
        var result = GeneratorHarness.Run("""
            namespace Tests;

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel { public required string Title { get; set; } }
            """);

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
        var result = GeneratorHarness.Run("""
            namespace Tests;

            [EZRestAPI.Model("Book", "Books", Endpoints = EZRestAPI.Endpoints.List)]
            public partial class BookModel { public required string Title { get; set; } }
            """);

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
        var result = GeneratorHarness.Run("""
            namespace Tests;

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel { public required string Title { get; set; } }
            """);

        Assert.True(HasSource(result, "PagedResponse.g.cs"));
        Assert.True(HasSource(result, "WriteResult.g.cs"));
    }

    [Fact]
    public void NoneParent_DoesNotGenerateNestedDtosForItsChildren()
    {
        var result = GeneratorHarness.Run("""
            namespace Tests;

            [EZRestAPI.Model("Author", "Authors")]
            public partial class AuthorModel { public required string Name { get; set; } }

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel
            {
                public required string Title { get; set; }
                public required int AuthorId { get; set; }
            }
            """);

        Assert.False(HasSource(result, "CreateBookUnderAuthorRequest.g.cs"));
        Assert.False(HasSource(result, "UpdateBookUnderAuthorRequest.g.cs"));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test EZRestAPI.Tests -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q --filter "FullyQualifiedName~EndpointFlagsTests"
```

Expected: FAIL — the repository and DTO files are still generated for a `None` model.

- [ ] **Step 3: Gate the repository**

In `EZRestAPI/Generators/RepositoryGenerator.cs`, at the top of the model `RegisterSourceOutput` lambda (line 712, immediately after `(ctx, model) =>` opens its block), add:

```csharp
                if (model.Endpoints == ProviderExtensions.EndpointFeatures.None)
                {
                    return;
                }
```

Leave the aggregates output below it untouched.

- [ ] **Step 4: Gate the per-model DTOs**

In `EZRestAPI/Generators/DtoGenerator.cs`, inside `RegisterDto`'s lambda (line 431, immediately after `(ctx, model) =>` opens its block), add:

```csharp
                if (model.Endpoints == ProviderExtensions.EndpointFeatures.None)
                {
                    return;
                }
```

And in the nested-DTO output (line 152, after `(ctx, model) =>` opens its block), add the same guard.

Do **not** touch the `modelsProvider.Collect()` registration at line 171 — `PagedResponse`, `WriteResult` and the validation and problem helpers are shared infrastructure that `[Aggregate]` also consumes, and gating them would break an assembly whose models are all `None`.

- [ ] **Step 5: Run the tests**

```bash
dotnet test EZRestAPI.Tests -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: PASS, all tests.

- [ ] **Step 6: Verify the Example project still builds**

```bash
dotnet build Example/Example.csproj -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add EZRestAPI/Generators/RepositoryGenerator.cs EZRestAPI/Generators/DtoGenerator.cs EZRestAPI.Tests/EndpointFlagsTests.cs
git commit -m "Skip repositories and per-model DTOs for Endpoints.None models.

None means persistence only, so generating an unreferenced repository and
four DTOs for it would be dead code in the consumer's assembly. Shared
infrastructure stays ungated because [Aggregate] consumes it too."
```

---

### Task 5: Exclude `None` models from the bootstrap

Without this, a `None` model emits `services.AddScoped<BookRepository>()` and `app.MapBookEndpoints()` referencing two types Task 3 and Task 4 no longer generate — a compile error in the consuming project that the text-only fast tests cannot see.

**Files:**
- Modify: `EZRestAPI/Generators/BootstrapGenerator.cs:14,33-36`
- Test: `EZRestAPI.Tests/EndpointFlagsTests.cs`

**Interfaces:**
- Consumes: `Model.Endpoints` from Task 1.
- Produces: `EZRestAPIExtensions.g.cs` naming only non-`None` models (aggregates unchanged).

- [ ] **Step 1: Write the failing test**

Append to `EZRestAPI.Tests/EndpointFlagsTests.cs`:

```csharp
    [Fact]
    public void Bootstrap_ExcludesNoneModelsAndKeepsTheRest()
    {
        var result = GeneratorHarness.Run("""
            namespace Tests;

            [EZRestAPI.Model("Author", "Authors")]
            public partial class AuthorModel { public required string Name { get; set; } }

            [EZRestAPI.Model("Book", "Books", Endpoints = EZRestAPI.Endpoints.All)]
            public partial class BookModel { public required string Title { get; set; } }
            """);

        var bootstrap = GeneratorHarness.GetSource(result, "EZRestAPIExtensions.g.cs");

        Assert.Contains("services.AddScoped<BookRepository>();", bootstrap);
        Assert.Contains("app.MapBookEndpoints();", bootstrap);
        Assert.DoesNotContain("AuthorRepository", bootstrap);
        Assert.DoesNotContain("MapAuthorEndpoints", bootstrap);
    }
```

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test EZRestAPI.Tests -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q --filter "FullyQualifiedName~Bootstrap_ExcludesNoneModels"
```

Expected: FAIL — `AuthorRepository` is still registered.

- [ ] **Step 3: Filter the model list**

In `EZRestAPI/Generators/BootstrapGenerator.cs`, replace line 33-36:

```csharp
                // Both kinds expose a `{Singular}Repository` + `Map{Singular}Endpoints`.
                var repositoryNames = models
                    .Select(m => m.SingularName)
                    .Concat(aggregates.Select(a => a.SingularName))
                    .ToArray();
```

with:

```csharp
                // Both kinds expose a `{Singular}Repository` + `Map{Singular}Endpoints`
                // — except Endpoints.None models, which are persistence only and
                // generate neither, so naming them here would dangle.
                var repositoryNames = models
                    .Where(m => m.Endpoints != ProviderExtensions.EndpointFeatures.None)
                    .Select(m => m.SingularName)
                    .Concat(aggregates.Select(a => a.SingularName))
                    .ToArray();
```

The `using EZRestAPI.Providers;` directive is already present at line 4.

- [ ] **Step 4: Run the tests**

```bash
dotnet test EZRestAPI.Tests -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: PASS, all tests.

- [ ] **Step 5: Verify the Example project still builds**

```bash
dotnet build Example/Example.csproj -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add EZRestAPI/Generators/BootstrapGenerator.cs EZRestAPI.Tests/EndpointFlagsTests.cs
git commit -m "Exclude Endpoints.None models from AddEZRestAPI and MapEZRestAPI.

Without the filter a None model names a repository and an endpoint class
that are no longer generated, which fails in the consuming compilation
rather than in the generator's own text assertions."
```

---

### Task 6: Add the `EZR013` and `EZR014` diagnostics

**Files:**
- Modify: `EZRestAPI/Generators/DiagnosticsGenerator.cs:110-118` (after `AggregateFactoryArity`) and `:131-157` (the per-model loop)
- Modify: `EZRestAPI/AnalyzerReleases.Unshipped.md`
- Test: `EZRestAPI.Tests/EndpointFlagsTests.cs`

**Interfaces:**
- Consumes: `Model.Endpoints`, `ProviderExtensions.Has(...)` from Task 1.
- Produces: `DiagnosticsGenerator.CreateWithoutRead` (`EZR013`, Warning) and `DiagnosticsGenerator.NoEndpointsGenerated` (`EZR014`, Info).

- [ ] **Step 1: Write the failing tests**

Append to `EZRestAPI.Tests/EndpointFlagsTests.cs`:

```csharp
    private static string[] DiagnosticsFor(string flags)
    {
        var source = $$"""
            namespace Tests;

            [EZRestAPI.Model("Book", "Books"{{flags}})]
            public partial class BookModel { public required string Title { get; set; } }
            """;

        return GeneratorHarness.DiagnosticIds(GeneratorHarness.Run(source));
    }

    [Fact]
    public void EZR013_FiresWhenCreateIsSetWithoutRead()
    {
        Assert.Contains("EZR013", DiagnosticsFor(", Endpoints = EZRestAPI.Endpoints.Create"));
    }

    [Theory]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.All")]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.ReadOnly")]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.None")]
    [InlineData("")]
    public void EZR013_DoesNotFireOtherwise(string flags)
    {
        Assert.DoesNotContain("EZR013", DiagnosticsFor(flags));
    }

    [Theory]
    [InlineData("")]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.None")]
    public void EZR014_FiresWhenNoEndpointsAreSelected(string flags)
    {
        Assert.Contains("EZR014", DiagnosticsFor(flags));
    }

    [Theory]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.All")]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.ReadOnly")]
    [InlineData(", Endpoints = EZRestAPI.Endpoints.List")]
    public void EZR014_DoesNotFireForAnyNonZeroCombination(string flags)
    {
        Assert.DoesNotContain("EZR014", DiagnosticsFor(flags));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test EZRestAPI.Tests -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q --filter "FullyQualifiedName~EZR01"
```

Expected: FAIL — neither diagnostic exists.

- [ ] **Step 3: Declare the descriptors**

In `EZRestAPI/Generators/DiagnosticsGenerator.cs`, after `AggregateFactoryArity` (line 118), add:

```csharp
    public static readonly DiagnosticDescriptor CreateWithoutRead = new(
        "EZR013",
        "Create endpoint has an unreachable Location header",
        "Model '{0}' generates a Create endpoint whose Location header points at '/{1}/{{id}}', but Endpoints.Read is not set",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor NoEndpointsGenerated = new(
        "EZR014",
        "Model generates no API surface",
        "Model '{0}' is registered in the DbContext only; set Endpoints on [EZRestAPI.Model] to generate a repository, DTOs and endpoints",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );
```

The `{{id}}` in the `EZR013` message format is an escaped literal `{id}` — `Diagnostic.Create` runs the message through `string.Format`, so a bare `{id}` would throw at runtime.

- [ ] **Step 4: Report them in the per-model loop**

In the `foreach (var entry in models)` loop, after the `ReportPropertyDiagnostics(...)` call (line 156), add:

```csharp
                    var flags = model.Endpoints;

                    if (flags == ProviderExtensions.EndpointFeatures.None)
                    {
                        ctx.ReportDiagnostic(
                            Diagnostic.Create(NoEndpointsGenerated, location, model.ModelName)
                        );
                    }
                    else if (
                        ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Create)
                        && !ProviderExtensions.Has(flags, ProviderExtensions.EndpointFeatures.Read)
                    )
                    {
                        ctx.ReportDiagnostic(
                            Diagnostic.Create(
                                CreateWithoutRead,
                                location,
                                model.ModelName,
                                model.PluralName.ToLowerInvariant()
                            )
                        );
                    }
```

- [ ] **Step 5: Register the rules**

Append two rows to the table in `EZRestAPI/AnalyzerReleases.Unshipped.md`:

```
EZR013 | EZRestAPI | Warning | Create endpoint has an unreachable Location header
EZR014 | EZRestAPI | Info | Model generates no API surface
```

- [ ] **Step 6: Run the tests**

```bash
dotnet test EZRestAPI.Tests -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: PASS. If `DiagnosticsTests` now fails because a scenario picked up an unexpected `EZR014`, that scenario's inline `[Model]` was missed in Task 2 — add `Endpoints = EZRestAPI.Endpoints.All` to it rather than weakening the assertion.

- [ ] **Step 7: Verify the Example project still builds**

```bash
dotnet build Example/Example.csproj -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: build succeeds with no `EZR013`/`EZR014` output — every Example model is still `Endpoints.All` at this point.

- [ ] **Step 8: Commit**

```bash
git add EZRestAPI/Generators/DiagnosticsGenerator.cs EZRestAPI/AnalyzerReleases.Unshipped.md EZRestAPI.Tests/EndpointFlagsTests.cs
git commit -m "Add EZR013 (Create without Read) and EZR014 (no API surface).

EZR013 is a Warning because a 201 whose Location 404s is a wrong response,
not a smaller API. EZR014 is Info because a deliberate persistence-only
model is now spelled by omitting the property, and Warning severity would
put build noise on a correct program."
```

---

### Task 7: Demonstrate the flags in the Example project

**Files:**
- Create: `Example/Models/AuditLogModel.cs`, `Example/Models/AuditNoteModel.cs`, `Example/Models/ExchangeRateModel.cs`
- Create: `Example.Tests/GeneratorTests/EndpointFlagsEndpointTests.cs`
- Modify: nothing existing — the 13 existing models stay on `Endpoints.All` so the feature tour and its integration suite keep their coverage.

**Interfaces:**
- Consumes: everything from Tasks 1-6.
- Produces: three Example models covering the `None`, `ReadOnly` and `Crud` tiers.

- [ ] **Step 1: Add the persistence-only model**

Create `Example/Models/AuditLogModel.cs`:

```csharp
namespace Example.Models;

/// <summary>
/// Endpoints.None (the default): registered in the DbContext and nothing else.
/// No repository, no DTOs, no routes — persistence without an API.
/// </summary>
[EZRestAPI.Model("AuditLog", "AuditLogs")]
public partial class AuditLogModel
{
    public required string Message { get; set; }

    public required DateTimeOffset OccurredAt { get; set; }
}
```

- [ ] **Step 2: Add the read-only model**

Create `Example/Models/ExchangeRateModel.cs`:

```csharp
namespace Example.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Endpoints.ReadOnly: GET the collection and GET an item, no writes.
/// </summary>
[EZRestAPI.Model("ExchangeRate", "ExchangeRates", Endpoints = EZRestAPI.Endpoints.ReadOnly)]
public partial class ExchangeRateModel
{
    [MaxLength(3)]
    public required string Code { get; set; }

    public required decimal Rate { get; set; }
}
```

- [ ] **Step 3: Add the flat-only child model**

Create `Example/Models/AuditNoteModel.cs`:

```csharp
namespace Example.Models;

/// <summary>
/// Endpoints.Crud: all five flat verbs and no nested group, even though
/// AuditLogId makes this a child of AuditLog. The parent is Endpoints.None,
/// which still gives it the DbSet this model's foreign key points at.
/// </summary>
[EZRestAPI.Model("AuditNote", "AuditNotes", Endpoints = EZRestAPI.Endpoints.Crud)]
public partial class AuditNoteModel
{
    public required string Text { get; set; }

    public required int AuditLogId { get; set; }
}
```

- [ ] **Step 4: Build the Example project**

```bash
dotnet build Example/Example.csproj -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: build succeeds. This is the check that Task 5's bootstrap filter is correct — `AuditLog` is `None`, so without the filter this build fails on a missing `AuditLogRepository`.

- [ ] **Step 5: Confirm the expected diagnostics**

```bash
dotnet build Example/Example.csproj -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v n 2>&1 | grep -E 'EZR01[34]'
```

Expected: one `EZR014` for `AuditLogModel`, and no `EZR013` anywhere.

- [ ] **Step 6: Write the integration test**

Create `Example.Tests/GeneratorTests/EndpointFlagsEndpointTests.cs`. The fixture wiring below is copied from `Example.Tests/GeneratorTests/EndpointTests.cs:1-34` — the suite shares one SQL Server container through the `MsSql` collection, and each test class builds its own `WebApplicationFactory` pointed at that container's connection string.

```csharp
namespace Example.Tests;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

[Collection("MsSql")]
public class EndpointFlagsEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly HttpClient client;

    public EndpointFlagsEndpointTests(MsSqlContainerFixture fixture)
    {
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Example"] = fixture.ConnectionString,
                        }
                    )
            )
        );

        client = factory.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task ReadOnlyModel_ExposesGetsAndNotWrites()
    {
        var listResponse = await client.GetAsync("/exchangerates");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var writeResponse = await client.PostAsJsonAsync(
            "/exchangerates",
            new { Code = "USD", Rate = 1.0m }
        );
        Assert.Equal(HttpStatusCode.NotFound, writeResponse.StatusCode);
    }

    [Fact]
    public async Task NoneModel_ExposesNoRoutes()
    {
        var response = await client.GetAsync("/auditlogs");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CrudModel_ExposesFlatRoutesButNoNestedGroup()
    {
        var flatResponse = await client.GetAsync("/auditnotes");
        Assert.Equal(HttpStatusCode.OK, flatResponse.StatusCode);

        var nestedResponse = await client.GetAsync("/auditlogs/1/auditnotes");
        Assert.Equal(HttpStatusCode.NotFound, nestedResponse.StatusCode);
    }
}
```

Note the distinction these assertions rely on: a route that was never mapped returns 404 from routing itself, which is why "write rejected" and "model not exposed" both read as 404 here rather than 405.

- [ ] **Step 7: Verify the integration project compiles**

```bash
dotnet build Example.Tests/Example.Tests.csproj -m:1 -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: build succeeds. **Do not run these tests** — they need Docker, which the sandbox does not have. Report them as written-but-unrun.

- [ ] **Step 8: Run the fast suite once more**

```bash
dotnet test EZRestAPI.Tests -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add Example/Models Example.Tests/GeneratorTests/EndpointFlagsEndpointTests.cs
git commit -m "Add Example models covering the None, ReadOnly and Crud tiers.

AuditNote is a Crud child of a None parent, which exercises both that Crud
omits the nested group and that a persistence-only parent still supplies the
DbSet its children's foreign keys point at."
```

---

### Task 8: Update the README and ROADMAP

**Files:**
- Modify: `README.md`
- Modify: `ROADMAP.md` (the Phase 2 "Attribute options" bullet)

**Interfaces:**
- Consumes: the delivered feature.
- Produces: documentation matching behaviour.

- [ ] **Step 1: Read the README's current shape**

```bash
cat README.md
```

Match its existing voice and heading depth — it was deliberately rewritten as brief, plain-worded user documentation in commit `f4a30b8`. Do not restructure it.

- [ ] **Step 2: Add every existing `[Model]` example's `Endpoints` argument**

```bash
grep -n 'EZRestAPI.Model(' README.md
```

Every example must name `Endpoints`, or a reader following the quick-start gets no API. Use `Endpoints = EZRestAPI.Endpoints.All` in the quick-start.

- [ ] **Step 3: Add the `Endpoints` section**

Add a section after the `[Model]` documentation covering:

- The enum members and the three presets, as a table or list.
- That the default is `None`, and what `None` generates (DbContext registration, no repository, no DTOs, no routes).
- That any non-zero value generates the complete repository and DTO surface and exposes only the selected routes — so disabling `Create` leaves `CreateAsync` and `CreatePostRequest` available for a hand-written endpoint.
- That `Nested` is ANDed with the verbs: `List | Nested` yields both list routes, `Crud` yields flat routes only.
- `EZR013` and `EZR014`, one line each.

- [ ] **Step 4: Update the ROADMAP**

In `ROADMAP.md`, Phase 2, replace the `**Attribute options:**` bullet with:

```markdown
- [x] **Endpoint flags.** `[Model("Post", "Posts", Endpoints = Endpoints.ReadOnly)]`
      selects which endpoints are generated, defaulting to `Endpoints.None`
      (DbContext registration only). Presets: `Crud`, `ReadOnly`, `All`.
      `EZR013` warns when `Create` is set without `Read`; `EZR014` reports a
      model with no API surface.
- [ ] **Remaining attribute options:** `Route = "blog-posts"`,
      `KeyType = KeyType.Guid`.
```

- [ ] **Step 5: Verify the documented commands still work**

Run any command the README instructs a reader to run, and confirm the quick-start's model snippet compiles by pasting it into a scratch build if it is not already covered by `Example`.

- [ ] **Step 6: Commit**

```bash
git add README.md ROADMAP.md
git commit -m "Document the Endpoints flags.

Every [Model] example gains an explicit Endpoints argument — with opt-in
defaults a reader following the quick-start verbatim would otherwise get a
compiling project with no API."
```

---

## Final verification

- [ ] **Full fast suite**

```bash
export NUGET_PACKAGES="$PWD/.nuget-cache"
dotnet test EZRestAPI.Tests -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: PASS, at least 104 + the ~25 tests this plan adds.

- [ ] **Real codegen gate**

```bash
dotnet build Example/Example.csproj -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: build succeeds.

- [ ] **Integration project compiles**

```bash
dotnet build Example.Tests/Example.Tests.csproj -m:1 -p:EnableSourceControlManagerQueries=false -p:Deterministic=false --nologo -v q
```

Expected: build succeeds; tests unrun (no Docker).

- [ ] **Report honestly** which suites ran, which did not, and why — `Example.Tests` requires Docker and must be reported as written-but-unrun.
