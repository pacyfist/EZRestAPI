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

    /// <summary>
    /// True when a read property is a get-only <c>IReadOnlyList&lt;T&gt;</c>
    /// projection over a private backing field: EF cannot use the (setter-less)
    /// property, so it is mapped through the field via <c>PropertyAccessMode.Field</c>.
    /// </summary>
    private static bool IsReadOnlyListProjection(ProviderExtensions.Property property) =>
        property.Kind == ProviderExtensions.NestedKind.None
        && property.TypeName.StartsWith(
            "System.Collections.Generic.IReadOnlyList<",
            System.StringComparison.Ordinal
        );

    private static bool AggregateNeedsConfig(ProviderExtensions.Aggregate aggregate) =>
        aggregate.Properties.Any(p =>
            p.Kind != ProviderExtensions.NestedKind.None || IsReadOnlyListProjection(p)
        );

    private static void InsertAggregateConfiguration(
        IndentedTextWriter writer,
        ProviderExtensions.Aggregate aggregate
    )
    {
        writer.WriteLine();
        writer.WriteLine($"modelBuilder.Entity<{aggregate.ClassName}>(entity =>");
        writer.WriteLine("{");
        writer.Indent++;
        foreach (var property in aggregate.Properties)
        {
            if (property.Kind != ProviderExtensions.NestedKind.None)
            {
                // Value objects / child entities map as owned types.
                InsertOwnedConfiguration(writer, "entity", property, 1);
            }
            else if (IsReadOnlyListProjection(property))
            {
                writer.WriteLine(
                    $"entity.PrimitiveCollection(e => e.{property.PropertyName}).UsePropertyAccessMode(Microsoft.EntityFrameworkCore.PropertyAccessMode.Field);"
                );
            }
        }
        writer.Indent--;
        writer.WriteLine("});");
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModelsWithRelationships().Collect();
        var aggregatesProvider = context.SyntaxProvider.GetAggregates().Collect();
        var combined = modelsProvider.Combine(aggregatesProvider);

        context.RegisterSourceOutput(
            combined,
            (ctx, pair) =>
            {
                var (models, aggregates) = pair;

                if (models.IsDefaultOrEmpty && aggregates.IsDefaultOrEmpty)
                {
                    return;
                }

                var assemblyName = models.IsDefaultOrEmpty
                    ? aggregates.First().AssemblyName
                    : models.First().AssemblyName;

                var writer = SourceWriter.Create();

                writer.WriteLine($"namespace {assemblyName};");
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

                // Aggregates get a DbSet and participate exactly like a [Model].
                foreach (var aggregate in aggregates)
                {
                    writer.WriteLine(
                        $"public DbSet<{aggregate.ClassName}> {aggregate.PluralName} {{ get; set; }} = null!;"
                    );
                }

                var modelsWithNested = models
                    .Where(m => m.Properties.Any(p => p.Kind != ProviderExtensions.NestedKind.None))
                    .ToList();

                var modelsWithRelationships = models
                    .Where(m => m.ParentRelationships.Any())
                    .ToList();

                var aggregatesWithConfig = aggregates.Where(AggregateNeedsConfig).ToList();

                if (
                    modelsWithNested.Count > 0
                    || modelsWithRelationships.Count > 0
                    || aggregatesWithConfig.Count > 0
                )
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

                    foreach (var model in modelsWithRelationships)
                    {
                        foreach (var rel in model.ParentRelationships)
                        {
                            writer.WriteLine();
                            writer.WriteLine($"modelBuilder.Entity<{model.ClassName}>()");
                            writer.Indent++;
                            writer.WriteLine($".HasOne<{rel.ParentClassName}>()");
                            writer.WriteLine(".WithMany()");
                            writer.WriteLine(
                                $".HasForeignKey(e => e.{rel.ForeignKeyPropertyName})"
                            );
                            writer.WriteLine(".OnDelete(DeleteBehavior.Restrict);");
                            writer.Indent--;
                        }
                    }

                    foreach (var aggregate in aggregatesWithConfig)
                    {
                        InsertAggregateConfiguration(writer, aggregate);
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
