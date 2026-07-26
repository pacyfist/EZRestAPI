namespace EZRestAPI.Generators;

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
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

    private static string StatusConst(int status) =>
        status switch
        {
            404 => "StatusCodes.Status404NotFound",
            409 => "StatusCodes.Status409Conflict",
            422 => "StatusCodes.Status422UnprocessableEntity",
            _ => throw new System.ArgumentOutOfRangeException(nameof(status)),
        };

    /// <summary>
    /// Emits the OpenAPI metadata chain that terminates each mapped route:
    /// the stable <c>operationId</c> (<c>.WithName</c>), the resource
    /// <c>.WithTags</c>, and one <c>.ProducesProblem(..., "application/problem+json")</c>
    /// per error status the route can return. The chain is written after the
    /// closing <c>})</c> of the handler lambda and carries the terminating <c>;</c>.
    /// </summary>
    private static void EmitMetadata(
        IndentedTextWriter writer,
        string operationId,
        string tag,
        params int[] problemStatuses
    )
    {
        writer.Indent++;
        writer.WriteLine($".WithName(\"{operationId}\")");
        if (problemStatuses.Length == 0)
        {
            writer.WriteLine($".WithTags(\"{tag}\");");
        }
        else
        {
            writer.WriteLine($".WithTags(\"{tag}\")");
            for (var i = 0; i < problemStatuses.Length; i++)
            {
                var terminator = i == problemStatuses.Length - 1 ? ";" : "";
                // 422 carries the validation `errors` field-map, so document it as
                // a ValidationProblem (HttpValidationProblemDetails) rather than a
                // bare ProblemDetails, so generated clients see the errors schema.
                var producer =
                    problemStatuses[i] == 422 ? "ProducesValidationProblem" : "ProducesProblem";
                writer.WriteLine(
                    $".{producer}({StatusConst(problemStatuses[i])}, \"application/problem+json\"){terminator}"
                );
            }
        }
        writer.Indent--;
    }

    /// <summary>
    /// Emits the inline DataAnnotations validation guard at the top of a
    /// POST/PUT handler: a failed <see cref="EZRestAPIValidation.Validate"/>
    /// yields a 422 <c>HttpValidationProblemDetails</c> (RFC 9457 <c>errors</c>
    /// field-map) via <c>TypedResults.Problem</c>.
    /// </summary>
    private static void EmitValidationGuard(IndentedTextWriter writer)
    {
        writer.WriteLine("var validationErrors = EZRestAPIValidation.Validate(request);");
        writer.WriteLine("if (validationErrors is not null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            "var problem = new Microsoft.AspNetCore.Http.HttpValidationProblemDetails(validationErrors)"
        );
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("Status = StatusCodes.Status422UnprocessableEntity,");
        writer.WriteLine("Title = \"One or more validation errors occurred.\",");
        writer.WriteLine(
            "Detail = \"One or more fields failed validation; see 'errors' for details.\","
        );
        writer.Indent--;
        writer.WriteLine("};");
        writer.WriteLine("problem.Extensions[\"code\"] = \"unprocessableEntity\";");
        writer.WriteLine("return TypedResults.Problem(problem);");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
    }

    private static void InsertListEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
    {
        writer.WriteLine(
            $"group.MapGet(\"/\", async Task<Results<Ok<PagedResponse<Read{model.SingularName}Response>>, ProblemHttpResult>> ("
        );
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
        writer.WriteLine(
            "return EZRestAPIProblems.Unprocessable(\"page and pageSize must be >= 1.\");"
        );
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine("pageSize = System.Math.Min(pageSize, 100);");
        writer.WriteLine();
        writer.WriteLine(
            "var response = await repository.ListAsync(page, pageSize, cancellationToken);"
        );
        writer.WriteLine("return TypedResults.Ok(response);");
        writer.Indent--;
        writer.WriteLine("})");
        EmitMetadata(writer, $"List{model.PluralName}", model.PluralName, 422);
    }

    private static void InsertCreateEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
    {
        writer.WriteLine(
            $"group.MapPost(\"/\", async Task<Results<Created<Create{model.SingularName}Response>, ProblemHttpResult>> ("
        );
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine($"[FromBody] Create{model.SingularName}Request request,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        var hasParents = model.ParentRelationships.Any();

        writer.WriteLine("{");
        writer.Indent++;
        EmitValidationGuard(writer);
        writer.WriteLine("var id = await repository.CreateAsync(request, cancellationToken);");
        writer.WriteLine();
        if (hasParents)
        {
            var parentName = model.ParentRelationships.First().ParentSingularName;
            writer.WriteLine("if (id is null)");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine(
                $"return EZRestAPIProblems.Unprocessable(\"The referenced {parentName} does not exist.\");"
            );
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
                ? $"return TypedResults.Created($\"/{Route(model)}/{{id.Value}}\", response);"
                : $"return TypedResults.Created($\"/{Route(model)}/{{id}}\", response);"
        );
        writer.Indent--;
        writer.WriteLine("})");
        EmitMetadata(writer, $"Create{model.SingularName}", model.PluralName, 422);
    }

    private static void InsertReadEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
    {
        writer.WriteLine(
            $"group.MapGet(\"/{{id:int}}\", async Task<Results<Ok<Read{model.SingularName}Response>, ProblemHttpResult>> ("
        );
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
        writer.WriteLine(
            $"return EZRestAPIProblems.NotFound($\"No {model.SingularName} with id {{id}} exists.\");"
        );
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("return TypedResults.Ok(response);");
        writer.Indent--;
        writer.WriteLine("})");
        EmitMetadata(writer, $"Read{model.SingularName}", model.PluralName, 404);
    }

    private static void InsertUpdateEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
    {
        writer.WriteLine(
            $"group.MapPut(\"/{{id:int}}\", async Task<Results<NoContent, ProblemHttpResult>> ("
        );
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine("int id,");
        writer.WriteLine($"[FromBody] Update{model.SingularName}Request request,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        EmitValidationGuard(writer);
        if (model.ParentRelationships.Any())
        {
            var parentName = model.ParentRelationships.First().ParentSingularName;
            writer.WriteLine(
                "var outcome = await repository.UpdateAsync(id, request, cancellationToken);"
            );
            writer.WriteLine();
            writer.WriteLine("return outcome switch");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine("WriteResult.Success => TypedResults.NoContent(),");
            writer.WriteLine(
                $"WriteResult.Conflict => EZRestAPIProblems.Unprocessable(\"The referenced {parentName} does not exist.\"),"
            );
            writer.WriteLine(
                $"_ => EZRestAPIProblems.NotFound($\"No {model.SingularName} with id {{id}} exists.\"),"
            );
            writer.Indent--;
            writer.WriteLine("};");
        }
        else
        {
            writer.WriteLine(
                "var updated = await repository.UpdateAsync(id, request, cancellationToken);"
            );
            writer.WriteLine();
            writer.WriteLine(
                $"return updated ? TypedResults.NoContent() : EZRestAPIProblems.NotFound($\"No {model.SingularName} with id {{id}} exists.\");"
            );
        }
        writer.Indent--;
        writer.WriteLine("})");
        EmitMetadata(writer, $"Update{model.SingularName}", model.PluralName, 422, 404);
    }

    private static void InsertDeleteEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
    {
        writer.WriteLine(
            $"group.MapDelete(\"/{{id:int}}\", async Task<Results<NoContent, ProblemHttpResult>> ("
        );
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine("int id,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        if (model.ChildRelationships.Any())
        {
            var childName = model.ChildRelationships.First().ChildPluralName;
            writer.WriteLine("var outcome = await repository.DeleteAsync(id, cancellationToken);");
            writer.WriteLine();
            writer.WriteLine("return outcome switch");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine("WriteResult.Success => TypedResults.NoContent(),");
            writer.WriteLine(
                $"WriteResult.Conflict => EZRestAPIProblems.Conflict(\"The {model.SingularName} has dependent {childName} and cannot be deleted.\"),"
            );
            writer.WriteLine(
                $"_ => EZRestAPIProblems.NotFound($\"No {model.SingularName} with id {{id}} exists.\"),"
            );
            writer.Indent--;
            writer.WriteLine("};");
            writer.Indent--;
            writer.WriteLine("})");
            EmitMetadata(writer, $"Delete{model.SingularName}", model.PluralName, 404, 409);
        }
        else
        {
            writer.WriteLine("var deleted = await repository.DeleteAsync(id, cancellationToken);");
            writer.WriteLine();
            writer.WriteLine(
                $"return deleted ? TypedResults.NoContent() : EZRestAPIProblems.NotFound($\"No {model.SingularName} with id {{id}} exists.\");"
            );
            writer.Indent--;
            writer.WriteLine("})");
            EmitMetadata(writer, $"Delete{model.SingularName}", model.PluralName, 404);
        }
    }

    private static void InsertNestedListEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model,
        ProviderExtensions.RelationshipInfo rel
    )
    {
        var name = $"{rel.ChildSingularName}Under{rel.ParentSingularName}";
        var tag = rel.ChildPluralName;
        var groupVar = $"{rel.ParentSingularName.ToLowerInvariant()}{rel.ChildSingularName}Group";

        // GET / -> paged list scoped to the parent.
        writer.WriteLine(
            $"{groupVar}.MapGet(\"/\", async Task<Results<Ok<PagedResponse<Read{model.SingularName}Response>>, ProblemHttpResult>> ("
        );
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
        writer.WriteLine(
            "return EZRestAPIProblems.Unprocessable(\"page and pageSize must be >= 1.\");"
        );
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
        writer.WriteLine(
            $"return EZRestAPIProblems.NotFound($\"No {rel.ParentSingularName} with id {{parentId}} exists.\");"
        );
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("return TypedResults.Ok(response);");
        writer.Indent--;
        writer.WriteLine("})");
        EmitMetadata(writer, $"List{name}", tag, 422, 404);
    }

    private static void InsertNestedCreateEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model,
        ProviderExtensions.RelationshipInfo rel
    )
    {
        var childRoute = rel.ChildPluralName.ToLowerInvariant();
        var name = $"{rel.ChildSingularName}Under{rel.ParentSingularName}";
        var tag = rel.ChildPluralName;
        var groupVar = $"{rel.ParentSingularName.ToLowerInvariant()}{rel.ChildSingularName}Group";
        var createRequest = $"Create{name}Request";

        // POST / -> create under the parent (FK from route).
        writer.WriteLine(
            $"{groupVar}.MapPost(\"/\", async Task<Results<Created<Create{model.SingularName}Response>, ProblemHttpResult>> ("
        );
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine("int parentId,");
        writer.WriteLine($"[FromBody] {createRequest} request,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        EmitValidationGuard(writer);
        writer.WriteLine(
            $"var id = await repository.Create{name}Async(parentId, request, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine("if (id is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"return EZRestAPIProblems.NotFound($\"No {rel.ParentSingularName} with id {{parentId}} exists.\");"
        );
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
        writer.WriteLine(
            $"return TypedResults.Created($\"/{childRoute}/{{id.Value}}\", response);"
        );
        writer.Indent--;
        writer.WriteLine("})");
        EmitMetadata(writer, $"Create{name}", tag, 422, 404);
    }

    private static void InsertNestedReadEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model,
        ProviderExtensions.RelationshipInfo rel
    )
    {
        var name = $"{rel.ChildSingularName}Under{rel.ParentSingularName}";
        var tag = rel.ChildPluralName;
        var groupVar = $"{rel.ParentSingularName.ToLowerInvariant()}{rel.ChildSingularName}Group";

        // GET /{id:int} -> scoped read.
        writer.WriteLine(
            $"{groupVar}.MapGet(\"/{{id:int}}\", async Task<Results<Ok<Read{model.SingularName}Response>, ProblemHttpResult>> ("
        );
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
        writer.WriteLine(
            $"return EZRestAPIProblems.NotFound($\"No {rel.ChildSingularName} with id {{id}} exists under {rel.ParentSingularName} {{parentId}}.\");"
        );
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("return TypedResults.Ok(response);");
        writer.Indent--;
        writer.WriteLine("})");
        EmitMetadata(writer, $"Read{name}", tag, 404);
    }

    private static void InsertNestedUpdateEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model,
        ProviderExtensions.RelationshipInfo rel
    )
    {
        var name = $"{rel.ChildSingularName}Under{rel.ParentSingularName}";
        var tag = rel.ChildPluralName;
        var groupVar = $"{rel.ParentSingularName.ToLowerInvariant()}{rel.ChildSingularName}Group";
        var updateRequest = $"Update{name}Request";

        // PUT /{id:int} -> scoped update.
        writer.WriteLine(
            $"{groupVar}.MapPut(\"/{{id:int}}\", async Task<Results<NoContent, ProblemHttpResult>> ("
        );
        writer.Indent++;
        writer.WriteLine($"[FromServices] {model.SingularName}Repository repository,");
        writer.WriteLine("int parentId,");
        writer.WriteLine("int id,");
        writer.WriteLine($"[FromBody] {updateRequest} request,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        EmitValidationGuard(writer);
        writer.WriteLine(
            $"var updated = await repository.Update{name}Async(parentId, id, request, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine(
            $"return updated ? TypedResults.NoContent() : EZRestAPIProblems.NotFound($\"No {rel.ChildSingularName} with id {{id}} exists under {rel.ParentSingularName} {{parentId}}.\");"
        );
        writer.Indent--;
        writer.WriteLine("})");
        EmitMetadata(writer, $"Update{name}", tag, 422, 404);
    }

    private static void InsertNestedDeleteEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Model model,
        ProviderExtensions.RelationshipInfo rel
    )
    {
        var name = $"{rel.ChildSingularName}Under{rel.ParentSingularName}";
        var tag = rel.ChildPluralName;
        var groupVar = $"{rel.ParentSingularName.ToLowerInvariant()}{rel.ChildSingularName}Group";

        // DELETE /{id:int} -> scoped delete.
        writer.WriteLine(
            $"{groupVar}.MapDelete(\"/{{id:int}}\", async Task<Results<NoContent, ProblemHttpResult>> ("
        );
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
        writer.WriteLine("WriteResult.Success => TypedResults.NoContent(),");
        writer.WriteLine(
            $"WriteResult.Conflict => EZRestAPIProblems.Conflict(\"The {rel.ChildSingularName} has dependent records and cannot be deleted.\"),"
        );
        writer.WriteLine(
            $"_ => EZRestAPIProblems.NotFound($\"No {rel.ChildSingularName} with id {{id}} exists under {rel.ParentSingularName} {{parentId}}.\"),"
        );
        writer.Indent--;
        writer.WriteLine("};");
        writer.Indent--;
        writer.WriteLine("})");
        EmitMetadata(writer, $"Delete{name}", tag, 404, 409);
    }

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

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModelsWithRelationships();

        context.RegisterSourceOutput(
            modelsProvider,
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

                var flat = new List<Action>();
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

                // No flat verb survived: don't emit a "group" nobody reads.
                if (flat.Count > 0)
                {
                    writer.WriteLine($"var group = app.MapGroup(\"/{Route(model)}\");");
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
        );
    }
}
