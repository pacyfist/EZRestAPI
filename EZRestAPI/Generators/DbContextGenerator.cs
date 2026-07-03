namespace EZRestAPI.Generators;

using System.CodeDom.Compiler;
using System.Text;
using EZRestAPI.Providers;
using EZRestAPI.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

[Generator(LanguageNames.CSharp)]
public class DbContextGenerator : IIncrementalGenerator
{
    private static void InsertOwnedConfiguration(
        IndentedTextWriter writer,
        string builderName,
        ProviderExtensions.Property property,
        int depth
    )
    {
        var method =
            property.Kind == ProviderExtensions.NestedKind.Collection ? "OwnsMany" : "OwnsOne";
        var nestedChildren = property
            .Nested!.Properties.Where(p => p.Kind != ProviderExtensions.NestedKind.None)
            .ToList();

        if (nestedChildren.Count == 0)
        {
            writer.WriteLine($"{builderName}.{method}(e => e.{property.PropertyName});");
            return;
        }

        writer.WriteLine($"{builderName}.{method}(e => e.{property.PropertyName}, owned{depth} =>");
        writer.WriteLine("{");
        writer.Indent++;
        foreach (var child in nestedChildren)
        {
            InsertOwnedConfiguration(writer, $"owned{depth}", child, depth + 1);
        }
        writer.Indent--;
        writer.WriteLine("});");
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModels().Collect();

        context.RegisterSourceOutput(
            modelsProvider,
            (ctx, models) =>
            {
                if (models.IsDefaultOrEmpty)
                {
                    return;
                }

                var writer = SourceWriter.Create();

                writer.WriteLine($"namespace {models.First().AssemblyName};");
                writer.WriteLine();
                writer.WriteLine("using Microsoft.EntityFrameworkCore;");
                writer.WriteLine();
                writer.WriteLine("public partial class CustomDbContext : DbContext");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine(
                    "public CustomDbContext(DbContextOptions<CustomDbContext> options)"
                );
                writer.Indent++;
                writer.WriteLine(": base(options)");
                writer.Indent--;
                writer.WriteLine("{");
                writer.WriteLine("}");
                writer.WriteLine();

                foreach (var model in models)
                {
                    writer.WriteLine(
                        $"public DbSet<{model.ClassName}> {model.PluralName} {{ get; set; }} = null!;"
                    );
                }

                var modelsWithNested = models
                    .Where(m => m.Properties.Any(p => p.Kind != ProviderExtensions.NestedKind.None))
                    .ToList();

                if (modelsWithNested.Count > 0)
                {
                    writer.WriteLine();
                    writer.WriteLine(
                        "protected override void OnModelCreating(ModelBuilder modelBuilder)"
                    );
                    writer.WriteLine("{");
                    writer.Indent++;
                    writer.WriteLine("base.OnModelCreating(modelBuilder);");

                    foreach (var model in modelsWithNested)
                    {
                        writer.WriteLine();
                        writer.WriteLine($"modelBuilder.Entity<{model.ClassName}>(entity =>");
                        writer.WriteLine("{");
                        writer.Indent++;
                        foreach (
                            var property in model.Properties.Where(p =>
                                p.Kind != ProviderExtensions.NestedKind.None
                            )
                        )
                        {
                            InsertOwnedConfiguration(writer, "entity", property, 1);
                        }
                        writer.Indent--;
                        writer.WriteLine("});");
                    }

                    writer.Indent--;
                    writer.WriteLine("}");
                }

                writer.Indent--;
                writer.WriteLine("}");

                ctx.AddSource(
                    "CustomDbContext.g.cs",
                    SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
                );
            }
        );
    }
}
