namespace EZRestAPI.Generators;

using System.Text;
using EZRestAPI.Providers;
using EZRestAPI.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

[Generator(LanguageNames.CSharp)]
public class DtoGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModels();

        RegisterDto(context, modelsProvider, "Create{0}Request", idLine: null);
        RegisterDto(context, modelsProvider, "Create{0}Response", "public int Id { get; set; }");
        RegisterDto(context, modelsProvider, "Read{0}Response", "public int? Id { get; set; }");
        RegisterDto(context, modelsProvider, "Update{0}Request", "public int Id { get; set; }");
    }

    private static void RegisterDto(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<ProviderExtensions.Model> modelsProvider,
        string classNameFormat,
        string? idLine
    )
    {
        context.RegisterSourceOutput(
            modelsProvider,
            (ctx, model) =>
            {
                var className = string.Format(classNameFormat, model.SingularName);

                var writer = SourceWriter.Create();

                writer.WriteLine($"namespace {model.AssemblyName};");
                writer.WriteLine();
                writer.WriteLine($"public class {className}");
                writer.WriteLine("{");
                writer.Indent++;
                if (idLine is not null)
                {
                    writer.WriteLine(idLine);
                }
                foreach (var property in model.Properties)
                {
                    writer.WriteLine(
                        $"public {(property.NeedsRequiredModifier ? "required " : "")}{property.DtoTypeName} {property.PropertyName} {{ get; set; }}"
                    );
                }
                writer.Indent--;
                writer.WriteLine("}");

                ctx.AddSource(
                    $"{className}.g.cs",
                    SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
                );
            }
        );
    }
}
