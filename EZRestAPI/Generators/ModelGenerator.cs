namespace EZRestAPI.Generators;

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using EZRestAPI.Providers;
using System.CodeDom.Compiler;

[Generator(LanguageNames.CSharp)]
public class ModelGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModels();

        context.RegisterSourceOutput(modelsProvider, (ctx, model) =>
        {
            IndentedTextWriter writer = new(new StringWriter());

            writer.WriteLine($"namespace {model.ModelNamespace.ToCleanNamespace()};");
            writer.WriteLine();
            writer.WriteLine("using System.ComponentModel.DataAnnotations;");
            writer.WriteLine();
            writer.WriteLine($"public partial class {model.ModelName}");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine("[Key]");
            writer.WriteLine("public int Id { get; set; }");
            writer.Indent--;
            writer.WriteLine("}");

            ctx.AddSource(
                $"{model.ModelName}.g.cs",
                SourceText.From(
                    writer.InnerWriter.ToString(),
                    Encoding.UTF8));
        });
    }
}
