namespace EZRestAPI.Providers;

using System.Collections.Immutable;
using System.Linq;
using EZRestAPI.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public static class ProviderExtensions
{
    public enum NestedKind
    {
        None,
        Single,
        Collection,
    }

    public record Model(
        string AssemblyName,
        string ModelNamespace,
        string ModelName,
        string ClassName,
        string SingularName,
        string PluralName,
        EquatableArray<Property> Properties
    );

    public record NestedModel(
        string AssemblyName,
        string ClassName,
        string SingularName,
        EquatableArray<Property> Properties
    );

    public record NestedType(
        string ClassName,
        string SingularName,
        EquatableArray<Property> Properties
    );

    public record Property(
        bool IsRequired,
        string TypeName,
        string PropertyName,
        bool IsNonNullableReferenceType,
        NestedKind Kind = NestedKind.None,
        NestedType? Nested = null
    )
    {
        /// <summary>
        /// True when a generated DTO property must carry the `required`
        /// modifier to be valid under nullable reference types.
        /// </summary>
        public bool NeedsRequiredModifier => IsRequired || IsNonNullableReferenceType;

        /// <summary>
        /// The type this property has in generated DTOs, repositories and
        /// endpoints: nested models are exposed as their generated Dto types.
        /// </summary>
        public string DtoTypeName =>
            Kind switch
            {
                NestedKind.Single =>
                    $"{Nested!.SingularName}Dto{(IsNonNullableReferenceType ? "" : "?")}",
                NestedKind.Collection =>
                    $"System.Collections.Generic.List<{Nested!.SingularName}Dto>",
                _ => TypeName,
            };

        /// <summary>
        /// Expression converting <paramref name="value"/> (a DTO-typed value)
        /// into the entity type, recursing through nested mappers.
        /// </summary>
        public string ToEntityExpression(string value) =>
            Kind switch
            {
                NestedKind.Single when IsNonNullableReferenceType =>
                    $"{Nested!.SingularName}Mapper.ToEntity({value})",
                NestedKind.Single =>
                    $"{value} is null ? null : {Nested!.SingularName}Mapper.ToEntity({value})",
                NestedKind.Collection =>
                    $"{value}.Select({Nested!.SingularName}Mapper.ToEntity).ToList()",
                _ => value,
            };

        /// <summary>
        /// Expression converting <paramref name="value"/> (an entity-typed
        /// value) into the DTO type, recursing through nested mappers.
        /// </summary>
        public string ToDtoExpression(string value) =>
            Kind switch
            {
                NestedKind.Single when IsNonNullableReferenceType =>
                    $"{Nested!.SingularName}Mapper.ToDto({value})",
                NestedKind.Single =>
                    $"{value} is null ? null : {Nested!.SingularName}Mapper.ToDto({value})",
                NestedKind.Collection =>
                    $"{value}.Select({Nested!.SingularName}Mapper.ToDto).ToList()",
                _ => value,
            };
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

                var className = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                return new Model(
                    AssemblyName: symbol.ContainingAssembly.Name,
                    ModelNamespace: symbol.ContainingNamespace.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    ),
                    ClassName: className,
                    ModelName: symbol.Name,
                    SingularName: singularName,
                    PluralName: pluralName,
                    Properties: CollectProperties(
                        namedTypeSymbol,
                        ImmutableHashSet.Create(className)
                    )
                );
            }
        );
    }

    public static IncrementalValuesProvider<NestedModel> GetNestedModels(
        this SyntaxValueProvider provider
    )
    {
        return provider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: "EZRestAPI.NestedAttribute",
            predicate: static (syntaxNode, cancellationToken) =>
                syntaxNode is ClassDeclarationSyntax,
            transform: static (context, cancellationToken) =>
            {
                var symbol = (INamedTypeSymbol)context.TargetSymbol;

                var attribute = context.Attributes.Single();
                var singularName =
                    attribute.ConstructorArguments[0].Value?.ToString() ?? "SingularNameNotSet";

                var className = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                return new NestedModel(
                    AssemblyName: symbol.ContainingAssembly.Name,
                    ClassName: className,
                    SingularName: singularName,
                    Properties: CollectProperties(symbol, ImmutableHashSet.Create(className))
                );
            }
        );
    }

    private static EquatableArray<Property> CollectProperties(
        INamedTypeSymbol? typeSymbol,
        ImmutableHashSet<string> visited
    )
    {
        var properties = typeSymbol?.GetMembers().OfType<IPropertySymbol>() ?? [];

        return new EquatableArray<Property>(
            properties.Select(p => CreateProperty(p, visited)).ToArray()
        );
    }

    private static Property CreateProperty(
        IPropertySymbol property,
        ImmutableHashSet<string> visited
    )
    {
        var isNonNullableReferenceType =
            property.Type.IsReferenceType
            && property.NullableAnnotation == NullableAnnotation.NotAnnotated;

        if (
            property.Type
                is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } collectionType
            && collectionType.TypeArguments[0] is INamedTypeSymbol elementType
            && TryCreateNestedType(elementType, visited) is { } collectionNested
        )
        {
            return new Property(
                IsRequired: property.IsRequired,
                TypeName: property.Type.ToDisplayString(),
                PropertyName: property.Name,
                IsNonNullableReferenceType: isNonNullableReferenceType,
                Kind: NestedKind.Collection,
                Nested: collectionNested
            );
        }

        if (
            property.Type is INamedTypeSymbol namedType
            && TryCreateNestedType(namedType, visited) is { } singleNested
        )
        {
            return new Property(
                IsRequired: property.IsRequired,
                TypeName: property.Type.ToDisplayString(),
                PropertyName: property.Name,
                IsNonNullableReferenceType: isNonNullableReferenceType,
                Kind: NestedKind.Single,
                Nested: singleNested
            );
        }

        return new Property(
            IsRequired: property.IsRequired,
            TypeName: property.Type.ToDisplayString(),
            PropertyName: property.Name,
            IsNonNullableReferenceType: isNonNullableReferenceType
        );
    }

    private static NestedType? TryCreateNestedType(
        INamedTypeSymbol symbol,
        ImmutableHashSet<string> visited
    )
    {
        var attribute = symbol
            .GetAttributes()
            .FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == "EZRestAPI.NestedAttribute"
            );

        if (attribute is null)
        {
            return null;
        }

        var className = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var singularName =
            attribute.ConstructorArguments[0].Value?.ToString() ?? "SingularNameNotSet";

        // Cycle guard: a type nested (directly or indirectly) inside itself is
        // not expanded further.
        var properties = visited.Contains(className)
            ? new EquatableArray<Property>([])
            : CollectProperties(symbol, visited.Add(className));

        return new NestedType(className, singularName, properties);
    }
}
