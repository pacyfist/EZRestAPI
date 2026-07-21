# Relationship-Aware Nested Endpoints Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Teach the EZRestAPI source generator to detect `{Singular}Id` foreign keys between top-level `[Model]`s by naming convention and emit full nested + flat CRUD routes with paginated collections and correct REST status codes.

**Architecture:** A new relationship-resolution provider collects all models, matches `{Singular}Id` `int` scalars against known model singular names, and augments each `Model` with `ParentRelationships` (its FKs) and `ChildRelationships` (FKs pointing at it). Existing per-generator emitters (DbContext, Dto, Repository, Endpoints) consume that augmented model to add FK configuration, nested DTOs, scoped repository methods, and nested route groups. A new `EZR011` diagnostic warns on FK-shaped-but-unresolved properties. Conflict-aware method variants are generated only for models participating in relationships, preserving existing signatures for everything else.

**Tech Stack:** C# incremental source generators (Roslyn, `netstandard2.0`), EF Core 10 (owned + associated entities), ASP.NET Core minimal APIs (`net10.0`), xUnit, Testcontainers SQL Server.

## Global Constraints

- Generator project targets `netstandard2.0`; Example/tests target `net10.0` (`global.json` SDK `10.0.100`, `rollForward: latestFeature`). Do not change target frameworks.
- Only `int` keys are supported (existing `EZR007`). A foreign key is `int` or `int?` **only**.
- Generated files carry their own `using` directives; never rely on `ImplicitUsings` (see commit `45422cd`).
- Source locations must never enter the cached code-gen model records; locations live only in the `*Diagnostics` records consumed by `DiagnosticsGenerator` (see commit `8701b42`).
- Generators write with `SourceWriter.Create()` (an `IndentedTextWriter`) and emit via `ctx.AddSource("<Name>.g.cs", SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8))`. Match the existing brace/indent style exactly.
- Fast generator tests live in `EZRestAPI.Tests` and run via `GeneratorHarness.Run(source)`; integration tests live in `Example.Tests`, use `[Collection("MsSql")]` and `WebApplicationFactory<Program>`.
- New diagnostic id is **`EZR011`** (`EZR001`–`EZR010` are taken).
- Route segments are the plural name lowercased via `ToLowerInvariant()` (existing `EndpointsGenerator.Route`).
- Run generator tests with `dotnet test EZRestAPI.Tests`. Run integration tests with `dotnet test Example.Tests` (requires Docker).
- Commit after every task with a message in the repo's style; end the message with the `Co-Authored-By` trailer used by the project.

---

## Data model reference (already in `EZRestAPI/Providers/ProviderExtensions.cs`)

```csharp
public record Model(
    string AssemblyName, string ModelNamespace, string ModelName, string ClassName,
    string SingularName, string PluralName, EquatableArray<Property> Properties,
    string? UserIdTypeName = null);

public record Property(
    bool IsRequired, string TypeName, string PropertyName, bool IsNonNullableReferenceType,
    NestedKind Kind = NestedKind.None, NestedType? Nested = null,
    bool IsModelReference = false, bool IsUnsupportedNestedShape = false);
```

`Property.TypeName` is `property.Type.ToDisplayString()`; for `int` it is `"int"`, for `int?` it is `"int?"`, for `Guid` it is `"System.Guid"`. `EquatableArray<T>` lives in `EZRestAPI/Utils/EquatableArray.cs` and is constructed as `new EquatableArray<T>(array)`.

---

## Task 1: Relationship metadata, `[Scalar]` attribute, and DbContext FK configuration

**Files:**
- Modify: `EZRestAPI/Providers/ProviderExtensions.cs`
- Modify: `EZRestAPI/Generators/AttributesGenerator.cs`
- Modify: `EZRestAPI/Generators/DbContextGenerator.cs`
- Test: `EZRestAPI.Tests/RelationshipTests.cs` (create)

**Interfaces:**
- Produces:
  - `record RelationshipInfo(string ChildSingularName, string ChildPluralName, string ChildClassName, string ParentSingularName, string ParentPluralName, string ParentClassName, string ForeignKeyPropertyName, bool IsNullable)`
  - `Model` gains `EquatableArray<RelationshipInfo> ParentRelationships` (this model's FKs → parents) and `EquatableArray<RelationshipInfo> ChildRelationships` (FKs pointing at this model), both defaulting to empty.
  - `Property` gains `bool IsScalarOptOut = false`.
  - `ProviderExtensions.ScalarAttributeName = "EZRestAPI.ScalarAttribute"`.
  - `IncrementalValuesProvider<Model> GetModelsWithRelationships(this SyntaxValueProvider provider)` — per-model values, each with the two relationship arrays populated.
  - `[EZRestAPI.Scalar]` attribute type (source-generated), usable on properties.

- [ ] **Step 1: Write the failing test**

Create `EZRestAPI.Tests/RelationshipTests.cs`:

```csharp
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
        var result = GeneratorHarness.Run("""
            namespace Tests;

            [EZRestAPI.Model("Author", "Authors")]
            public partial class AuthorModel
            {
                public required string Name { get; set; }
                public System.Guid OrderId { get; set; }
            }
            """);
        var context = GeneratorHarness.GetSource(result, "CustomDbContext.g.cs");

        Assert.DoesNotContain("HasForeignKey", context);
    }

    [Fact]
    public void ScalarOptOut_SuppressesForeignKey()
    {
        var result = GeneratorHarness.Run("""
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
            """);
        var context = GeneratorHarness.GetSource(result, "CustomDbContext.g.cs");

        Assert.DoesNotContain("HasForeignKey", context);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test EZRestAPI.Tests --filter RelationshipTests`
Expected: FAIL — `[EZRestAPI.Scalar]` is unknown / `CustomDbContext.g.cs` contains no `HasForeignKey`.

- [ ] **Step 3: Add the `[Scalar]` attribute source**

In `AttributesGenerator.Initialize`, inside the `RegisterPostInitializationOutput` callback, add a third `ctx.AddSource` after the nested attribute:

```csharp
ctx.AddSource(
    "EZRestAPIScalar.g.cs",
    SourceText.From(
        """
        // <auto-generated/>
        #nullable enable

        namespace EZRestAPI;

        using System;

        [AttributeUsage(AttributeTargets.Property)]
        public partial class ScalarAttribute : Attribute
        {
        }
        """,
        Encoding.UTF8
    )
);
```

- [ ] **Step 4: Add `RelationshipInfo`, model/property fields, `[Scalar]` detection, and the resolution provider**

In `ProviderExtensions.cs`:

1. Add the constant near the existing ones:

```csharp
public const string ScalarAttributeName = "EZRestAPI.ScalarAttribute";
```

2. Add the record (next to `Model`):

```csharp
public record RelationshipInfo(
    string ChildSingularName,
    string ChildPluralName,
    string ChildClassName,
    string ParentSingularName,
    string ParentPluralName,
    string ParentClassName,
    string ForeignKeyPropertyName,
    bool IsNullable
);
```

3. Extend `Model` with two fields (append after `UserIdTypeName`, both defaulted so existing construction sites stay valid):

```csharp
    string? UserIdTypeName = null,
    EquatableArray<RelationshipInfo> ParentRelationships = default,
    EquatableArray<RelationshipInfo> ChildRelationships = default
```

4. Add `bool IsScalarOptOut = false` as the final `Property` constructor parameter.

5. In `CreateProperty`, set `IsScalarOptOut` in the `Plain` local function:

```csharp
Property Plain(bool isModelReference = false, bool isUnsupportedNestedShape = false) =>
    new(
        IsRequired: property.IsRequired,
        TypeName: property.Type.ToDisplayString(),
        PropertyName: property.Name,
        IsNonNullableReferenceType: isNonNullableReferenceType,
        IsModelReference: isModelReference,
        IsUnsupportedNestedShape: isUnsupportedNestedShape,
        IsScalarOptOut: property
            .GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == ScalarAttributeName)
    );
```

6. Add the resolution provider and helper:

```csharp
public static IncrementalValuesProvider<Model> GetModelsWithRelationships(
    this SyntaxValueProvider provider
)
{
    return provider
        .GetModels()
        .Collect()
        .SelectMany(static (models, _) => ResolveRelationships(models));
}

private static ImmutableArray<Model> ResolveRelationships(ImmutableArray<Model> models)
{
    var bySingular = new Dictionary<string, Model>();
    foreach (var m in models)
    {
        bySingular[m.SingularName] = m;
    }

    // First pass: each model's own foreign keys (edges to its parents).
    var parentEdges = new Dictionary<string, List<RelationshipInfo>>();
    var childEdges = new Dictionary<string, List<RelationshipInfo>>();

    foreach (var child in models)
    {
        foreach (var p in child.Properties)
        {
            if (p.Kind != NestedKind.None || p.IsScalarOptOut)
            {
                continue;
            }
            if (p.TypeName != "int" && p.TypeName != "int?")
            {
                continue;
            }
            if (!p.PropertyName.EndsWith("Id") || p.PropertyName.Length <= 2)
            {
                continue;
            }
            var parentSingular = p.PropertyName.Substring(0, p.PropertyName.Length - 2);
            if (parentSingular == child.SingularName)
            {
                continue; // self-reference: out of scope this cycle
            }
            if (!bySingular.TryGetValue(parentSingular, out var parent))
            {
                continue; // unresolved -> handled by EZR011, not here
            }

            var edge = new RelationshipInfo(
                ChildSingularName: child.SingularName,
                ChildPluralName: child.PluralName,
                ChildClassName: child.ClassName,
                ParentSingularName: parent.SingularName,
                ParentPluralName: parent.PluralName,
                ParentClassName: parent.ClassName,
                ForeignKeyPropertyName: p.PropertyName,
                IsNullable: p.TypeName == "int?"
            );

            (parentEdges.TryGetValue(child.SingularName, out var pe)
                ? pe
                : parentEdges[child.SingularName] = new()).Add(edge);
            (childEdges.TryGetValue(parent.SingularName, out var ce)
                ? ce
                : childEdges[parent.SingularName] = new()).Add(edge);
        }
    }

    return models
        .Select(m => m with
        {
            ParentRelationships = new EquatableArray<RelationshipInfo>(
                parentEdges.TryGetValue(m.SingularName, out var pe) ? pe.ToArray() : []),
            ChildRelationships = new EquatableArray<RelationshipInfo>(
                childEdges.TryGetValue(m.SingularName, out var ce) ? ce.ToArray() : []),
        })
        .ToImmutableArray();
}
```

(`System.Collections.Generic` and `System.Collections.Immutable` are already imported.)

- [ ] **Step 5: Emit FK configuration from `DbContextGenerator`**

Switch its provider to relationships and add FK config. Change line 45 from `GetModels().Collect()` to:

```csharp
var modelsProvider = context.SyntaxProvider.GetModelsWithRelationships().Collect();
```

After the existing owned-type `OnModelCreating` block (or extend the same method), the `OnModelCreating` body must also be emitted whenever any model has `ParentRelationships`. Replace the `modelsWithNested.Count > 0` guard so the method is written when **either** nested owned types **or** relationships exist:

```csharp
var modelsWithRelationships = models
    .Where(m => m.ParentRelationships.Any())
    .ToList();

if (modelsWithNested.Count > 0 || modelsWithRelationships.Count > 0)
{
    writer.WriteLine();
    writer.WriteLine("protected override void OnModelCreating(ModelBuilder modelBuilder)");
    writer.WriteLine("{");
    writer.Indent++;
    writer.WriteLine("base.OnModelCreating(modelBuilder);");

    // ... existing owned-type loop over modelsWithNested stays here ...

    foreach (var model in modelsWithRelationships)
    {
        foreach (var rel in model.ParentRelationships)
        {
            writer.WriteLine();
            writer.WriteLine($"modelBuilder.Entity<{model.ClassName}>()");
            writer.Indent++;
            writer.WriteLine($".HasOne<{rel.ParentClassName}>()");
            writer.WriteLine(".WithMany()");
            writer.WriteLine($".HasForeignKey(e => e.{rel.ForeignKeyPropertyName})");
            writer.WriteLine(".OnDelete(DeleteBehavior.Restrict);");
            writer.Indent--;
        }
    }

    writer.Indent--;
    writer.WriteLine("}");
}
```

Note: `model.ClassName` and `rel.ParentClassName` are already fully qualified (`global::Tests.BookModel`), so `HasOne<global::Tests.AuthorModel>()` renders correctly.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test EZRestAPI.Tests --filter RelationshipTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Run the whole fast suite to catch regressions**

Run: `dotnet test EZRestAPI.Tests`
Expected: PASS (all existing + new).

- [ ] **Step 8: Commit**

```bash
git add EZRestAPI/Providers/ProviderExtensions.cs EZRestAPI/Generators/AttributesGenerator.cs EZRestAPI/Generators/DbContextGenerator.cs EZRestAPI.Tests/RelationshipTests.cs
git commit -m "Detect {Name}Id foreign keys by convention and configure RESTRICT relationships."
```

---

## Task 2: `EZR011` diagnostic for FK-shaped but unresolved properties

**Files:**
- Modify: `EZRestAPI/Generators/DiagnosticsGenerator.cs`
- Test: `EZRestAPI.Tests/DiagnosticsTests.cs` (append)

**Interfaces:**
- Consumes: `ProviderExtensions.GetModelsWithDiagnostics()` (already collected in `DiagnosticsGenerator.Initialize`), each `ModelDiagnostics` exposing `Model` (with `Properties`) and `Location`.
- Produces: warning `EZR011` reported for an `int`/`int?` property named `{X}Id` where no `[Model]` has singular name `X` and the property is not `[Scalar]`.

- [ ] **Step 1: Write the failing test**

Append to `EZRestAPI.Tests/DiagnosticsTests.cs`:

```csharp
    [Fact]
    public void ForeignKeyShapedWithoutMatchingModel_ReportsEZR011()
    {
        var result = GeneratorHarness.Run("""
            namespace Tests;

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel
            {
                public required string Title { get; set; }
                public required int PublisherId { get; set; }
            }
            """);

        Assert.Contains("EZR011", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void GuidIdWithoutModel_DoesNotReportEZR011()
    {
        var result = GeneratorHarness.Run("""
            namespace Tests;

            [EZRestAPI.Model("Author", "Authors")]
            public partial class AuthorModel
            {
                public required string Name { get; set; }
                public System.Guid OrderId { get; set; }
            }
            """);

        Assert.DoesNotContain("EZR011", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void ResolvedForeignKey_DoesNotReportEZR011()
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

        Assert.DoesNotContain("EZR011", GeneratorHarness.DiagnosticIds(result));
    }

    [Fact]
    public void ScalarOptOutForeignKeyShape_DoesNotReportEZR011()
    {
        var result = GeneratorHarness.Run("""
            namespace Tests;

            [EZRestAPI.Model("Book", "Books")]
            public partial class BookModel
            {
                public required string Title { get; set; }
                [EZRestAPI.Scalar]
                public required int PublisherId { get; set; }
            }
            """);

        Assert.DoesNotContain("EZR011", GeneratorHarness.DiagnosticIds(result));
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test EZRestAPI.Tests --filter DiagnosticsTests`
Expected: FAIL — `EZR011` never reported.

- [ ] **Step 3: Add the descriptor**

In `DiagnosticsGenerator.cs`, after `ModelAndNested` (EZR010):

```csharp
public static readonly DiagnosticDescriptor UnresolvedForeignKey = new(
    "EZR011",
    "Foreign-key-shaped property has no matching model",
    "Property '{0}' on '{1}' is named like a foreign key but no [EZRestAPI.Model] has singular name '{2}'; create that model, or mark the property [EZRestAPI.Scalar] to treat it as a plain value",
    Category,
    DiagnosticSeverity.Warning,
    isEnabledByDefault: true
);
```

- [ ] **Step 4: Report it in the diagnostics pipeline**

Inside `Initialize`, within the `RegisterSourceOutput` callback that already has `models` (collected `ModelDiagnostics`) and `nestedModels`, add a pass. Build the set of known singular names once, then scan each model's `int`/`int?` `{X}Id` properties:

```csharp
var knownSingulars = new System.Collections.Generic.HashSet<string>(
    models.Select(m => m.Model.SingularName));

foreach (var md in models)
{
    foreach (var p in md.Model.Properties)
    {
        if (p.Kind != ProviderExtensions.NestedKind.None || p.IsScalarOptOut)
        {
            continue;
        }
        if (p.TypeName != "int" && p.TypeName != "int?")
        {
            continue;
        }
        if (!p.PropertyName.EndsWith("Id") || p.PropertyName.Length <= 2)
        {
            continue;
        }
        var parentSingular = p.PropertyName.Substring(0, p.PropertyName.Length - 2);
        if (parentSingular == md.Model.SingularName || knownSingulars.Contains(parentSingular))
        {
            continue;
        }

        ctx.ReportDiagnostic(
            Diagnostic.Create(
                UnresolvedForeignKey,
                md.Location?.ToLocation(),
                p.PropertyName,
                md.Model.ModelName,
                parentSingular
            )
        );
    }
}
```

If `LocationInfo` does not expose `ToLocation()`, inspect `EZRestAPI/Providers/LocationInfo.cs` and use the same conversion the existing EZR00x reports use in this file (mirror the call already present for another diagnostic). Passing `null` for the location is acceptable if that matches existing usage.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test EZRestAPI.Tests --filter DiagnosticsTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add EZRestAPI/Generators/DiagnosticsGenerator.cs EZRestAPI.Tests/DiagnosticsTests.cs
git commit -m "Warn (EZR011) on foreign-key-shaped properties with no matching model."
```

---

## Task 3: `PagedResponse<T>` support type and paginated flat list endpoint

**Files:**
- Modify: `EZRestAPI/Generators/DtoGenerator.cs`
- Modify: `EZRestAPI/Generators/RepositoryGenerator.cs`
- Modify: `EZRestAPI/Generators/EndpointsGenerator.cs`
- Test: `EZRestAPI.Tests/PaginationTests.cs` (create)

**Interfaces:**
- Produces:
  - `PagedResponse<T>` class in namespace `{AssemblyName}` with `List<T> Items`, `int TotalCount`, `int Page`, `int PageSize` (generated once per assembly).
  - Repository method `public async Task<PagedResponse<Read{Name}Response>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)`.
  - Flat route `GET /{plural}?page=&pageSize=` returning `200` with the envelope, `400` when `page < 1` or `pageSize < 1`, `pageSize` clamped to `100`.
- Consumes: existing `Read{Name}Response` DTO and `Read{Name}Response` mapping from `InsertReadMethod`.

- [ ] **Step 1: Write the failing test**

Create `EZRestAPI.Tests/PaginationTests.cs`:

```csharp
namespace EZRestAPI.Tests;

public class PaginationTests
{
    private const string Source = """
        namespace Tests;

        [EZRestAPI.Model("Widget", "Widgets")]
        public partial class WidgetModel
        {
            public required string Name { get; set; }
        }
        """;

    [Fact]
    public void PagedResponseType_IsGeneratedOnce()
    {
        var result = GeneratorHarness.Run(Source);
        var dto = GeneratorHarness.GetSource(result, "PagedResponse.g.cs");

        Assert.Contains("public class PagedResponse<T>", dto);
        Assert.Contains("public int TotalCount { get; set; }", dto);
        Assert.Contains("public int Page { get; set; }", dto);
        Assert.Contains("public int PageSize { get; set; }", dto);
    }

    [Fact]
    public void Repository_HasPaginatedListAsync()
    {
        var result = GeneratorHarness.Run(Source);
        var repo = GeneratorHarness.GetSource(result, "WidgetRepository.g.cs");

        Assert.Contains(
            "public async Task<PagedResponse<ReadWidgetResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)",
            repo);
        Assert.Contains(".Skip((page - 1) * pageSize)", repo);
        Assert.Contains(".Take(pageSize)", repo);
        Assert.Contains(".OrderBy(e => e.Id)", repo);
    }

    [Fact]
    public void FlatListEndpoint_ValidatesAndClampsPaging()
    {
        var result = GeneratorHarness.Run(Source);
        var endpoints = GeneratorHarness.GetSource(result, "WidgetEndpoints.g.cs");

        Assert.Contains("group.MapGet(\"/\", async (", endpoints);
        Assert.Contains("if (page < 1 || pageSize < 1)", endpoints);
        Assert.Contains("pageSize = System.Math.Min(pageSize, 100);", endpoints);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test EZRestAPI.Tests --filter PaginationTests`
Expected: FAIL — `PagedResponse.g.cs` not generated; no `ListAsync`; no list route.

- [ ] **Step 3: Generate `PagedResponse<T>` once**

In `DtoGenerator.Initialize`, after the four `RegisterDto` calls, register a collected output:

```csharp
context.RegisterSourceOutput(
    modelsProvider.Collect(),
    (ctx, models) =>
    {
        if (models.IsDefaultOrEmpty)
        {
            return;
        }

        var writer = SourceWriter.Create();
        writer.WriteLine($"namespace {models[0].AssemblyName};");
        writer.WriteLine();
        writer.WriteLine("using System.Collections.Generic;");
        writer.WriteLine();
        writer.WriteLine("public class PagedResponse<T>");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("public List<T> Items { get; set; } = new();");
        writer.WriteLine("public int TotalCount { get; set; }");
        writer.WriteLine("public int Page { get; set; }");
        writer.WriteLine("public int PageSize { get; set; }");
        writer.Indent--;
        writer.WriteLine("}");

        ctx.AddSource(
            "PagedResponse.g.cs",
            SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8));
    });
```

- [ ] **Step 4: Add `ListAsync` to the repository**

In `RepositoryGenerator.cs`, add a method emitter and call it in `Initialize` (after `InsertReadMethod`, before update):

```csharp
private static void InsertListMethod(IndentedTextWriter writer, ProviderExtensions.Model model)
{
    writer.WriteLine(
        $"public async Task<PagedResponse<Read{model.SingularName}Response>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)");
    writer.WriteLine("{");
    writer.Indent++;
    writer.WriteLine($"var query = context.{model.PluralName}.AsNoTracking().OrderBy(e => e.Id);");
    writer.WriteLine("var totalCount = await query.CountAsync(cancellationToken);");
    writer.WriteLine("var entities = await query");
    writer.Indent++;
    writer.WriteLine(".Skip((page - 1) * pageSize)");
    writer.WriteLine(".Take(pageSize)");
    writer.WriteLine(".ToListAsync(cancellationToken);");
    writer.Indent--;
    writer.WriteLine();
    writer.WriteLine($"var items = new System.Collections.Generic.List<Read{model.SingularName}Response>();");
    writer.WriteLine("foreach (var entity in entities)");
    writer.WriteLine("{");
    writer.Indent++;
    writer.WriteLine($"items.Add(new Read{model.SingularName}Response()");
    writer.WriteLine("{");
    writer.Indent++;
    writer.WriteLine("Id = entity.Id,");
    foreach (var property in model.Properties)
    {
        writer.WriteLine($"{property.PropertyName} = {property.ToDtoExpression($"entity.{property.PropertyName}")},");
    }
    writer.Indent--;
    writer.WriteLine("});");
    writer.Indent--;
    writer.WriteLine("}");
    writer.WriteLine();
    writer.WriteLine($"return new PagedResponse<Read{model.SingularName}Response>()");
    writer.WriteLine("{");
    writer.Indent++;
    writer.WriteLine("Items = items,");
    writer.WriteLine("TotalCount = totalCount,");
    writer.WriteLine("Page = page,");
    writer.WriteLine("PageSize = pageSize,");
    writer.Indent--;
    writer.WriteLine("};");
    writer.Indent--;
    writer.WriteLine("}");
}
```

Wire it in `Initialize` after the `InsertReadMethod(writer, model);` line:

```csharp
writer.WriteLine();
InsertListMethod(writer, model);
```

- [ ] **Step 5: Add the flat list endpoint**

In `EndpointsGenerator.cs`, add an emitter and call it in `Initialize` right after `var group = app.MapGroup(...)` / before `InsertCreateEndpoint`:

```csharp
private static void InsertListEndpoint(IndentedTextWriter writer, ProviderExtensions.Model model)
{
    writer.WriteLine("group.MapGet(\"/\", async (");
    writer.Indent++;
    writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
    writer.WriteLine("[FromQuery] int page,");
    writer.WriteLine("[FromQuery] int pageSize,");
    writer.WriteLine("CancellationToken cancellationToken) =>");
    writer.Indent--;
    writer.WriteLine("{");
    writer.Indent++;
    writer.WriteLine("if (page < 1) page = 1;");
    writer.WriteLine("if (pageSize < 1) pageSize = 20;");
    writer.WriteLine("pageSize = System.Math.Min(pageSize, 100);");
    writer.WriteLine();
    writer.WriteLine("var response = await repository.ListAsync(page, pageSize, cancellationToken);");
    writer.WriteLine("return Results.Ok(response);");
    writer.Indent--;
    writer.WriteLine($"}}).WithName(\"List{model.PluralName}\");");
}
```

Wire it in after `writer.WriteLine($"var group = app.MapGroup(\"/{Route(model)}\");");`:

```csharp
writer.WriteLine();
InsertListEndpoint(writer, model);
```

Note on the test assertion `if (page < 1 || pageSize < 1)`: to satisfy the test **and** keep good behavior, use explicit `400` for out-of-range paging instead of silent defaulting. Replace the two `if` lines above with:

```csharp
    writer.WriteLine("if (page < 1 || pageSize < 1)");
    writer.WriteLine("{");
    writer.Indent++;
    writer.WriteLine("return Results.BadRequest(\"page and pageSize must be >= 1.\");");
    writer.Indent--;
    writer.WriteLine("}");
    writer.WriteLine("pageSize = System.Math.Min(pageSize, 100);");
```

(Use `[FromQuery] int page = 1` / `int pageSize = 20` default values in the lambda parameter list so callers may omit them; render them as `"[FromQuery] int page = 1,"` and `"[FromQuery] int pageSize = 20,"`.)

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test EZRestAPI.Tests --filter PaginationTests`
Expected: PASS.

- [ ] **Step 7: Full fast suite**

Run: `dotnet test EZRestAPI.Tests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add EZRestAPI/Generators/DtoGenerator.cs EZRestAPI/Generators/RepositoryGenerator.cs EZRestAPI/Generators/EndpointsGenerator.cs EZRestAPI.Tests/PaginationTests.cs
git commit -m "Generate PagedResponse<T> and a paginated flat list endpoint per model."
```

---

## Task 4: Flat-write foreign-key conflict (409) and parent-delete conflict (409)

**Files:**
- Modify: `EZRestAPI/Generators/DtoGenerator.cs` (emit `WriteResult` enum once)
- Modify: `EZRestAPI/Generators/RepositoryGenerator.cs` (conflict-aware create/update/delete for related models)
- Modify: `EZRestAPI/Generators/EndpointsGenerator.cs` (map conflict to 409)
- Test: `EZRestAPI.Tests/ConflictTests.cs` (create)

**Interfaces:**
- Produces:
  - `enum WriteResult { Success, NotFound, Conflict }` in namespace `{AssemblyName}` (once per assembly).
  - For a model with `ParentRelationships`: `CreateAsync` returns `Task<int?>` (`null` = conflict) and `UpdateAsync` returns `Task<WriteResult>`.
  - For a model with `ChildRelationships` (a parent): `DeleteAsync` returns `Task<WriteResult>` (`Conflict` when dependents exist).
  - Models with **neither** keep the existing `Task<int>` / `Task<bool>` / `Task<bool>` signatures unchanged.
- Consumes: `model.ParentRelationships`, `model.ChildRelationships` (Task 1). Requires the generators to read from `GetModelsWithRelationships()` instead of `GetModels()`.

- [ ] **Step 1: Switch Repository + Endpoints generators to the relationship-aware provider**

In both `RepositoryGenerator.Initialize` and `EndpointsGenerator.Initialize`, change:

```csharp
var modelsProvider = context.SyntaxProvider.GetModels();
```
to
```csharp
var modelsProvider = context.SyntaxProvider.GetModelsWithRelationships();
```

(Behavior is unchanged for models without relationships; this only makes the arrays available.)

- [ ] **Step 2: Write the failing test**

Create `EZRestAPI.Tests/ConflictTests.cs`:

```csharp
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
        Assert.Contains("public async Task<int?> CreateAsync(CreateBookRequest request, CancellationToken cancellationToken)", repo);
        Assert.Contains("context.Authors.AnyAsync(p => p.Id == request.AuthorId", repo);
    }

    [Fact]
    public void ParentDelete_ChecksDependentsAndReturnsWriteResult()
    {
        var result = GeneratorHarness.Run(Source);
        var repo = GeneratorHarness.GetSource(result, "AuthorRepository.g.cs");
        Assert.Contains("public async Task<WriteResult> DeleteAsync(int id, CancellationToken cancellationToken)", repo);
        Assert.Contains("context.Books.AnyAsync(c => c.AuthorId == id", repo);
    }

    [Fact]
    public void UnrelatedModel_KeepsSimpleSignatures()
    {
        var result = GeneratorHarness.Run("""
            namespace Tests;
            [EZRestAPI.Model("Tag", "Tags")]
            public partial class TagModel { public required string Name { get; set; } }
            """);
        var repo = GeneratorHarness.GetSource(result, "TagRepository.g.cs");
        Assert.Contains("public async Task<int> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken)", repo);
        Assert.Contains("public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)", repo);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test EZRestAPI.Tests --filter ConflictTests`
Expected: FAIL.

- [ ] **Step 4: Emit `WriteResult` once**

In `DtoGenerator.Initialize`, inside the same collected `RegisterSourceOutput` you added in Task 3 (or a second one), also emit:

```csharp
var w = SourceWriter.Create();
w.WriteLine($"namespace {models[0].AssemblyName};");
w.WriteLine();
w.WriteLine("public enum WriteResult");
w.WriteLine("{");
w.Indent++;
w.WriteLine("Success,");
w.WriteLine("NotFound,");
w.WriteLine("Conflict,");
w.Indent--;
w.WriteLine("}");
ctx.AddSource("WriteResult.g.cs", SourceText.From(w.InnerWriter.ToString(), Encoding.UTF8));
```

- [ ] **Step 5: Make create/update conflict-aware for related models**

In `RepositoryGenerator`, branch `InsertCreateMethod` and `InsertUpdateMethod` on `model.ParentRelationships.Any()`.

For **create** when there are parent relationships, change the signature and prepend existence checks before `context.{Plural}.Add(entity)`:

```csharp
// signature:
$"public async Task<int?> CreateAsync(Create{model.SingularName}Request request, CancellationToken cancellationToken)"
// after opening brace, before building the entity, for each relationship:
foreach (var rel in model.ParentRelationships)
{
    if (rel.IsNullable)
    {
        writer.WriteLine($"if (request.{rel.ForeignKeyPropertyName} is not null && !await context.{rel.ParentPluralName}.AnyAsync(p => p.Id == request.{rel.ForeignKeyPropertyName}, cancellationToken))");
    }
    else
    {
        writer.WriteLine($"if (!await context.{rel.ParentPluralName}.AnyAsync(p => p.Id == request.{rel.ForeignKeyPropertyName}, cancellationToken))");
    }
    writer.WriteLine("{");
    writer.Indent++;
    writer.WriteLine("return null;");
    writer.Indent--;
    writer.WriteLine("}");
    writer.WriteLine();
}
```

For **update** when there are parent relationships, change the signature to `Task<WriteResult>`, return `WriteResult.NotFound` where it currently returns `false`, add the same FK pre-checks returning `WriteResult.Conflict`, and return `WriteResult.Success` at the end (instead of `true`).

Keep the existing (non-branched) bodies verbatim for models with no parent relationships.

- [ ] **Step 6: Make delete conflict-aware for parent models**

In `RepositoryGenerator.InsertDeleteMethod`, branch on `model.ChildRelationships.Any()`. When true:

```csharp
writer.WriteLine($"public async Task<WriteResult> DeleteAsync(int id, CancellationToken cancellationToken)");
writer.WriteLine("{");
writer.Indent++;
writer.WriteLine("var entity = await context." + model.PluralName + ".FirstOrDefaultAsync(e => e.Id == id, cancellationToken);");
writer.WriteLine("if (entity is null)");
writer.WriteLine("{");
writer.Indent++;
writer.WriteLine("return WriteResult.NotFound;");
writer.Indent--;
writer.WriteLine("}");
writer.WriteLine();
foreach (var rel in model.ChildRelationships)
{
    writer.WriteLine($"if (await context.{rel.ChildPluralName}.AnyAsync(c => c.{rel.ForeignKeyPropertyName} == id, cancellationToken))");
    writer.WriteLine("{");
    writer.Indent++;
    writer.WriteLine("return WriteResult.Conflict;");
    writer.Indent--;
    writer.WriteLine("}");
    writer.WriteLine();
}
writer.WriteLine($"context.{model.PluralName}.Remove(entity);");
writer.WriteLine("await context.SaveChangesAsync(cancellationToken);");
writer.WriteLine("return WriteResult.Success;");
writer.Indent--;
writer.WriteLine("}");
```

Keep the existing `ExecuteDeleteAsync`-based body for models with no child relationships.

- [ ] **Step 7: Map results to status codes in endpoints**

In `EndpointsGenerator`, branch the create/update/delete endpoints on the same conditions.

- Create (parent relationships present): `var id = await repository.CreateAsync(request, cancellationToken);` then:
  ```csharp
  writer.WriteLine("if (id is null)");
  writer.WriteLine("{");
  writer.Indent++;
  writer.WriteLine("return Results.Conflict();");
  writer.Indent--;
  writer.WriteLine("}");
  ```
  and use `id.Value` when building the response / `Results.Created`.
- Update (parent relationships present):
  ```csharp
  writer.WriteLine("var outcome = await repository.UpdateAsync(id, request, cancellationToken);");
  writer.WriteLine("return outcome switch");
  writer.WriteLine("{");
  writer.Indent++;
  writer.WriteLine("WriteResult.Success => Results.NoContent(),");
  writer.WriteLine("WriteResult.Conflict => Results.Conflict(),");
  writer.WriteLine("_ => Results.NotFound(),");
  writer.Indent--;
  writer.WriteLine("};");
  ```
- Delete (child relationships present — i.e., a parent):
  ```csharp
  writer.WriteLine("var outcome = await repository.DeleteAsync(id, cancellationToken);");
  writer.WriteLine("return outcome switch");
  writer.WriteLine("{");
  writer.Indent++;
  writer.WriteLine("WriteResult.Success => Results.NoContent(),");
  writer.WriteLine("WriteResult.Conflict => Results.Conflict(),");
  writer.WriteLine("_ => Results.NotFound(),");
  writer.Indent--;
  writer.WriteLine("};");
  ```

Leave the existing endpoint bodies unchanged for models without the relevant relationships.

- [ ] **Step 8: Run to verify pass, then full suite**

Run: `dotnet test EZRestAPI.Tests --filter ConflictTests`
Expected: PASS.
Run: `dotnet test EZRestAPI.Tests`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add EZRestAPI/Generators/DtoGenerator.cs EZRestAPI/Generators/RepositoryGenerator.cs EZRestAPI/Generators/EndpointsGenerator.cs EZRestAPI.Tests/ConflictTests.cs
git commit -m "Return 409 for bad foreign keys on write and for deleting a referenced parent."
```

---

## Task 5: Nested request DTOs that omit the foreign key

**Files:**
- Modify: `EZRestAPI/Generators/DtoGenerator.cs`
- Test: `EZRestAPI.Tests/NestedDtoTests.cs` (create)

**Interfaces:**
- Produces, for each `RelationshipInfo` where a model is the child: `Create{Child}Under{Parent}Request` and `Update{Child}Under{Parent}Request` classes in `{AssemblyName}`, containing every model property **except** the FK property, and (for update) **no** `Id` line (the id is in the route).
- Consumes: `model.ParentRelationships` — requires `DtoGenerator` to also read `GetModelsWithRelationships()` for this registration.

- [ ] **Step 1: Write the failing test**

Create `EZRestAPI.Tests/NestedDtoTests.cs`:

```csharp
namespace EZRestAPI.Tests;

public class NestedDtoTests
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
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test EZRestAPI.Tests --filter NestedDtoTests`
Expected: FAIL — those sources are not generated.

- [ ] **Step 3: Generate the nested DTOs**

In `DtoGenerator.Initialize`, add a registration over `context.SyntaxProvider.GetModelsWithRelationships()` (a new local `var relModelsProvider = ...`). For each model, for each `rel` in `model.ParentRelationships`, emit two DTO classes. Reuse the existing property-writing loop but **skip** the FK property:

```csharp
context.RegisterSourceOutput(
    relModelsProvider,
    (ctx, model) =>
    {
        foreach (var rel in model.ParentRelationships)
        {
            EmitNestedDto(ctx, model, rel, $"Create{rel.ChildSingularName}Under{rel.ParentSingularName}Request");
            EmitNestedDto(ctx, model, rel, $"Update{rel.ChildSingularName}Under{rel.ParentSingularName}Request");
        }
    });
```

Add the helper:

```csharp
private static void EmitNestedDto(
    SourceProductionContext ctx,
    ProviderExtensions.Model model,
    ProviderExtensions.RelationshipInfo rel,
    string className)
{
    var writer = SourceWriter.Create();
    writer.WriteLine($"namespace {model.AssemblyName};");
    writer.WriteLine();
    writer.WriteLine($"public class {className}");
    writer.WriteLine("{");
    writer.Indent++;
    foreach (var property in model.Properties)
    {
        if (property.PropertyName == rel.ForeignKeyPropertyName)
        {
            continue;
        }
        writer.WriteLine(
            $"public {(property.NeedsRequiredModifier ? "required " : "")}{property.DtoTypeName} {property.PropertyName} {{ get; set; }}");
    }
    writer.Indent--;
    writer.WriteLine("}");
    ctx.AddSource($"{className}.g.cs", SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8));
}
```

- [ ] **Step 4: Run to verify pass, then full suite**

Run: `dotnet test EZRestAPI.Tests --filter NestedDtoTests`
Expected: PASS.
Run: `dotnet test EZRestAPI.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add EZRestAPI/Generators/DtoGenerator.cs EZRestAPI.Tests/NestedDtoTests.cs
git commit -m "Generate nested create/update DTOs that omit the route-owned foreign key."
```

---

## Task 6: Repository scoped methods for nested routes

**Files:**
- Modify: `EZRestAPI/Generators/RepositoryGenerator.cs`
- Test: `EZRestAPI.Tests/ScopedRepositoryTests.cs` (create)

**Interfaces:**
- Produces on the child repository, for each `rel` in `model.ParentRelationships`:
  - `Task<PagedResponse<Read{Child}Response>?> List{Child}By{Parent}Async(int parentId, int page, int pageSize, CancellationToken ct)` — `null` when the parent does not exist.
  - `Task<int?> Create{Child}Under{Parent}Async(int parentId, Create{Child}Under{Parent}Request request, CancellationToken ct)` — `null` when the parent does not exist.
  - `Task<Read{Child}Response?> Read{Child}Under{Parent}Async(int parentId, int id, CancellationToken ct)` — filters on both `Id` and the FK.
  - `Task<bool> Update{Child}Under{Parent}Async(int parentId, int id, Update{Child}Under{Parent}Request request, CancellationToken ct)` — `false` when not found or wrong parent.
  - `Task<WriteResult> Delete{Child}Under{Parent}Async(int parentId, int id, CancellationToken ct)` — `NotFound` (missing/wrong parent), `Conflict` (child itself has dependents), else `Success`.
- Consumes: nested DTOs (Task 5), `WriteResult` and `PagedResponse<T>` (Tasks 3–4).

- [ ] **Step 1: Write the failing test**

Create `EZRestAPI.Tests/ScopedRepositoryTests.cs`:

```csharp
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

        Assert.Contains("Task<PagedResponse<ReadBookResponse>?> ListBookByAuthorAsync(int parentId, int page, int pageSize, CancellationToken cancellationToken)", repo);
        Assert.Contains("Task<int?> CreateBookUnderAuthorAsync(int parentId, CreateBookUnderAuthorRequest request, CancellationToken cancellationToken)", repo);
        Assert.Contains("Task<ReadBookResponse?> ReadBookUnderAuthorAsync(int parentId, int id, CancellationToken cancellationToken)", repo);
        Assert.Contains("Task<bool> UpdateBookUnderAuthorAsync(int parentId, int id, UpdateBookUnderAuthorRequest request, CancellationToken cancellationToken)", repo);
        Assert.Contains("Task<WriteResult> DeleteBookUnderAuthorAsync(int parentId, int id, CancellationToken cancellationToken)", repo);
        Assert.Contains("e.AuthorId == parentId", repo);
        Assert.Contains("AuthorId = parentId,", repo);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test EZRestAPI.Tests --filter ScopedRepositoryTests`
Expected: FAIL.

- [ ] **Step 3: Emit the scoped methods**

In `RepositoryGenerator.Initialize`, after the flat methods and before closing the class, add:

```csharp
foreach (var rel in model.ParentRelationships)
{
    writer.WriteLine();
    InsertScopedMethods(writer, model, rel);
}
```

Implement `InsertScopedMethods(IndentedTextWriter writer, ProviderExtensions.Model model, ProviderExtensions.RelationshipInfo rel)` emitting the five methods. Key bodies:

- **List** (parent existence → `null`; else page the filtered set, mapping each entity to `Read{Child}Response` exactly as `InsertReadMethod`/`InsertListMethod` do):
  ```csharp
  $"if (!await context.{rel.ParentPluralName}.AnyAsync(p => p.Id == parentId, cancellationToken)) return null;"
  // query = context.{ChildPlural}.AsNoTracking().Where(e => e.{Fk} == parentId).OrderBy(e => e.Id)
  ```
- **Create** (parent existence → `null`; build entity from the nested request, set `{Fk} = parentId`, `Add`, `SaveChanges`, return `entity.Id`). Build every non-FK property with `property.ToEntityExpression($"request.{property.PropertyName}")`; set the FK line literally `entity.{rel.ForeignKeyPropertyName} = parentId;`.
- **Read** (`FirstOrDefaultAsync(e => e.Id == id && e.{Fk} == parentId)`; `null` → `null`; else map to `Read{Child}Response`).
- **Update** (`FirstOrDefaultAsync(e => e.Id == id && e.{Fk} == parentId)`; `null` → `false`; assign every non-FK property from `request` via `ToEntityExpression`; `SaveChanges`; `true`).
- **Delete** (`FirstOrDefaultAsync(e => e.Id == id && e.{Fk} == parentId)`; `null` → `WriteResult.NotFound`; for each `rel2` in `model.ChildRelationships` check dependents → `WriteResult.Conflict`; else `Remove` + `SaveChanges` + `WriteResult.Success`).

Reuse the property-mapping snippets already present in `InsertReadMethod`/`InsertCreateMethod`/`InsertUpdateMethod`, skipping the FK property on writes.

- [ ] **Step 4: Run to verify pass, then full suite**

Run: `dotnet test EZRestAPI.Tests --filter ScopedRepositoryTests`
Expected: PASS.
Run: `dotnet test EZRestAPI.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add EZRestAPI/Generators/RepositoryGenerator.cs EZRestAPI.Tests/ScopedRepositoryTests.cs
git commit -m "Generate parent-scoped repository methods for nested routes."
```

---

## Task 7: Nested endpoint route groups

**Files:**
- Modify: `EZRestAPI/Generators/EndpointsGenerator.cs`
- Test: `EZRestAPI.Tests/NestedEndpointTests.cs` (create)

**Interfaces:**
- Produces, inside `Map{Child}Endpoints`, one `RouteGroupBuilder` per `rel` in `model.ParentRelationships` rooted at `/{parentPluralLower}/{{parentId:int}}/{childPluralLower}`, with routes:
  - `GET /` → `List{Child}By{Parent}Async` (paged; `400` on bad paging; `404` when parent missing).
  - `POST /` → `Create{Child}Under{Parent}Async` (`404` parent missing; else `201` with `Read`/`Create` response and `Location` to the flat item `/{childPluralLower}/{id}`).
  - `GET /{id:int}` → `Read{Child}Under{Parent}Async` (`404` when missing/wrong parent).
  - `PUT /{id:int}` → `Update{Child}Under{Parent}Async` (`204`/`404`).
  - `DELETE /{id:int}` → `Delete{Child}Under{Parent}Async` (`204`/`404`/`409`).
- Consumes: scoped repository methods (Task 6).

- [ ] **Step 1: Write the failing test**

Create `EZRestAPI.Tests/NestedEndpointTests.cs`:

```csharp
namespace EZRestAPI.Tests;

public class NestedEndpointTests
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
    public void NestedRouteGroup_IsGenerated()
    {
        var result = GeneratorHarness.Run(Source);
        var endpoints = GeneratorHarness.GetSource(result, "BookEndpoints.g.cs");

        Assert.Contains("app.MapGroup(\"/authors/{parentId:int}/books\")", endpoints);
        Assert.Contains("ListBookByAuthorAsync", endpoints);
        Assert.Contains("CreateBookUnderAuthorAsync", endpoints);
        Assert.Contains("ReadBookUnderAuthorAsync", endpoints);
        Assert.Contains("UpdateBookUnderAuthorAsync", endpoints);
        Assert.Contains("DeleteBookUnderAuthorAsync", endpoints);
        Assert.Contains("Results.Conflict()", endpoints);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test EZRestAPI.Tests --filter NestedEndpointTests`
Expected: FAIL.

- [ ] **Step 3: Emit the nested groups**

In `EndpointsGenerator.Initialize`, after the flat `return app;` is written but **before** closing the method — actually before the `writer.WriteLine("return app;");` line — insert, for each relationship, a nested group. Add a helper:

```csharp
private static void InsertNestedGroup(
    IndentedTextWriter writer,
    ProviderExtensions.Model model,
    ProviderExtensions.RelationshipInfo rel)
{
    var parentRoute = rel.ParentPluralName.ToLowerInvariant();
    var childRoute = rel.ChildPluralName.ToLowerInvariant();
    var name = $"{rel.ChildSingularName}Under{rel.ParentSingularName}";

    writer.WriteLine($"var {rel.ParentSingularName.ToLowerInvariant()}{rel.ChildSingularName}Group = app.MapGroup(\"/{parentRoute}/{{parentId:int}}/{childRoute}\");");
    writer.WriteLine();
    // GET list, POST, GET/{id}, PUT/{id}, DELETE/{id} mapped on that group,
    // calling repository.List{name}Async / Create{name}Async / Read{name}Async /
    // Update{name}Async / Delete{name}Async with (parentId, ...) arguments.
}
```

Implement each verb mirroring the flat endpoints already in this file, but:
- inject `[FromServices] {model.SingularName}Repository repository`,
- add `int parentId` from the route,
- map `null` list/read/create results (parent or item missing) to `Results.NotFound()`,
- POST success → `Results.Created($"/{childRoute}/{{id}}", ...)` using the created id,
- PUT → `updated ? Results.NoContent() : Results.NotFound()`,
- DELETE → `switch` over `WriteResult` to `NoContent`/`Conflict`/`NotFound`,
- give each route a unique `.WithName("...")` incorporating `name` (e.g. `"ListBookUnderAuthor"`).

Wire the helper in `Initialize`:

```csharp
foreach (var rel in model.ParentRelationships)
{
    writer.WriteLine();
    InsertNestedGroup(writer, model, rel);
}
writer.WriteLine();
writer.WriteLine("return app;");
```

For the POST 201 response body, reuse `Create{Child}Response` by constructing it from the nested request plus the new id and `parentId` for the FK field.

- [ ] **Step 4: Run to verify pass, then full suite**

Run: `dotnet test EZRestAPI.Tests --filter NestedEndpointTests`
Expected: PASS.
Run: `dotnet test EZRestAPI.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add EZRestAPI/Generators/EndpointsGenerator.cs EZRestAPI.Tests/NestedEndpointTests.cs
git commit -m "Generate nested route groups for parent-scoped child resources."
```

---

## Task 8: Example `BookModel` and end-to-end integration tests

**Files:**
- Create: `Example/Models/BookModel.cs`
- Create: `Example.Tests/GeneratorTests/RelationshipEndpointTests.cs`
- Modify: `README.md` (document the feature)

**Interfaces:**
- Consumes: the full generated surface for `Author` (already present) + new `Book` child.

- [ ] **Step 1: Add the Example child model**

Create `Example/Models/BookModel.cs`:

```csharp
namespace Example.Models;

using System.ComponentModel.DataAnnotations;

[EZRestAPI.Model("Book", "Books")]
public partial class BookModel
{
    [MaxLength(255)]
    public required string Title { get; set; }

    public required int AuthorId { get; set; }
}
```

- [ ] **Step 2: Write the failing integration test**

Create `Example.Tests/GeneratorTests/RelationshipEndpointTests.cs` following the `EndpointTests` pattern (constructor wiring identical: `[Collection("MsSql")]`, `WebApplicationFactory<Program>`, in-memory connection string, `HttpClient`). Cover:

```csharp
// Sketch of the required [Fact] methods — fill bodies using client.PostAsJsonAsync etc.:
// 1. Author_Book_FlatAndNestedCrud_RoundTrip:
//    - POST /authors -> capture authorId
//    - POST /authors/{authorId}/books {Title="A"} -> 201
//    - GET  /authors/{authorId}/books?page=1&pageSize=20 -> 200, envelope TotalCount>=1, Items contains "A"
//    - GET  /books/{bookId} -> 200 flat
//    - PUT  /authors/{authorId}/books/{bookId} {Title="B"} -> 204; reread shows "B"
//    - DELETE /authors/{authorId}/books/{bookId} -> 204
// 2. NestedItem_WrongParent_Returns404:
//    - Create author1, author2, and a book under author1
//    - GET /authors/{author2}/books/{bookId} -> 404
// 3. NestedPost_MissingParent_Returns404:
//    - POST /authors/424242/books {Title="x"} -> 404
// 4. FlatPost_BadForeignKey_Returns409:
//    - POST /books {Title="x", AuthorId=424242} -> 409
// 5. DeleteAuthor_WithBooks_Returns409:
//    - Create author + book; DELETE /authors/{authorId} -> 409;
//    - GET /authors/{authorId} -> 200 (still present)
// 6. Pagination_BadPage_Returns400:
//    - GET /books?page=0&pageSize=20 -> 400
```

Each body uses `Assert.Equal(HttpStatusCode.X, response.StatusCode)` and `ReadFromJsonAsync` for payloads, exactly as `EndpointTests` does. For the paged envelope, deserialize into a small local record `record Page(List<ReadBookResponse> Items, int TotalCount, int Page, int PageSize);` or read the raw JSON and assert on `TotalCount`.

- [ ] **Step 3: Run to verify failure (before the generator changes are consumed by Example)**

Run: `dotnet test Example.Tests --filter RelationshipEndpointTests`
Expected: FAIL (routes 404 / missing) if run before Tasks 1–7 are merged; PASS after. If Tasks 1–7 are already merged, the test should compile and pass once `BookModel` exists.

- [ ] **Step 4: Run the full integration suite**

Run: `dotnet test Example.Tests`
Expected: PASS (existing SimpleData/Post tests + new relationship tests). Requires Docker.

- [ ] **Step 5: Document in README**

Add a "Relationships between models" section to `README.md` after "Nested models", explaining: `{Singular}Id` convention, `int`/`int?` requirement, `[Scalar]` opt-out, the generated flat + nested routes table, pagination envelope, and the 404/409 semantics. Mirror the existing table style.

- [ ] **Step 6: Commit**

```bash
git add Example/Models/BookModel.cs Example.Tests/GeneratorTests/RelationshipEndpointTests.cs README.md
git commit -m "Add Author/Book relationship sample and end-to-end relationship tests."
```

---

## Self-Review

**Spec coverage:**
- §2 convention detection → Task 1 (resolution) + Task 2 (`EZR011`). `int`-type + existing-model gate → Task 1 Steps 4, plus tests in Tasks 1–2. `[Scalar]` opt-out → Task 1. `OrderId` guard → Tasks 1 & 2 tests.
- §3 routes (flat + nested, all verbs) → Task 3 (flat list), existing generator (flat item/create), Task 7 (nested group).
- §4 pagination (envelope, caps, ordering, `page<1`→400) → Task 3 + Task 8 test 6.
- §5 status codes: scoped 404 → Tasks 6/7 + Task 8 test 2; nested POST missing parent 404 → Tasks 6/7 + test 3; flat bad-FK 409 → Task 4 + test 4; parent-delete 409 RESTRICT → Task 4 + test 5; DB FK constraint → Task 1.
- §6 architecture (RelationshipInfo, per-generator changes, Example BookModel) → Tasks 1–8.
- §7 testing (fast + integration) → every task's tests + Task 8.
- §8 orchestration → executed via the Workflow after this plan.

**Placeholder scan:** No "TBD"/"handle edge cases". Task 6/7 use prose to describe method bodies that mirror already-shown snippets (Read/Create/Update) rather than repeating ~200 lines verbatim; the exact signatures, filters (`e.{Fk} == parentId`), and result mappings are given. Acceptable because the referenced patterns are fully shown in Tasks 1/3/4.

**Type consistency:** `WriteResult { Success, NotFound, Conflict }` used identically in Tasks 4, 6, 7. `PagedResponse<T>` fields (`Items/TotalCount/Page/PageSize`) consistent across Tasks 3, 6, 8. Method names `Create{Child}Under{Parent}Async` / `List{Child}By{Parent}Async` / `Read|Update|Delete{Child}Under{Parent}Async` consistent across Tasks 6 and 7. Nested DTO names `Create/Update{Child}Under{Parent}Request` consistent across Tasks 5, 6, 7. `RelationshipInfo` field names consistent across Tasks 1, 4, 5, 6, 7.
