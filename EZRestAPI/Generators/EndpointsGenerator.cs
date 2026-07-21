namespace EZRestAPI.Generators;

using System.CodeDom.Compiler;
using System.Text;
using EZRestAPI.Providers;
using EZRestAPI.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

[Generator(LanguageNames.CSharp)]
public class EndpointsGenerator : IIncrementalGenerator
{
    private static string Route(ProviderExtensions.Model model) =>
        model.PluralName.ToLowerInvariant();

    private static void InsertListEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
    {
        writer.WriteLine("group.MapGet(\"/\", async (");
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine("[FromQuery] int page = 1,");
        writer.WriteLine("[FromQuery] int pageSize = 20,");
        writer.WriteLine("CancellationToken cancellationToken = default) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("if (page < 1 || pageSize < 1)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return Results.BadRequest(\"page and pageSize must be >= 1.\");");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine("pageSize = System.Math.Min(pageSize, 100);");
        writer.WriteLine();
        writer.WriteLine(
            "var response = await repository.ListAsync(page, pageSize, cancellationToken);"
        );
        writer.WriteLine("return Results.Ok(response);");
        writer.Indent--;
        writer.WriteLine($"}}).WithName(\"List{model.PluralName}\");");
    }

    private static void InsertCreateEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
    {
        writer.WriteLine("group.MapPost(\"/\", async (");
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine($"[FromBody] Create{model.SingularName}Request request,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        var hasParents = model.ParentRelationships.Any();

        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("var id = await repository.CreateAsync(request, cancellationToken);");
        writer.WriteLine();
        if (hasParents)
        {
            writer.WriteLine("if (id is null)");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine("return Results.Conflict();");
            writer.Indent--;
            writer.WriteLine("}");
            writer.WriteLine();
        }
        writer.WriteLine($"var response = new Create{model.SingularName}Response()");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(hasParents ? "Id = id.Value," : "Id = id,");
        foreach (var property in model.Properties)
        {
            writer.WriteLine($"{property.PropertyName} = request.{property.PropertyName},");
        }
        writer.Indent--;
        writer.WriteLine("};");
        writer.WriteLine();
        writer.WriteLine(
            hasParents
                ? $"return Results.Created($\"/{Route(model)}/{{id.Value}}\", response);"
                : $"return Results.Created($\"/{Route(model)}/{{id}}\", response);"
        );
        writer.Indent--;
        writer.WriteLine($"}}).WithName(\"Create{model.SingularName}\");");
    }

    private static void InsertReadEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
    {
        writer.WriteLine("group.MapGet(\"/{id:int}\", async (");
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine("int id,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("var response = await repository.ReadAsync(id, cancellationToken);");
        writer.WriteLine();
        writer.WriteLine("if (response is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return Results.NotFound();");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("return Results.Ok(response);");
        writer.Indent--;
        writer.WriteLine($"}}).WithName(\"Read{model.SingularName}\");");
    }

    private static void InsertUpdateEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
    {
        writer.WriteLine("group.MapPut(\"/{id:int}\", async (");
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine("int id,");
        writer.WriteLine($"[FromBody] Update{model.SingularName}Request request,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        if (model.ParentRelationships.Any())
        {
            writer.WriteLine(
                "var outcome = await repository.UpdateAsync(id, request, cancellationToken);"
            );
            writer.WriteLine();
            writer.WriteLine("return outcome switch");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine("WriteResult.Success => Results.NoContent(),");
            writer.WriteLine("WriteResult.Conflict => Results.Conflict(),");
            writer.WriteLine("_ => Results.NotFound(),");
            writer.Indent--;
            writer.WriteLine("};");
        }
        else
        {
            writer.WriteLine(
                "var updated = await repository.UpdateAsync(id, request, cancellationToken);"
            );
            writer.WriteLine();
            writer.WriteLine("return updated ? Results.NoContent() : Results.NotFound();");
        }
        writer.Indent--;
        writer.WriteLine($"}}).WithName(\"Update{model.SingularName}\");");
    }

    private static void InsertDeleteEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
    {
        writer.WriteLine("group.MapDelete(\"/{id:int}\", async (");
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine("int id,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        if (model.ChildRelationships.Any())
        {
            writer.WriteLine("var outcome = await repository.DeleteAsync(id, cancellationToken);");
            writer.WriteLine();
            writer.WriteLine("return outcome switch");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine("WriteResult.Success => Results.NoContent(),");
            writer.WriteLine("WriteResult.Conflict => Results.Conflict(),");
            writer.WriteLine("_ => Results.NotFound(),");
            writer.Indent--;
            writer.WriteLine("};");
        }
        else
        {
            writer.WriteLine("var deleted = await repository.DeleteAsync(id, cancellationToken);");
            writer.WriteLine();
            writer.WriteLine("return deleted ? Results.NoContent() : Results.NotFound();");
        }
        writer.Indent--;
        writer.WriteLine($"}}).WithName(\"Delete{model.SingularName}\");");
    }

    private static void InsertNestedGroup(
        IndentedTextWriter writer,
        ProviderExtensions.Model model,
        ProviderExtensions.RelationshipInfo rel
    )
    {
        var parentRoute = rel.ParentPluralName.ToLowerInvariant();
        var childRoute = rel.ChildPluralName.ToLowerInvariant();
        var name = $"{rel.ChildSingularName}Under{rel.ParentSingularName}";
        var groupVar =
            $"{rel.ParentSingularName.ToLowerInvariant()}{rel.ChildSingularName}Group";
        var createRequest = $"Create{name}Request";
        var updateRequest = $"Update{name}Request";

        writer.WriteLine(
            $"var {groupVar} = app.MapGroup(\"/{parentRoute}/{{parentId:int}}/{childRoute}\");"
        );
        writer.WriteLine();

        // GET / -> paged list scoped to the parent.
        writer.WriteLine($"{groupVar}.MapGet(\"/\", async (");
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine("int parentId,");
        writer.WriteLine("[FromQuery] int page = 1,");
        writer.WriteLine("[FromQuery] int pageSize = 20,");
        writer.WriteLine("CancellationToken cancellationToken = default) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("if (page < 1 || pageSize < 1)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return Results.BadRequest(\"page and pageSize must be >= 1.\");");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine("pageSize = System.Math.Min(pageSize, 100);");
        writer.WriteLine();
        writer.WriteLine(
            $"var response = await repository.List{rel.ChildSingularName}By{rel.ParentSingularName}Async(parentId, page, pageSize, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine("if (response is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return Results.NotFound();");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("return Results.Ok(response);");
        writer.Indent--;
        writer.WriteLine($"}}).WithName(\"List{name}\");");
        writer.WriteLine();

        // POST / -> create under the parent (FK from route).
        writer.WriteLine($"{groupVar}.MapPost(\"/\", async (");
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine("int parentId,");
        writer.WriteLine($"[FromBody] {createRequest} request,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"var id = await repository.Create{name}Async(parentId, request, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine("if (id is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return Results.NotFound();");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"var response = new Create{model.SingularName}Response()");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("Id = id.Value,");
        foreach (var property in model.Properties)
        {
            writer.WriteLine(
                property.PropertyName == rel.ForeignKeyPropertyName
                    ? $"{property.PropertyName} = parentId,"
                    : $"{property.PropertyName} = request.{property.PropertyName},"
            );
        }
        writer.Indent--;
        writer.WriteLine("};");
        writer.WriteLine();
        writer.WriteLine($"return Results.Created($\"/{childRoute}/{{id.Value}}\", response);");
        writer.Indent--;
        writer.WriteLine($"}}).WithName(\"Create{name}\");");
        writer.WriteLine();

        // GET /{id:int} -> scoped read.
        writer.WriteLine($"{groupVar}.MapGet(\"/{{id:int}}\", async (");
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine("int parentId,");
        writer.WriteLine("int id,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"var response = await repository.Read{name}Async(parentId, id, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine("if (response is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return Results.NotFound();");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("return Results.Ok(response);");
        writer.Indent--;
        writer.WriteLine($"}}).WithName(\"Read{name}\");");
        writer.WriteLine();

        // PUT /{id:int} -> scoped update.
        writer.WriteLine($"{groupVar}.MapPut(\"/{{id:int}}\", async (");
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine("int parentId,");
        writer.WriteLine("int id,");
        writer.WriteLine($"[FromBody] {updateRequest} request,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"var updated = await repository.Update{name}Async(parentId, id, request, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine("return updated ? Results.NoContent() : Results.NotFound();");
        writer.Indent--;
        writer.WriteLine($"}}).WithName(\"Update{name}\");");
        writer.WriteLine();

        // DELETE /{id:int} -> scoped delete.
        writer.WriteLine($"{groupVar}.MapDelete(\"/{{id:int}}\", async (");
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine("int parentId,");
        writer.WriteLine("int id,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"var outcome = await repository.Delete{name}Async(parentId, id, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine("return outcome switch");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("WriteResult.Success => Results.NoContent(),");
        writer.WriteLine("WriteResult.Conflict => Results.Conflict(),");
        writer.WriteLine("_ => Results.NotFound(),");
        writer.Indent--;
        writer.WriteLine("};");
        writer.Indent--;
        writer.WriteLine($"}}).WithName(\"Delete{name}\");");
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
                writer.WriteLine("using System.Threading;");
                writer.WriteLine("using Microsoft.AspNetCore.Builder;");
                writer.WriteLine("using Microsoft.AspNetCore.Http;");
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
                writer.WriteLine();
                InsertListEndpoint(writer, model);
                writer.WriteLine();
                InsertCreateEndpoint(writer, model);
                writer.WriteLine();
                InsertReadEndpoint(writer, model);
                writer.WriteLine();
                InsertUpdateEndpoint(writer, model);
                writer.WriteLine();
                InsertDeleteEndpoint(writer, model);
                foreach (var rel in model.ParentRelationships)
                {
                    writer.WriteLine();
                    InsertNestedGroup(writer, model, rel);
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
        );
    }
}
