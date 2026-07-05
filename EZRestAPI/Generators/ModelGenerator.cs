namespace EZRestAPI.Generators;

using System.CodeDom.Compiler;
using System.Text;
using EZRestAPI.Providers;
using EZRestAPI.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

[Generator(LanguageNames.CSharp)]
public class ModelGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModels();

        context.RegisterSourceOutput(
            modelsProvider,
            (ctx, model) =>
            {
                // The user declared their own Id; nothing to add (EZR007
                // enforces that it is an int).
                if (model.UserIdTypeName is not null)
                {
                    return;
                }

                var writer = SourceWriter.Create();

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
                    SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
                );
            }
        );
    }
}
