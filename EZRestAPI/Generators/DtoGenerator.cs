namespace EZRestAPI.Generators;

using System.CodeDom.Compiler;
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

        RegisterDto(
            context,
            modelsProvider,
            "Create{0}Request",
            idLine: null,
            emitValidation: true
        );
        RegisterDto(
            context,
            modelsProvider,
            "Create{0}Response",
            "public int Id { get; set; }",
            emitValidation: false
        );
        RegisterDto(
            context,
            modelsProvider,
            "Read{0}Response",
            "public int? Id { get; set; }",
            emitValidation: false
        );
        RegisterDto(
            context,
            modelsProvider,
            "Update{0}Request",
            "public int Id { get; set; }",
            emitValidation: true
        );

        // Aggregates get a Read response (reused as the paginated list item),
        // built from the read-only property rule resolved by the provider. No
        // Create/Update request DTO is emitted here — those belong to T3/T4.
        var aggregatesProvider = context.SyntaxProvider.GetAggregates();

        context.RegisterSourceOutput(
            aggregatesProvider,
            (ctx, aggregate) =>
            {
                var className = $"Read{aggregate.SingularName}Response";

                var writer = SourceWriter.Create();

                writer.WriteLine($"namespace {aggregate.AssemblyName};");
                writer.WriteLine();
                writer.WriteLine($"public class {className}");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine("public int? Id { get; set; }");
                foreach (var property in aggregate.Properties)
                {
                    WriteDtoProperty(writer, property, emitValidation: false);
                }
                writer.Indent--;
                writer.WriteLine("}");

                ctx.AddSource(
                    $"{className}.g.cs",
                    SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
                );
            }
        );

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

                EmitValidationHelper(ctx, models[0].AssemblyName);
                EmitProblemsHelper(ctx, models[0].AssemblyName);
            }
        );
    }

    /// <summary>
    /// Emits the reflection-based DataAnnotations validator (once per assembly).
    /// Returns the RFC 9457 <c>errors</c> field-map, or null when the request is
    /// valid, so POST/PUT handlers can turn a failure into a 422 ProblemDetails.
    /// </summary>
    private static void EmitValidationHelper(SourceProductionContext ctx, string assemblyName)
    {
        var writer = SourceWriter.Create();

        writer.WriteLine($"namespace {assemblyName};");
        writer.WriteLine();
        writer.WriteLine("using System.Linq;");
        writer.WriteLine();
        writer.WriteLine("public static class EZRestAPIValidation");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            "public static System.Collections.Generic.IDictionary<string, string[]>? Validate(object request)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            "var context = new System.ComponentModel.DataAnnotations.ValidationContext(request);"
        );
        writer.WriteLine(
            "var results = new System.Collections.Generic.List<System.ComponentModel.DataAnnotations.ValidationResult>();"
        );
        writer.WriteLine(
            "if (System.ComponentModel.DataAnnotations.Validator.TryValidateObject(request, context, results, validateAllProperties: true))"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return null;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine("return results");
        writer.Indent++;
        writer.WriteLine(
            ".SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : new[] { string.Empty })"
        );
        writer.Indent++;
        writer.WriteLine(".Select(m => (Member: m, r.ErrorMessage)))");
        writer.Indent--;
        writer.WriteLine(".GroupBy(x => x.Member)");
        writer.WriteLine(
            ".ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? \"Invalid\").ToArray());"
        );
        writer.Indent--;
        writer.Indent--;
        writer.WriteLine("}");
        writer.Indent--;
        writer.WriteLine("}");

        ctx.AddSource(
            "EZRestAPIValidation.g.cs",
            SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
        );
    }

    /// <summary>
    /// Emits the shared ProblemDetails factory (once per assembly) producing
    /// RFC 9457 <c>application/problem+json</c> results with a machine-readable
    /// <c>code</c> extension member for 404 / 409 / 422.
    /// </summary>
    private static void EmitProblemsHelper(SourceProductionContext ctx, string assemblyName)
    {
        var writer = SourceWriter.Create();

        writer.WriteLine($"namespace {assemblyName};");
        writer.WriteLine();
        writer.WriteLine("using Microsoft.AspNetCore.Http;");
        writer.WriteLine();
        writer.WriteLine("public static class EZRestAPIProblems");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            "public static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult NotFound(string detail) => TypedResults.Problem("
        );
        writer.Indent++;
        writer.WriteLine(
            "statusCode: StatusCodes.Status404NotFound, title: \"Not Found\", detail: detail,"
        );
        writer.WriteLine(
            "extensions: new System.Collections.Generic.Dictionary<string, object?> { [\"code\"] = \"notFound\" });"
        );
        writer.Indent--;
        writer.WriteLine(
            "public static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Conflict(string detail) => TypedResults.Problem("
        );
        writer.Indent++;
        writer.WriteLine(
            "statusCode: StatusCodes.Status409Conflict, title: \"Conflict\", detail: detail,"
        );
        writer.WriteLine(
            "extensions: new System.Collections.Generic.Dictionary<string, object?> { [\"code\"] = \"conflict\" });"
        );
        writer.Indent--;
        writer.WriteLine(
            "public static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Unprocessable(string detail) => TypedResults.Problem("
        );
        writer.Indent++;
        writer.WriteLine(
            "statusCode: StatusCodes.Status422UnprocessableEntity, title: \"Unprocessable Entity\", detail: detail,"
        );
        writer.WriteLine(
            "extensions: new System.Collections.Generic.Dictionary<string, object?> { [\"code\"] = \"unprocessableEntity\" });"
        );
        writer.Indent--;
        writer.Indent--;
        writer.WriteLine("}");

        ctx.AddSource(
            "EZRestAPIProblems.g.cs",
            SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
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
            WriteDtoProperty(writer, property, emitValidation: true);
        }
        writer.Indent--;
        writer.WriteLine("}");

        ctx.AddSource(
            $"{className}.g.cs",
            SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
        );
    }

    /// <summary>
    /// Emits a single DTO property, optionally prefixed with the model's copied
    /// DataAnnotations plus a synthesized <c>[Required]</c> on non-nullable
    /// reference-typed properties (so a missing/null JSON field is caught by
    /// validation rather than a downstream 500).
    /// </summary>
    private static void WriteDtoProperty(
        IndentedTextWriter writer,
        ProviderExtensions.Property property,
        bool emitValidation
    )
    {
        if (emitValidation)
        {
            foreach (var annotation in property.DataAnnotations)
            {
                writer.WriteLine($"[{annotation}]");
            }

            if (property.IsNonNullableReferenceType && !HasRequiredAnnotation(property))
            {
                writer.WriteLine("[System.ComponentModel.DataAnnotations.Required]");
            }
        }

        writer.WriteLine(
            $"public {(property.NeedsRequiredModifier ? "required " : "")}{property.DtoTypeName} {property.PropertyName} {{ get; set; }}"
        );
    }

    private static bool HasRequiredAnnotation(ProviderExtensions.Property property)
    {
        foreach (var annotation in property.DataAnnotations)
        {
            if (
                annotation.StartsWith(
                    "System.ComponentModel.DataAnnotations.RequiredAttribute",
                    System.StringComparison.Ordinal
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static void RegisterDto(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<ProviderExtensions.Model> modelsProvider,
        string classNameFormat,
        string? idLine,
        bool emitValidation
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
                    WriteDtoProperty(writer, property, emitValidation);
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
