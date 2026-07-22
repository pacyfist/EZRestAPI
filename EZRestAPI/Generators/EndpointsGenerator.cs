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

    private static void InsertNestedGroup(
        IndentedTextWriter writer,
        ProviderExtensions.Model model,
        ProviderExtensions.RelationshipInfo rel
    )
    {
        var parentRoute = rel.ParentPluralName.ToLowerInvariant();
        var childRoute = rel.ChildPluralName.ToLowerInvariant();
        var name = $"{rel.ChildSingularName}Under{rel.ParentSingularName}";
        var tag = rel.ChildPluralName;
        var groupVar = $"{rel.ParentSingularName.ToLowerInvariant()}{rel.ChildSingularName}Group";
        var createRequest = $"Create{name}Request";
        var updateRequest = $"Update{name}Request";

        writer.WriteLine(
            $"var {groupVar} = app.MapGroup(\"/{parentRoute}/{{parentId:int}}/{childRoute}\");"
        );
        writer.WriteLine();

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
        writer.WriteLine();

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
        writer.WriteLine();

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
        writer.WriteLine();

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
        writer.WriteLine();

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

    // ---- Aggregate endpoints (read/list/delete; T2 scope) ----------------

    /// <summary>
    /// Emits the aggregate's factory-based create endpoint: <c>POST /{plural}</c>
    /// validating the request (422 via <see cref="EmitValidationGuard"/>),
    /// invoking the repository's factory-based <c>CreateAsync</c>, and returning
    /// 201 + a flat <c>/{plural}/{id}</c> Location with the Read representation
    /// of the created aggregate. Uses the same union / ProblemDetails / metadata
    /// conventions as every other write endpoint.
    /// </summary>
    private static void InsertAggregateCreateEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Aggregate aggregate
    )
    {
        var route = aggregate.PluralName.ToLowerInvariant();

        writer.WriteLine(
            $"group.MapPost(\"/\", async Task<Results<Created<Read{aggregate.SingularName}Response>, ProblemHttpResult>> ("
        );
        writer.Indent++;
        writer.WriteLine($"[FromServices] {aggregate.SingularName}Repository repository,");
        writer.WriteLine($"[FromBody] Create{aggregate.SingularName}Request request,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        EmitValidationGuard(writer);
        writer.WriteLine(
            "var response = await repository.CreateAsync(request, cancellationToken);"
        );
        writer.WriteLine();
        writer.WriteLine($"return TypedResults.Created($\"/{route}/{{response.Id}}\", response);");
        writer.Indent--;
        writer.WriteLine("})");
        EmitMetadata(writer, $"Create{aggregate.SingularName}", aggregate.PluralName, 422);
    }

    private static void InsertAggregateListEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Aggregate aggregate
    )
    {
        writer.WriteLine(
            $"group.MapGet(\"/\", async Task<Results<Ok<PagedResponse<Read{aggregate.SingularName}Response>>, ProblemHttpResult>> ("
        );
        writer.Indent++;
        writer.WriteLine($"[FromServices] {aggregate.SingularName}Repository repository,");
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
        EmitMetadata(writer, $"List{aggregate.PluralName}", aggregate.PluralName, 422);
    }

    private static void InsertAggregateReadEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Aggregate aggregate
    )
    {
        writer.WriteLine(
            $"group.MapGet(\"/{{id:int}}\", async Task<Results<Ok<Read{aggregate.SingularName}Response>, ProblemHttpResult>> ("
        );
        writer.Indent++;
        writer.WriteLine($"[FromServices] {aggregate.SingularName}Repository repository,");
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
            $"return EZRestAPIProblems.NotFound($\"No {aggregate.SingularName} with id {{id}} exists.\");"
        );
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("return TypedResults.Ok(response);");
        writer.Indent--;
        writer.WriteLine("})");
        EmitMetadata(writer, $"Read{aggregate.SingularName}", aggregate.PluralName, 404);
    }

    private static void InsertAggregateDeleteEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Aggregate aggregate
    )
    {
        writer.WriteLine(
            "group.MapDelete(\"/{id:int}\", async Task<Results<NoContent, ProblemHttpResult>> ("
        );
        writer.Indent++;
        writer.WriteLine($"[FromServices] {aggregate.SingularName}Repository repository,");
        writer.WriteLine("int id,");
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("var deleted = await repository.DeleteAsync(id, cancellationToken);");
        writer.WriteLine();
        writer.WriteLine(
            $"return deleted ? TypedResults.NoContent() : EZRestAPIProblems.NotFound($\"No {aggregate.SingularName} with id {{id}} exists.\");"
        );
        writer.Indent--;
        writer.WriteLine("})");
        EmitMetadata(writer, $"Delete{aggregate.SingularName}", aggregate.PluralName, 404);
    }

    /// <summary>
    /// Emits an aggregate command endpoint: <c>POST /{plural}/{id}/{command}</c>
    /// where <c>{command}</c> is the [Command] route override or the kebab-cased
    /// method name. The handler loads/mutates the aggregate through the tracked
    /// <c>Execute{Command}Async</c> repository method inside a try/catch that
    /// maps the domain guard's exception to a status:
    /// <list type="bullet">
    ///   <item><c>System.ArgumentException</c> (and subclasses, e.g.
    ///   <c>ArgumentOutOfRangeException</c>) → 422 ProblemDetails.</item>
    ///   <item><c>System.InvalidOperationException</c> → 409 ProblemDetails.</item>
    /// </list>
    /// The <c>ArgumentException</c> arm is written first so a subclass never
    /// escapes into a broader arm and <c>InvalidOperationException</c> is never
    /// swallowed by it; anything else propagates (500). A missing aggregate
    /// (null response) is 404; success is 200 + the mutated Read representation.
    /// </summary>
    private static void InsertAggregateCommandEndpoint(
        IndentedTextWriter writer,
        ProviderExtensions.Aggregate aggregate,
        ProviderExtensions.CommandInfo command
    )
    {
        var hasRequest = command.Parameters.Any();
        var operationId = $"{command.MethodName}{aggregate.SingularName}";

        writer.WriteLine(
            $"group.MapPost(\"/{{id:int}}/{command.RouteName}\", async Task<Results<Ok<Read{aggregate.SingularName}Response>, ProblemHttpResult>> ("
        );
        writer.Indent++;
        writer.WriteLine($"[FromServices] {aggregate.SingularName}Repository repository,");
        writer.WriteLine("int id,");
        if (hasRequest)
        {
            writer.WriteLine(
                $"[FromBody] {command.MethodName}{aggregate.SingularName}Request request,"
            );
        }
        writer.WriteLine("CancellationToken cancellationToken) =>");
        writer.Indent--;
        writer.WriteLine("{");
        writer.Indent++;
        if (hasRequest)
        {
            EmitValidationGuard(writer);
        }
        writer.WriteLine($"Read{aggregate.SingularName}Response? response;");
        writer.WriteLine("try");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            hasRequest
                ? $"response = await repository.Execute{command.MethodName}Async(id, request, cancellationToken);"
                : $"response = await repository.Execute{command.MethodName}Async(id, cancellationToken);"
        );
        writer.Indent--;
        writer.WriteLine("}");
        // ArgumentException (and its subclasses) maps to 422 and MUST be caught
        // before the InvalidOperationException arm.
        writer.WriteLine("catch (System.ArgumentException ex)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return EZRestAPIProblems.Unprocessable(ex.Message);");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine("catch (System.InvalidOperationException ex)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("return EZRestAPIProblems.Conflict(ex.Message);");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("if (response is null)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine(
            $"return EZRestAPIProblems.NotFound($\"No {aggregate.SingularName} with id {{id}} exists.\");"
        );
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("return TypedResults.Ok(response);");
        writer.Indent--;
        writer.WriteLine("})");
        EmitMetadata(writer, operationId, aggregate.PluralName, 404, 409, 422);
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

        var aggregatesProvider = context.SyntaxProvider.GetAggregates();

        context.RegisterSourceOutput(
            aggregatesProvider,
            (ctx, aggregate) =>
            {
                var writer = SourceWriter.Create();

                writer.WriteLine($"namespace {aggregate.AssemblyName};");
                writer.WriteLine();
                writer.WriteLine("using System.Threading;");
                writer.WriteLine("using Microsoft.AspNetCore.Builder;");
                writer.WriteLine("using Microsoft.AspNetCore.Http;");
                writer.WriteLine("using Microsoft.AspNetCore.Http.HttpResults;");
                writer.WriteLine("using Microsoft.AspNetCore.Mvc;");
                writer.WriteLine("using Microsoft.AspNetCore.Routing;");
                writer.WriteLine();
                writer.WriteLine($"public static class {aggregate.SingularName}Endpoints");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine(
                    $"public static IEndpointRouteBuilder Map{aggregate.SingularName}Endpoints(this IEndpointRouteBuilder app)"
                );
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine(
                    $"var group = app.MapGroup(\"/{aggregate.PluralName.ToLowerInvariant()}\");"
                );
                writer.WriteLine();
                InsertAggregateListEndpoint(writer, aggregate);
                writer.WriteLine();
                if (aggregate.Factory is not null)
                {
                    InsertAggregateCreateEndpoint(writer, aggregate);
                    writer.WriteLine();
                }
                InsertAggregateReadEndpoint(writer, aggregate);
                writer.WriteLine();
                InsertAggregateDeleteEndpoint(writer, aggregate);
                foreach (var command in aggregate.Commands)
                {
                    writer.WriteLine();
                    InsertAggregateCommandEndpoint(writer, aggregate, command);
                }
                writer.WriteLine();
                writer.WriteLine("return app;");
                writer.Indent--;
                writer.WriteLine("}");
                writer.Indent--;
                writer.WriteLine("}");

                ctx.AddSource(
                    $"{aggregate.SingularName}Endpoints.g.cs",
                    SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
                );
            }
        );
    }
}
