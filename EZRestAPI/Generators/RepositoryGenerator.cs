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
        writer.WriteLine(
            $"public async Task<int> CreateAsync(Create{model.SingularName}Request request, CancellationToken cancellationToken)"
        );
        writer.WriteLine("{");
        writer.Indent++;
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

    private static void InsertUpdateMethod(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
    {
        writer.WriteLine(
            $"public async Task<bool> UpdateAsync(int id, Update{model.SingularName}Request request, CancellationToken cancellationToken)"
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
        writer.WriteLine("return false;");
        writer.Indent--;
        writer.WriteLine("}");
        writer.WriteLine();
        foreach (var property in model.Properties)
        {
            writer.WriteLine(
                $"entity.{property.PropertyName} = {property.ToEntityExpression($"request.{property.PropertyName}")};"
            );
        }
        writer.WriteLine();
        writer.WriteLine($"await context.SaveChangesAsync(cancellationToken);");
        writer.WriteLine();
        writer.WriteLine("return true;");
        writer.Indent--;
        writer.WriteLine("}");
    }

    private static void InsertDeleteMethod(
        IndentedTextWriter writer,
        ProviderExtensions.Model model
    )
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
                writer.WriteLine("using System.Linq;");
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
                InsertUpdateMethod(writer, model);
                writer.WriteLine();
                InsertDeleteMethod(writer, model);
                writer.Indent--;
                writer.WriteLine("}");

                ctx.AddSource(
                    $"{model.SingularName}Repository.g.cs",
                    SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
                );
            }
        );
    }
}
