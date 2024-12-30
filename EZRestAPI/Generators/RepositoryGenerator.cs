namespace EZRestAPI.Generators;

using Scriban;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using EZRestAPI.Providers;

[Generator(LanguageNames.CSharp)]
public class RepositoryGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var template = Template.Parse("""
            namespace {{model.AssemblyName}};
            
            using Microsoft.EntityFrameworkCore;

            {{func onlyRequired(p)
                ret p.IsRequired
            end}}

            public partial class {{model.SingularName}}Repository(
                CustomDbContext context)
            {
                public async Task CreateAsync(
                    {{~ for prop in model.Properties | array.filter @onlyRequired ~}}
                    {{prop.TypeName}} {{prop.PropertyName}},
                    {{~ end ~}}
                    CancellationToken cancellationToken)
                {
                    var entity = new {{model.ModelName}}()
                    {
                    {{~ for prop in model.Properties | array.filter @onlyRequired ~}}
                        {{prop.PropertyName}} = {{prop.PropertyName}},
                    {{~ end ~}}
                    };

                    context.{{model.PluralName}}.Add(entity);
                    await context.SaveChangesAsync(cancellationToken);
                }

                public async Task DeleteAsync(
                    int Id,
                    CancellationToken cancellationToken)
                {
                    await context
                        .{{model.PluralName}}
                        .Where(e => e.Id == Id)
                        .ExecuteDeleteAsync(cancellationToken);
                    
                    await context.SaveChangesAsync(cancellationToken);
                }
            }
            """);

        var modelsProvider = context.SyntaxProvider.GetModels();

        context.RegisterSourceOutput(modelsProvider, (ctx, model) =>
        {
            ctx.AddSource(
                $"{model.SingularName}Service.g.cs",
                SourceText.From(
                    template.Render(new { model }, memberRenamer: n => n.Name),
                    Encoding.UTF8));
        });
    }
}
