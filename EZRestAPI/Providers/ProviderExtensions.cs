namespace EZRestAPI.Providers;

using System.Linq;
using EZRestAPI.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public static class ProviderExtensions
{
    public record Model(
        string AssemblyName,
        string ModelNamespace,
        string ModelName,
        string ClassName,
        string SingularName,
        string PluralName,
        EquatableArray<Property> Properties
    );

    public record Property(
        bool IsRequired,
        string TypeName,
        string PropertyName,
        bool IsNonNullableReferenceType
    )
    {
        /// <summary>
        /// True when a generated DTO property must carry the `required`
        /// modifier to be valid under nullable reference types.
        /// </summary>
        public bool NeedsRequiredModifier => IsRequired || IsNonNullableReferenceType;
    }

    public static IncrementalValuesProvider<Model> GetModels(this SyntaxValueProvider provider)
    {
        return provider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: "EZRestAPI.ModelAttribute",
            predicate: static (syntaxNode, cancellationToken) =>
                syntaxNode is ClassDeclarationSyntax,
            transform: static (context, cancellationToken) =>
            {
                var symbol = context.TargetSymbol;
                var namedTypeSymbol = symbol as INamedTypeSymbol;

                var attribute = context.Attributes.Single();
                var singularName =
                    attribute.ConstructorArguments[0].Value?.ToString() ?? "SingularNameNotSet";
                var pluralName =
                    attribute.ConstructorArguments[1].Value?.ToString() ?? "PluralNameNotSet";

                var properties = namedTypeSymbol?.GetMembers().OfType<IPropertySymbol>() ?? [];

                return new Model(
                    AssemblyName: symbol.ContainingAssembly.Name,
                    ModelNamespace: symbol.ContainingNamespace.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    ),
                    ClassName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ModelName: symbol.Name,
                    SingularName: singularName,
                    PluralName: pluralName,
                    Properties: new EquatableArray<Property>(
                        properties
                            .Select(p => new Property(
                                IsRequired: p.IsRequired,
                                TypeName: p.Type.ToDisplayString(),
                                PropertyName: p.Name,
                                IsNonNullableReferenceType: p.Type.IsReferenceType
                                    && p.NullableAnnotation == NullableAnnotation.NotAnnotated
                            ))
                            .ToArray()
                    )
                );
            }
        );
    }
}
