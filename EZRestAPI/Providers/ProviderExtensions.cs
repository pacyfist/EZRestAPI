namespace EZRestAPI.Providers;

using Microsoft.CodeAnalysis;
using System.Linq;

public static class ProviderExtensions
{
    public record Model(
        string AssemblyName,
        string ModelNamespace,
        string ModelName,
        string ClassName,
        string SingularName,
        string PluralName,
        IEnumerable<Property> Properties);

    public record Property(
        bool IsRequired,
        string TypeName,
        string PropertyName);

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
                    AssemblyName: symbol.ContainingAssembly.Name,
                    ModelNamespace: symbol.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClassName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ModelName: symbol.Name,
                    SingularName: attr.ConstructorArguments[0].Value?.ToString() ?? "xxx",
                    PluralName: attr.ConstructorArguments[1].Value?.ToString() ?? "xxx",
                    Properties: (symbol as INamedTypeSymbol).GetMembers()
                        .Where(m => m is IPropertySymbol)
                        .OfType<IPropertySymbol>()
                        .Select(p => new Property(
                            IsRequired: p.IsRequired,
                            TypeName: p.Type.ToDisplayString(),
                            PropertyName: p.Name))
                        .ToList()
                    );
            });
    }
}
