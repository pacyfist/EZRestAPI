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
        var requiredProperties = model.Properties.Where(p => p.IsRequired);
        var methodParameters = string.Join(", ", requiredProperties.Select(p => $"{p.TypeName} param_{p.PropertyName}"));

        writer.WriteLine($"public async Task CreateAsync({methodParameters}, CancellationToken cancellationToken)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine($"var entity = new {model.ModelName}()");
        writer.WriteLine("{");
        writer.Indent++;
        foreach (var property in requiredProperties)
        {
            writer.WriteLine($"{property.PropertyName} = param_{property.PropertyName},");
        }
        writer.Indent--;
        writer.WriteLine("};");
        writer.WriteLine();
        writer.WriteLine($"context.{model.PluralName}.Add(entity);");
        writer.WriteLine($"await context.SaveChangesAsync(cancellationToken);");
        writer.Indent--;
        writer.WriteLine("}");
    }

    public void InsertUpdateMethod(ref IndentedTextWriter writer, ProviderExtensions.Model model)
    {
        var requiredProperties = model.Properties.Where(p => p.IsRequired);
        var methodParameters = string.Join(", ", requiredProperties.Select(p => $"{p.TypeName} param_{p.PropertyName}"));

        writer.WriteLine($"public async Task UpdateAsync(int param_Id, {methodParameters}, CancellationToken cancellationToken)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine($"var entity = context.{model.PluralName}.Find(param_Id);");
        writer.WriteLine();
        foreach (var property in requiredProperties)
        {
            writer.WriteLine($"entity.{property.PropertyName} = param_{property.PropertyName};");
        }
        writer.WriteLine();
        writer.WriteLine($"await context.SaveChangesAsync(cancellationToken);");
        writer.Indent--;
        writer.WriteLine("}");
    }

    public void InsertDeleteMethod(ref IndentedTextWriter writer, ProviderExtensions.Model model)
    {
        var requiredProperties = model.Properties.Where(p => p.IsRequired);
        var methodParameters = string.Join(", ", requiredProperties.Select(p => $"{p.TypeName} param_{p.PropertyName}"));

        writer.WriteLine($"public async Task DeleteAsync(int id, CancellationToken cancellationToken)");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine($"await context.{model.PluralName}");
        writer.Indent++;
        writer.WriteLine(".Where(e => e.Id == id)");
        writer.WriteLine(".ExecuteDeleteAsync(cancellationToken);");
        writer.Indent--;
        writer.WriteLine();
        writer.WriteLine($"await context.SaveChangesAsync(cancellationToken);");
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
            InsertUpdateMethod(ref writer, model);
            writer.WriteLine();
            InsertDeleteMethod(ref writer, model);
            writer.Indent--;
            writer.WriteLine("}");

            ctx.AddSource(
                $"{model.SingularName}Service.g.cs",
                SourceText.From(
                    writer.InnerWriter.ToString(),
                    Encoding.UTF8));
        });
    }
}
