namespace EZRestAPI.Generators;

using System.CodeDom.Compiler;
using System.Text;
using EZRestAPI.Providers;
using EZRestAPI.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

[Generator(LanguageNames.CSharp)]
public class RepositoryGenerator : IIncrementalGenerator
{
    private static void InsertCreateMethod(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
    {
        var hasParents = model.ParentRelationships.Any();

        writer.WriteLine(
            hasParents
                ? $"public async Task<int?> CreateAsync(Create{model.SingularName}Request request, CancellationToken cancellationToken)"
                : $"public async Task<int> CreateAsync(Create{model.SingularName}Request request, CancellationToken cancellationToken)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        foreach (var rel in model.ParentRelationships)
        {
            writer.WriteLine(
                rel.IsNullable
                    ? $"if (request.{rel.ForeignKeyPropertyName} is not null && !await context.{rel.ParentPluralName}.AnyAsync(p => p.Id == request.{rel.ForeignKeyPropertyName}, cancellationToken))"
                    : $"if (!await context.{rel.ParentPluralName}.AnyAsync(p => p.Id == request.{rel.ForeignKeyPropertyName}, cancellationToken))"
            );
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine("return null;");
            writer.Indent--;
            writer.WriteLine("}");
            writer.WriteLine();
        }
        writer.WriteLine($"var entity = new {model.ClassName}()");
        writer.WriteLine("{");
        writer.Indent++;
        foreach (var property in model.Properties)
        {
            writer.WriteLine(
                $"{property.PropertyName} = {property.ToEntityExpression($"request.{property.PropertyName}")},"
            );
        }
        writer.Indent--;
        writer.WriteLine("};");
        writer.WriteLine();
        writer.WriteLine($"context.{model.PluralName}.Add(entity);");
        writer.WriteLine($"await context.SaveChangesAsync(cancellationToken);");
        writer.WriteLine();
        writer.WriteLine($"return entity.Id;");
        writer.Indent--;
        writer.WriteLine("}");
    }

    private static void InsertReadMethod(IndentedTextWriter writer, ProviderExtensions.Model model)
    {
        writer.WriteLine(
            $"public async Task<Read{model.SingularName}Response?> ReadAsync(int id, CancellationToken cancellationToken)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"var entity = await context.{model.PluralName}.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine("if (entity is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return null;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"return new Read{model.SingularName}Response()");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("Id = entity.Id,");
        foreach (var property in model.Properties)
        {
            writer.WriteLine(
                $"{property.PropertyName} = {property.ToDtoExpression($"entity.{property.PropertyName}")},"
            );
        }
        writer.Indent--;
        writer.WriteLine("};");
        writer.Indent--;
        writer.WriteLine("}");
    }

    private static void InsertListMethod(IndentedTextWriter writer, ProviderExtensions.Model model)
    {
        writer.WriteLine(
            $"public async Task<PagedResponse<Read{model.SingularName}Response>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"var query = context.{model.PluralName}.AsNoTracking().OrderBy(e => e.Id);"
        );
        writer.WriteLine("var totalCount = await query.CountAsync(cancellationToken);");
        writer.WriteLine("var entities = await query");
        writer.Indent++;
        writer.WriteLine(".Skip((page - 1) * pageSize)");
        writer.WriteLine(".Take(pageSize)");
        writer.WriteLine(".ToListAsync(cancellationToken);");
        writer.Indent--;
        writer.WriteLine();
        writer.WriteLine(
            $"var items = new System.Collections.Generic.List<Read{model.SingularName}Response>();"
        );
        writer.WriteLine("foreach (var entity in entities)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine($"items.Add(new Read{model.SingularName}Response()");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("Id = entity.Id,");
        foreach (var property in model.Properties)
        {
            writer.WriteLine(
                $"{property.PropertyName} = {property.ToDtoExpression($"entity.{property.PropertyName}")},"
            );
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

    private static void InsertUpdateMethod(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
    {
        var hasParents = model.ParentRelationships.Any();

        writer.WriteLine(
            hasParents
                ? $"public async Task<WriteResult> UpdateAsync(int id, Update{model.SingularName}Request request, CancellationToken cancellationToken)"
                : $"public async Task<bool> UpdateAsync(int id, Update{model.SingularName}Request request, CancellationToken cancellationToken)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"var entity = await context.{model.PluralName}.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine("if (entity is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(hasParents ? "return WriteResult.NotFound;" : "return false;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        foreach (var rel in model.ParentRelationships)
        {
            writer.WriteLine(
                rel.IsNullable
                    ? $"if (request.{rel.ForeignKeyPropertyName} is not null && !await context.{rel.ParentPluralName}.AnyAsync(p => p.Id == request.{rel.ForeignKeyPropertyName}, cancellationToken))"
                    : $"if (!await context.{rel.ParentPluralName}.AnyAsync(p => p.Id == request.{rel.ForeignKeyPropertyName}, cancellationToken))"
            );
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine("return WriteResult.Conflict;");
            writer.Indent--;
            writer.WriteLine("}");
            writer.WriteLine();
        }
        foreach (var property in model.Properties)
        {
            writer.WriteLine(
                $"entity.{property.PropertyName} = {property.ToEntityExpression($"request.{property.PropertyName}")};"
            );
        }
        writer.WriteLine();
        writer.WriteLine($"await context.SaveChangesAsync(cancellationToken);");
        writer.WriteLine();
        writer.WriteLine(hasParents ? "return WriteResult.Success;" : "return true;");
        writer.Indent--;
        writer.WriteLine("}");
    }

    private static void InsertDeleteMethod(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
    {
        if (!model.ChildRelationships.Any())
        {
            writer.WriteLine(
                $"public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)"
            );
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine(
                $"return await context.{model.PluralName}.Where(e => e.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;"
            );
            writer.Indent--;
            writer.WriteLine("}");
            return;
        }

        writer.WriteLine(
            $"public async Task<WriteResult> DeleteAsync(int id, CancellationToken cancellationToken)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"var entity = await context.{model.PluralName}.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine("if (entity is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return WriteResult.NotFound;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        foreach (var rel in model.ChildRelationships)
        {
            writer.WriteLine(
                $"if (await context.{rel.ChildPluralName}.AnyAsync(c => c.{rel.ForeignKeyPropertyName} == id, cancellationToken))"
            );
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
    }

    private static void InsertScopedMethods(
        IndentedTextWriter writer,
        ProviderExtensions.Model model,
        ProviderExtensions.RelationshipInfo rel
    )
    {
        var childResponse = $"Read{model.SingularName}Response";
        var createRequest = $"Create{rel.ChildSingularName}Under{rel.ParentSingularName}Request";
        var updateRequest = $"Update{rel.ChildSingularName}Under{rel.ParentSingularName}Request";

        // List{Child}By{Parent}Async
        writer.WriteLine(
            $"public async Task<PagedResponse<{childResponse}>?> List{rel.ChildSingularName}By{rel.ParentSingularName}Async(int parentId, int page, int pageSize, CancellationToken cancellationToken)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"if (!await context.{rel.ParentPluralName}.AnyAsync(p => p.Id == parentId, cancellationToken))"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return null;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine(
            $"var query = context.{model.PluralName}.AsNoTracking().Where(e => e.{rel.ForeignKeyPropertyName} == parentId).OrderBy(e => e.Id);"
        );
        writer.WriteLine("var totalCount = await query.CountAsync(cancellationToken);");
        writer.WriteLine("var entities = await query");
        writer.Indent++;
        writer.WriteLine(".Skip((page - 1) * pageSize)");
        writer.WriteLine(".Take(pageSize)");
        writer.WriteLine(".ToListAsync(cancellationToken);");
        writer.Indent--;
        writer.WriteLine();
        writer.WriteLine($"var items = new System.Collections.Generic.List<{childResponse}>();");
        writer.WriteLine("foreach (var entity in entities)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine($"items.Add(new {childResponse}()");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("Id = entity.Id,");
        foreach (var property in model.Properties)
        {
            writer.WriteLine(
                $"{property.PropertyName} = {property.ToDtoExpression($"entity.{property.PropertyName}")},"
            );
        }
        writer.Indent--;
        writer.WriteLine("});");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"return new PagedResponse<{childResponse}>()");
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
        writer.WriteLine();

        // Create{Child}Under{Parent}Async
        writer.WriteLine(
            $"public async Task<int?> Create{rel.ChildSingularName}Under{rel.ParentSingularName}Async(int parentId, {createRequest} request, CancellationToken cancellationToken)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"if (!await context.{rel.ParentPluralName}.AnyAsync(p => p.Id == parentId, cancellationToken))"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return null;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"var entity = new {model.ClassName}()");
        writer.WriteLine("{");
        writer.Indent++;
        foreach (var property in model.Properties)
        {
            if (property.PropertyName == rel.ForeignKeyPropertyName)
            {
                writer.WriteLine($"{property.PropertyName} = parentId,");
                continue;
            }
            writer.WriteLine(
                $"{property.PropertyName} = {property.ToEntityExpression($"request.{property.PropertyName}")},"
            );
        }
        writer.Indent--;
        writer.WriteLine("};");
        writer.WriteLine();
        writer.WriteLine($"context.{model.PluralName}.Add(entity);");
        writer.WriteLine("await context.SaveChangesAsync(cancellationToken);");
        writer.WriteLine();
        writer.WriteLine("return entity.Id;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Read{Child}Under{Parent}Async
        writer.WriteLine(
            $"public async Task<{childResponse}?> Read{rel.ChildSingularName}Under{rel.ParentSingularName}Async(int parentId, int id, CancellationToken cancellationToken)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"var entity = await context.{model.PluralName}.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id && e.{rel.ForeignKeyPropertyName} == parentId, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine("if (entity is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return null;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"return new {childResponse}()");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("Id = entity.Id,");
        foreach (var property in model.Properties)
        {
            writer.WriteLine(
                $"{property.PropertyName} = {property.ToDtoExpression($"entity.{property.PropertyName}")},"
            );
        }
        writer.Indent--;
        writer.WriteLine("};");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Update{Child}Under{Parent}Async
        writer.WriteLine(
            $"public async Task<bool> Update{rel.ChildSingularName}Under{rel.ParentSingularName}Async(int parentId, int id, {updateRequest} request, CancellationToken cancellationToken)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"var entity = await context.{model.PluralName}.FirstOrDefaultAsync(e => e.Id == id && e.{rel.ForeignKeyPropertyName} == parentId, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine("if (entity is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return false;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        foreach (var property in model.Properties)
        {
            if (property.PropertyName == rel.ForeignKeyPropertyName)
            {
                continue;
            }
            writer.WriteLine(
                $"entity.{property.PropertyName} = {property.ToEntityExpression($"request.{property.PropertyName}")};"
            );
        }
        writer.WriteLine();
        writer.WriteLine("await context.SaveChangesAsync(cancellationToken);");
        writer.WriteLine();
        writer.WriteLine("return true;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Delete{Child}Under{Parent}Async
        writer.WriteLine(
            $"public async Task<WriteResult> Delete{rel.ChildSingularName}Under{rel.ParentSingularName}Async(int parentId, int id, CancellationToken cancellationToken)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"var entity = await context.{model.PluralName}.FirstOrDefaultAsync(e => e.Id == id && e.{rel.ForeignKeyPropertyName} == parentId, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine("if (entity is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return WriteResult.NotFound;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        foreach (var rel2 in model.ChildRelationships)
        {
            writer.WriteLine(
                $"if (await context.{rel2.ChildPluralName}.AnyAsync(c => c.{rel2.ForeignKeyPropertyName} == id, cancellationToken))"
            );
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
    }

    // ---- Aggregate repository (reads + delete; T2 scope) ------------------

    private static void InsertAggregateReadMethod(
        IndentedTextWriter writer,
        ProviderExtensions.Aggregate aggregate
    )
    {
        writer.WriteLine(
            $"public async Task<Read{aggregate.SingularName}Response?> ReadAsync(int id, CancellationToken cancellationToken)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"var entity = await context.{aggregate.PluralName}.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine("if (entity is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return null;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"return new Read{aggregate.SingularName}Response()");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("Id = entity.Id,");
        foreach (var property in aggregate.Properties)
        {
            writer.WriteLine(
                $"{property.PropertyName} = {property.ToDtoExpression($"entity.{property.PropertyName}")},"
            );
        }
        writer.Indent--;
        writer.WriteLine("};");
        writer.Indent--;
        writer.WriteLine("}");
    }

    private static void InsertAggregateListMethod(
        IndentedTextWriter writer,
        ProviderExtensions.Aggregate aggregate
    )
    {
        writer.WriteLine(
            $"public async Task<PagedResponse<Read{aggregate.SingularName}Response>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"var query = context.{aggregate.PluralName}.AsNoTracking().OrderBy(e => e.Id);"
        );
        writer.WriteLine("var totalCount = await query.CountAsync(cancellationToken);");
        writer.WriteLine("var entities = await query");
        writer.Indent++;
        writer.WriteLine(".Skip((page - 1) * pageSize)");
        writer.WriteLine(".Take(pageSize)");
        writer.WriteLine(".ToListAsync(cancellationToken);");
        writer.Indent--;
        writer.WriteLine();
        writer.WriteLine(
            $"var items = new System.Collections.Generic.List<Read{aggregate.SingularName}Response>();"
        );
        writer.WriteLine("foreach (var entity in entities)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine($"items.Add(new Read{aggregate.SingularName}Response()");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("Id = entity.Id,");
        foreach (var property in aggregate.Properties)
        {
            writer.WriteLine(
                $"{property.PropertyName} = {property.ToDtoExpression($"entity.{property.PropertyName}")},"
            );
        }
        writer.Indent--;
        writer.WriteLine("});");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"return new PagedResponse<Read{aggregate.SingularName}Response>()");
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

    private static void InsertAggregateDeleteMethod(
        IndentedTextWriter writer,
        ProviderExtensions.Aggregate aggregate
    )
    {
        // Load + Remove + SaveChanges (not ExecuteDelete) so EF cascades the
        // aggregate's owned value objects / child entities in the same unit.
        writer.WriteLine(
            "public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"var entity = await context.{aggregate.PluralName}.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine("if (entity is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return false;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"context.{aggregate.PluralName}.Remove(entity);");
        writer.WriteLine("await context.SaveChangesAsync(cancellationToken);");
        writer.WriteLine("return true;");
        writer.Indent--;
        writer.WriteLine("}");
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModelsWithRelationships();

        context.RegisterSourceOutput(
            modelsProvider,
            (ctx, model) =>
            {
                var writer = SourceWriter.Create();

                writer.WriteLine($"namespace {model.AssemblyName};");
                writer.WriteLine();
                writer.WriteLine("using System.Linq;");
                writer.WriteLine("using System.Threading;");
                writer.WriteLine("using System.Threading.Tasks;");
                writer.WriteLine("using Microsoft.EntityFrameworkCore;");
                writer.WriteLine();
                writer.WriteLine(
                    $"public partial class {model.SingularName}Repository(CustomDbContext context)"
                );
                writer.WriteLine("{");
                writer.Indent++;
                InsertCreateMethod(writer, model);
                writer.WriteLine();
                InsertReadMethod(writer, model);
                writer.WriteLine();
                InsertListMethod(writer, model);
                writer.WriteLine();
                InsertUpdateMethod(writer, model);
                writer.WriteLine();
                InsertDeleteMethod(writer, model);
                foreach (var rel in model.ParentRelationships)
                {
                    writer.WriteLine();
                    InsertScopedMethods(writer, model, rel);
                }
                writer.Indent--;
                writer.WriteLine("}");

                ctx.AddSource(
                    $"{model.SingularName}Repository.g.cs",
                    SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
                );
            }
        );

        var aggregatesProvider = context.SyntaxProvider.GetAggregates();

        context.RegisterSourceOutput(
            aggregatesProvider,
            (ctx, aggregate) =>
            {
                var writer = SourceWriter.Create();

                writer.WriteLine($"namespace {aggregate.AssemblyName};");
                writer.WriteLine();
                writer.WriteLine("using System.Linq;");
                writer.WriteLine("using System.Threading;");
                writer.WriteLine("using System.Threading.Tasks;");
                writer.WriteLine("using Microsoft.EntityFrameworkCore;");
                writer.WriteLine();
                writer.WriteLine(
                    $"public partial class {aggregate.SingularName}Repository(CustomDbContext context)"
                );
                writer.WriteLine("{");
                writer.Indent++;
                InsertAggregateReadMethod(writer, aggregate);
                writer.WriteLine();
                InsertAggregateListMethod(writer, aggregate);
                writer.WriteLine();
                InsertAggregateDeleteMethod(writer, aggregate);
                writer.Indent--;
                writer.WriteLine("}");

                ctx.AddSource(
                    $"{aggregate.SingularName}Repository.g.cs",
                    SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
                );
            }
        );
    }
}
