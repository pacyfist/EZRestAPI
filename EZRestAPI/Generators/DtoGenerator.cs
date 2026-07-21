namespace EZRestAPI.Generators;

using System.Text;
using EZRestAPI.Providers;
using EZRestAPI.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

[Generator(LanguageNames.CSharp)]
public class DtoGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModels();

        RegisterDto(context, modelsProvider, "Create{0}Request", idLine: null);
        RegisterDto(context, modelsProvider, "Create{0}Response", "public int Id { get; set; }");
        RegisterDto(context, modelsProvider, "Read{0}Response", "public int? Id { get; set; }");
        RegisterDto(context, modelsProvider, "Update{0}Request", "public int Id { get; set; }");

        var relModelsProvider = context.SyntaxProvider.GetModelsWithRelationships();

        context.RegisterSourceOutput(
            relModelsProvider,
            (ctx, model) =>
            {
                foreach (var rel in model.ParentRelationships)
                {
                    EmitNestedDto(
                        ctx,
                        model,
                        rel,
                        $"Create{rel.ChildSingularName}Under{rel.ParentSingularName}Request"
                    );
                    EmitNestedDto(
                        ctx,
                        model,
                        rel,
                        $"Update{rel.ChildSingularName}Under{rel.ParentSingularName}Request"
                    );
                }
            }
        );

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
                    SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
                );

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

                ctx.AddSource(
                    "WriteResult.g.cs",
                    SourceText.From(w.InnerWriter.ToString(), Encoding.UTF8)
                );
            }
        );
    }

    private static void EmitNestedDto(
        SourceProductionContext ctx,
        ProviderExtensions.Model model,
        ProviderExtensions.RelationshipInfo rel,
        string className
    )
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
                $"public {(property.NeedsRequiredModifier ? "required " : "")}{property.DtoTypeName} {property.PropertyName} {{ get; set; }}"
            );
        }
        writer.Indent--;
        writer.WriteLine("}");

        ctx.AddSource(
            $"{className}.g.cs",
            SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
        );
    }

    private static void RegisterDto(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<ProviderExtensions.Model> modelsProvider,
        string classNameFormat,
        string? idLine
    )
    {
        context.RegisterSourceOutput(
            modelsProvider,
            (ctx, model) =>
            {
                var className = string.Format(classNameFormat, model.SingularName);

                var writer = SourceWriter.Create();

                writer.WriteLine($"namespace {model.AssemblyName};");
                writer.WriteLine();
                writer.WriteLine($"public class {className}");
                writer.WriteLine("{");
                writer.Indent++;
                if (idLine is not null)
                {
                    writer.WriteLine(idLine);
                }
                foreach (var property in model.Properties)
                {
                    writer.WriteLine(
                        $"public {(property.NeedsRequiredModifier ? "required " : "")}{property.DtoTypeName} {property.PropertyName} {{ get; set; }}"
                    );
                }
                writer.Indent--;
                writer.WriteLine("}");

                ctx.AddSource(
                    $"{className}.g.cs",
                    SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
                );
            }
        );
    }
}
