namespace EZRestAPI.Generators;

using System.Text;
using EZRestAPI.Providers;
using EZRestAPI.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

[Generator(LanguageNames.CSharp)]
public class NestedGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var nestedProvider = context.SyntaxProvider.GetNestedModels();

        context.RegisterSourceOutput(
            nestedProvider,
            (ctx, nested) =>
            {
                var writer = SourceWriter.Create();

                writer.WriteLine($"namespace {nested.AssemblyName};");
                writer.WriteLine();
                writer.WriteLine("using System.Linq;");
                writer.WriteLine();

                writer.WriteLine($"public class {nested.SingularName}Dto");
                writer.WriteLine("{");
                writer.Indent++;
                foreach (var property in nested.Properties)
                {
                    writer.WriteLine(
                        $"public {(property.NeedsRequiredModifier ? "required " : "")}{property.DtoTypeName} {property.PropertyName} {{ get; set; }}"
                    );
                }
                writer.Indent--;
                writer.WriteLine("}");
                writer.WriteLine();

                writer.WriteLine($"public static class {nested.SingularName}Mapper");
                writer.WriteLine("{");
                writer.Indent++;

                writer.WriteLine(
                    $"public static {nested.ClassName} ToEntity({nested.SingularName}Dto dto)"
                );
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine($"return new {nested.ClassName}()");
                writer.WriteLine("{");
                writer.Indent++;
                foreach (var property in nested.Properties)
                {
                    writer.WriteLine(
                        $"{property.PropertyName} = {property.ToEntityExpression($"dto.{property.PropertyName}")},"
                    );
                }
                writer.Indent--;
                writer.WriteLine("};");
                writer.Indent--;
                writer.WriteLine("}");
                writer.WriteLine();

                writer.WriteLine(
                    $"public static {nested.SingularName}Dto ToDto({nested.ClassName} entity)"
                );
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine($"return new {nested.SingularName}Dto()");
                writer.WriteLine("{");
                writer.Indent++;
                foreach (var property in nested.Properties)
                {
                    writer.WriteLine(
                        $"{property.PropertyName} = {property.ToDtoExpression($"entity.{property.PropertyName}")},"
                    );
                }
                writer.Indent--;
                writer.WriteLine("};");
                writer.Indent--;
                writer.WriteLine("}");

                writer.Indent--;
                writer.WriteLine("}");

                ctx.AddSource(
                    $"{nested.SingularName}Dto.g.cs",
                    SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
                );
            }
        );
    }
}
