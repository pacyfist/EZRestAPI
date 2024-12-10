namespace EZRestAPI.Generators;

using Microsoft.CodeAnalysis;
using System.Linq;

public static class PipelinesExtensions
{
    public record Model(string AssemblyName, string ModelNamespace, string ClassName, string SingularName, string PluralName);

    public static IncrementalValuesProvider<Model> GetModels(this SyntaxValueProvider provider)
    {
        return provider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: "EZRestAPI.EZRestAPIModelAttribute",
            predicate: static (syntaxNode, cancellationToken) => true,
            transform: static (context, cancellationToken) =>
            {
                var symbol = context.TargetSymbol;

                var attr = context.Attributes
                    .First(a => a.AttributeClass?.Name == "EZRestAPIModelAttribute");

                return new Model(
                    AssemblyName: symbol.ContainingAssembly.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ModelNamespace: symbol.ContainingNamespace.ToDisplayString(),
                    ClassName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    SingularName: attr.ConstructorArguments[0].Value?.ToString() ?? "xxx",
                    PluralName: attr.ConstructorArguments[1].Value?.ToString() ?? "xxx");
            });
    }
}
