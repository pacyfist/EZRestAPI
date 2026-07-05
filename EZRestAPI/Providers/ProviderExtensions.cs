namespace EZRestAPI.Providers;

using System.Collections.Immutable;
using System.Linq;
using EZRestAPI.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public static class ProviderExtensions
{
    public const string ModelAttributeName = "EZRestAPI.ModelAttribute";
    public const string NestedAttributeName = "EZRestAPI.NestedAttribute";

    /// <summary>
    /// Collection shapes supported for nested model properties: they must be
    /// assignable from List&lt;T&gt; and usable as EF Core collection navigations.
    /// </summary>
    private static readonly string[] SupportedCollectionTypes =
    [
        "System.Collections.Generic.List<T>",
        "System.Collections.Generic.IList<T>",
        "System.Collections.Generic.ICollection<T>",
    ];

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
        EquatableArray<Property> Properties,
        string? UserIdTypeName = null
    );

    /// <summary>
    /// Diagnostics-only view of a model. Locations change whenever text above
    /// the class shifts, so they must never flow into the cached models the
    /// code-generating pipelines consume.
    /// </summary>
    public record ModelDiagnostics(Model Model, bool IsPartial, LocationInfo? Location);

    public record NestedModel(
        string AssemblyName,
        string ClassName,
        string SingularName,
        EquatableArray<Property> Properties
    );

    /// <summary>
    /// Diagnostics-only view of a nested model; see <see cref="ModelDiagnostics"/>.
    /// </summary>
    public record NestedModelDiagnostics(NestedModel Nested, LocationInfo? Location);

    public record NestedType(
        string ClassName,
        string SingularName,
        EquatableArray<Property> Properties,
        bool IsCycle = false
    );

    public record Property(
        bool IsRequired,
        string TypeName,
        string PropertyName,
        bool IsNonNullableReferenceType,
        NestedKind Kind = NestedKind.None,
        NestedType? Nested = null,
        bool IsModelReference = false,
        bool IsUnsupportedNestedShape = false
    )
    {
        /// <summary>
        /// True when this property (or its collection element) closes a
        /// [Nested] containment cycle.
        /// </summary>
        public bool IsNestedCycle => Nested?.IsCycle == true;

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
                    $"System.Collections.Generic.List<{Nested!.SingularName}Dto>{(IsNonNullableReferenceType ? "" : "?")}",
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
                NestedKind.Collection when IsNonNullableReferenceType =>
                    $"{value}.Select({Nested!.SingularName}Mapper.ToEntity).ToList()",
                NestedKind.Collection =>
                    $"{value} is null ? null : {value}.Select({Nested!.SingularName}Mapper.ToEntity).ToList()",
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
                NestedKind.Collection when IsNonNullableReferenceType =>
                    $"{value}.Select({Nested!.SingularName}Mapper.ToDto).ToList()",
                NestedKind.Collection =>
                    $"{value} is null ? null : {value}.Select({Nested!.SingularName}Mapper.ToDto).ToList()",
                _ => value,
            };
    }

    public static IncrementalValuesProvider<Model> GetModels(this SyntaxValueProvider provider)
    {
        return provider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: ModelAttributeName,
            predicate: static (syntaxNode, cancellationToken) =>
                syntaxNode is ClassDeclarationSyntax,
            transform: static (context, cancellationToken) => CreateModel(context)
        );
    }

    /// <summary>
    /// Same as <see cref="GetModels"/> but wrapped with source locations and
    /// syntax facts, for the diagnostics generator only.
    /// </summary>
    public static IncrementalValuesProvider<ModelDiagnostics> GetModelsWithDiagnostics(
        this SyntaxValueProvider provider
    )
    {
        return provider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: ModelAttributeName,
            predicate: static (syntaxNode, cancellationToken) =>
                syntaxNode is ClassDeclarationSyntax,
            transform: static (context, cancellationToken) =>
            {
                var isPartial =
                    context.TargetNode is ClassDeclarationSyntax classDeclaration
                    && classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));

                return new ModelDiagnostics(
                    CreateModel(context),
                    isPartial,
                    LocationInfo.From(context.TargetSymbol.Locations.FirstOrDefault())
                );
            }
        );
    }

    private static Model CreateModel(GeneratorAttributeSyntaxContext context)
    {
        var symbol = context.TargetSymbol;
        var namedTypeSymbol = symbol as INamedTypeSymbol;

        var attribute = context.Attributes.First();
        var singularName = GetArgument(attribute, 0, "SingularNameNotSet");
        var pluralName = GetArgument(attribute, 1, "PluralNameNotSet");

        var className = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // A user-declared Id takes over from the generated key.
        var userIdProperty = namedTypeSymbol
            ?.GetMembers("Id")
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => !p.IsStatic && !p.IsIndexer);

        return new Model(
            AssemblyName: symbol.ContainingAssembly.Name.ToValidNamespace(),
            ModelNamespace: symbol.ContainingNamespace.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            ),
            ClassName: className,
            ModelName: symbol.Name,
            SingularName: singularName,
            PluralName: pluralName,
            Properties: CollectProperties(
                namedTypeSymbol,
                ImmutableHashSet.Create(className),
                excludeId: true
            ),
            UserIdTypeName: userIdProperty?.Type.ToDisplayString()
        );
    }

    public static IncrementalValuesProvider<NestedModel> GetNestedModels(
        this SyntaxValueProvider provider
    )
    {
        return provider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: NestedAttributeName,
            predicate: static (syntaxNode, cancellationToken) =>
                syntaxNode is ClassDeclarationSyntax,
            transform: static (context, cancellationToken) => CreateNestedModel(context)
        );
    }

    /// <summary>
    /// Same as <see cref="GetNestedModels"/> but wrapped with source
    /// locations, for the diagnostics generator only.
    /// </summary>
    public static IncrementalValuesProvider<NestedModelDiagnostics> GetNestedModelsWithDiagnostics(
        this SyntaxValueProvider provider
    )
    {
        return provider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: NestedAttributeName,
            predicate: static (syntaxNode, cancellationToken) =>
                syntaxNode is ClassDeclarationSyntax,
            transform: static (context, cancellationToken) =>
                new NestedModelDiagnostics(
                    CreateNestedModel(context),
                    LocationInfo.From(context.TargetSymbol.Locations.FirstOrDefault())
                )
        );
    }

    private static NestedModel CreateNestedModel(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;

        var attribute = context.Attributes.First();
        var singularName = GetArgument(attribute, 0, "SingularNameNotSet");

        var className = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return new NestedModel(
            AssemblyName: symbol.ContainingAssembly.Name.ToValidNamespace(),
            ClassName: className,
            SingularName: singularName,
            Properties: CollectProperties(symbol, ImmutableHashSet.Create(className))
        );
    }

    /// <summary>
    /// Reads an attribute constructor argument defensively: while the user is
    /// still typing the attribute, arguments may be missing entirely, and a
    /// transform that throws takes down every generator with CS8785.
    /// </summary>
    private static string GetArgument(AttributeData attribute, int index, string fallback)
    {
        return attribute.ConstructorArguments.Length > index
            ? attribute.ConstructorArguments[index].Value?.ToString() ?? fallback
            : fallback;
    }

    private static EquatableArray<Property> CollectProperties(
        INamedTypeSymbol? typeSymbol,
        ImmutableHashSet<string> visited,
        bool excludeId = false
    )
    {
        // Only public, instance, readable AND writable, non-indexer properties
        // participate in generation; anything else (computed properties,
        // statics, indexers) cannot be round-tripped through DTOs.
        var properties =
            typeSymbol
                ?.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p =>
                    !p.IsStatic
                    && !p.IsIndexer
                    && !p.IsImplicitlyDeclared
                    && p.GetMethod is not null
                    && p.SetMethod is not null
                    && p.DeclaredAccessibility == Accessibility.Public
                    && !(excludeId && p.Name == "Id")
                )
            ?? [];

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

        Property Plain(bool isModelReference = false, bool isUnsupportedNestedShape = false) =>
            new(
                IsRequired: property.IsRequired,
                TypeName: property.Type.ToDisplayString(),
                PropertyName: property.Name,
                IsNonNullableReferenceType: isNonNullableReferenceType,
                IsModelReference: isModelReference,
                IsUnsupportedNestedShape: isUnsupportedNestedShape
            );

        // Arrays of annotated types are not supported containers.
        if (property.Type is IArrayTypeSymbol array)
        {
            if (array.ElementType is INamedTypeSymbol arrayElement)
            {
                if (HasAttribute(arrayElement, ModelAttributeName))
                {
                    return Plain(isModelReference: true);
                }

                if (HasAttribute(arrayElement, NestedAttributeName))
                {
                    return Plain(isUnsupportedNestedShape: true);
                }
            }

            return Plain();
        }

        if (property.Type is not INamedTypeSymbol namedType)
        {
            return Plain();
        }

        if (namedType.IsGenericType)
        {
            var annotatedArguments = namedType.TypeArguments.OfType<INamedTypeSymbol>().ToArray();

            var nestedElement = annotatedArguments.FirstOrDefault(a =>
                HasAttribute(a, NestedAttributeName)
            );

            if (nestedElement is not null)
            {
                if (
                    namedType.TypeArguments.Length == 1
                    && SupportedCollectionTypes.Contains(
                        namedType.ConstructedFrom.ToDisplayString()
                    )
                )
                {
                    return new Property(
                        IsRequired: property.IsRequired,
                        TypeName: property.Type.ToDisplayString(),
                        PropertyName: property.Name,
                        IsNonNullableReferenceType: isNonNullableReferenceType,
                        Kind: NestedKind.Collection,
                        Nested: TryCreateNestedType(nestedElement, visited)
                    );
                }

                return Plain(isUnsupportedNestedShape: true);
            }

            if (annotatedArguments.Any(a => HasAttribute(a, ModelAttributeName)))
            {
                return Plain(isModelReference: true);
            }

            return Plain();
        }

        if (TryCreateNestedType(namedType, visited) is { } singleNested)
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

        return Plain(isModelReference: HasAttribute(namedType, ModelAttributeName));
    }

    private static bool HasAttribute(INamedTypeSymbol symbol, string attributeName)
    {
        return symbol
            .GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == attributeName);
    }

    private static NestedType? TryCreateNestedType(
        INamedTypeSymbol symbol,
        ImmutableHashSet<string> visited
    )
    {
        var attribute = symbol
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == NestedAttributeName);

        if (attribute is null)
        {
            return null;
        }

        var className = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var singularName = GetArgument(attribute, 0, "SingularNameNotSet");

        // Cycle guard: a type nested (directly or indirectly) inside itself is
        // not expanded further and is flagged for diagnostics.
        if (visited.Contains(className))
        {
            return new NestedType(
                className,
                singularName,
                new EquatableArray<Property>([]),
                IsCycle: true
            );
        }

        return new NestedType(
            className,
            singularName,
            CollectProperties(symbol, visited.Add(className))
        );
    }
}
