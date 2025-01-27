namespace EZRestAPI.Generators;

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using EZRestAPI.Providers;
using System.CodeDom.Compiler;

[Generator(LanguageNames.CSharp)]
public class DbContextGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModels().Collect();

        context.RegisterSourceOutput(modelsProvider, (ctx, models) =>
        {
            var writer = new IndentedTextWriter(new StringWriter());

            writer.WriteLine($"namespace {models.First().AssemblyName};");
            writer.WriteLine();
            writer.WriteLine("using Microsoft.EntityFrameworkCore;");
            writer.WriteLine();
            writer.WriteLine("public partial class CustomDbContext : DbContext");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine("public CustomDbContext(DbContextOptions<CustomDbContext> options)");
            writer.Indent++;
            writer.WriteLine(": base(options)");
            writer.Indent--;
            writer.WriteLine("{");
            writer.WriteLine("}");
            writer.WriteLine();

            foreach (var model in models)
            {
                writer.WriteLine($"public DbSet<{model.ClassName}> {model.PluralName} {{ get; set; }} = null!;");
            }

            writer.Indent--;
            writer.WriteLine("}");

            ctx.AddSource(
                "CustomDbContext.g.cs",
                SourceText.From(
                    writer.InnerWriter.ToString(),
                    Encoding.UTF8));
        });
    }
}
