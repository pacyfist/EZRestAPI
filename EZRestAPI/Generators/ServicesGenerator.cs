namespace EZRestAPI.Generators;

using Scriban;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using EZRestAPI.Providers;

[Generator(LanguageNames.CSharp)]
public class ServicesGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var template = Template.Parse("""
            namespace {{model.AssemblyName}};
            
            public partial class {{model.SingularName}}Service
            {
                public Task Create(
                {{- for prop in model.Properties -}}
                    {{prop.TypeName}} {{prop.PropertyName}}, {{}} 
                {{- end -}}
                CancellationToken cancellationToken = default)
                {
                    return Task.CompletedTask;
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
