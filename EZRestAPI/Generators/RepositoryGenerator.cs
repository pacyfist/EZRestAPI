namespace EZRestAPI.Generators;

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using EZRestAPI.Providers;
using System.CodeDom.Compiler;

[Generator(LanguageNames.CSharp)]
public class RepositoryGenerator : IIncrementalGenerator
{
    public void InsertCreateMethod(ref IndentedTextWriter writer, ProviderExtensions.Model model)
    {
        var methodParameters = string.Join(", ", model.Properties.Select(p => $"{p.TypeName} {p.PropertyName.ToCamelCase()}"));

        writer.WriteLine($"public async Task<int> CreateAsync({methodParameters}, CancellationToken cancellationToken)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine($"var entity = new {model.ClassName}()");
        writer.WriteLine("{");
        writer.Indent++;
        foreach (var property in model.Properties)
        {
            writer.WriteLine($"{property.PropertyName} = {property.PropertyName.ToCamelCase()},");
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

    public void InsertReadMethod(ref IndentedTextWriter writer, ProviderExtensions.Model model)
    {
        var tupleDefinition = string.Join(", ", model.Properties.Select(p => $"{p.TypeName} {p.PropertyName}"));
        var tupleValues = string.Join(", ", model.Properties.Select(p => $"entity.{p.PropertyName}"));

        writer.WriteLine($"public async Task<({tupleDefinition})> ReadAsync(int id, CancellationToken cancellationToken)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine($"var entity = await context.{model.PluralName}.AsNoTracking().FirstAsync(s => s.Id == id, cancellationToken);");
        writer.WriteLine();
        writer.WriteLine($"return ({tupleValues});");
        writer.Indent--;
        writer.WriteLine("}");
    }

    public void InsertUpdateMethod(ref IndentedTextWriter writer, ProviderExtensions.Model model)
    {
        var methodParameters = string.Join(", ", model.Properties.Select(p => $"{p.TypeName} {p.PropertyName.ToCamelCase()}"));

        writer.WriteLine($"public async Task UpdateAsync(int id, {methodParameters}, CancellationToken cancellationToken)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine($"var entity = await context.{model.PluralName}.FirstAsync(s => s.Id == id, cancellationToken);");
        writer.WriteLine();
        foreach (var property in model.Properties)
        {
            writer.WriteLine($"entity.{property.PropertyName} = {property.PropertyName.ToCamelCase()};");
        }
        writer.WriteLine();
        writer.WriteLine($"await context.SaveChangesAsync(cancellationToken);");
        writer.Indent--;
        writer.WriteLine("}");
    }

    public void InsertDeleteMethod(ref IndentedTextWriter writer, ProviderExtensions.Model model)
    {
        writer.WriteLine($"public async Task DeleteAsync(int id, CancellationToken cancellationToken)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine($"await context.{model.PluralName}.Where(e => e.Id == id).ExecuteDeleteAsync(cancellationToken);");
        writer.Indent--;
        writer.WriteLine("}");
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModels();

        context.RegisterSourceOutput(modelsProvider, (ctx, model) =>
        {
            IndentedTextWriter writer = new IndentedTextWriter(new StringWriter());

            writer.WriteLine($"namespace {model.AssemblyName};");
            writer.WriteLine();
            writer.WriteLine("using Microsoft.EntityFrameworkCore;");
            writer.WriteLine();
            writer.WriteLine($"public partial class {model.SingularName}Repository(CustomDbContext context)");
            writer.WriteLine("{");
            writer.Indent++;
            InsertCreateMethod(ref writer, model);
            writer.WriteLine();
            InsertReadMethod(ref writer, model);
            writer.WriteLine();
            InsertUpdateMethod(ref writer, model);
            writer.WriteLine();
            InsertDeleteMethod(ref writer, model);
            writer.Indent--;
            writer.WriteLine("}");

            ctx.AddSource(
                $"{model.SingularName}Repository.g.cs",
                SourceText.From(
                    writer.InnerWriter.ToString(),
                    Encoding.UTF8));
        });
    }
}
