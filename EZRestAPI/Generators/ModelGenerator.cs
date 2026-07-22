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

                EmitIdPartial(
                    ctx,
                    model.ModelNamespace,
                    model.ModelName,
                    // Anemic models are object-initialized, so the key needs a
                    // public setter like every other property.
                    "public int Id { get; set; }",
                    $"{model.ModelName}.g.cs"
                );
            }
        );

        var aggregatesProvider = context.SyntaxProvider.GetAggregates();

        context.RegisterSourceOutput(
            aggregatesProvider,
            (ctx, aggregate) =>
            {
                // The aggregate is never object-initialized from generated
                // code, so EF materializes the key through a private setter,
                // keeping invariants encapsulated.
                EmitIdPartial(
                    ctx,
                    aggregate.ModelNamespace,
                    aggregate.ModelName,
                    "public int Id { get; private set; }",
                    $"{aggregate.ModelName}.Id.g.cs"
                );
            }
        );
    }

    private static void EmitIdPartial(
        SourceProductionContext ctx,
        string modelNamespace,
        string modelName,
        string idLine,
        string hintName
    )
    {
        var writer = SourceWriter.Create();

        writer.WriteLine($"namespace {modelNamespace.ToCleanNamespace()};");
        writer.WriteLine();
        writer.WriteLine("using System.ComponentModel.DataAnnotations;");
        writer.WriteLine();
        writer.WriteLine($"public partial class {modelName}");
        writer.WriteLine("{");
        writer.Indent++;
        writer.WriteLine("[Key]");
        writer.WriteLine(idLine);
        writer.Indent--;
        writer.WriteLine("}");

        ctx.AddSource(hintName, SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8));
    }
}
