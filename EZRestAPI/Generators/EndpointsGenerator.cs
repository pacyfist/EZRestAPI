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
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("var id = await repository.CreateAsync(");
        writer.Indent++;
        foreach (var property in model.Properties)
        {
            writer.WriteLine(
                $"{property.PropertyName.ToCamelCase()}: request.{property.PropertyName},"
            );
        }
        writer.WriteLine("cancellationToken: cancellationToken);");
        writer.Indent--;
        writer.WriteLine();
        writer.WriteLine($"var response = new Create{model.SingularName}Response()");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("Id = id,");
        foreach (var property in model.Properties)
        {
            writer.WriteLine($"{property.PropertyName} = request.{property.PropertyName},");
        }
        writer.Indent--;
        writer.WriteLine("};");
        writer.WriteLine();
        writer.WriteLine($"return Results.Created($\"/{Route(model)}/{{id}}\", response);");
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
        writer.WriteLine("var entity = await repository.ReadAsync(id, cancellationToken);");
        writer.WriteLine();
        writer.WriteLine("if (entity is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return Results.NotFound();");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"var response = new Read{model.SingularName}Response()");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("Id = id,");
        foreach (var property in model.Properties)
        {
            writer.WriteLine($"{property.PropertyName} = entity.Value.{property.PropertyName},");
        }
        writer.Indent--;
        writer.WriteLine("};");
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
        writer.WriteLine("var updated = await repository.UpdateAsync(");
        writer.Indent++;
        writer.WriteLine("id: id,");
        foreach (var property in model.Properties)
        {
            writer.WriteLine(
                $"{property.PropertyName.ToCamelCase()}: request.{property.PropertyName},"
            );
        }
        writer.WriteLine("cancellationToken: cancellationToken);");
        writer.Indent--;
        writer.WriteLine();
        writer.WriteLine("return updated ? Results.NoContent() : Results.NotFound();");
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
        writer.WriteLine("var deleted = await repository.DeleteAsync(id, cancellationToken);");
        writer.WriteLine();
        writer.WriteLine("return deleted ? Results.NoContent() : Results.NotFound();");
        writer.Indent--;
        writer.WriteLine($"}}).WithName(\"Delete{model.SingularName}\");");
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModels();

        context.RegisterSourceOutput(
            modelsProvider,
            (ctx, model) =>
            {
                var writer = SourceWriter.Create();

                writer.WriteLine($"namespace {model.AssemblyName};");
                writer.WriteLine();
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
                InsertCreateEndpoint(writer, model);
                writer.WriteLine();
                InsertReadEndpoint(writer, model);
                writer.WriteLine();
                InsertUpdateEndpoint(writer, model);
                writer.WriteLine();
                InsertDeleteEndpoint(writer, model);
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
