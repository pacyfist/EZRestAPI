namespace EZRestAPI.Generators;

using Scriban;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using EZRestAPI.Providers;

[Generator(LanguageNames.CSharp)]
public class DbContextGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var template = Template.Parse("""
            namespace {{AssemblyName}};

            using Microsoft.EntityFrameworkCore;
            
            public partial class CustomDbContext : DbContext
            {
                public CustomDbContext(DbContextOptions<DbContext> options)
                    : base(options)
                {

                }

                {{~ for model in models ~}}
                public DbSet<{{model.ClassName}}> {{model.PluralName}} { get; set; } = null!;
                {{~ end ~}}
            }
            """);


        var modelsProvider = context.SyntaxProvider.GetModels().Collect();

        context.RegisterSourceOutput(modelsProvider, (ctx, models) =>
        {
            ctx.AddSource(
                "CustomDbContext.g.cs",
                SourceText.From(template.Render(new
                {
                    models.First().AssemblyName,
                    models
                }, memberRenamer: n => n.Name), Encoding.UTF8));
        });
    }
}
