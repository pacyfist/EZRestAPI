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
    public const string ScalarAttributeName = "EZRestAPI.ScalarAttribute";
    public const string AggregateAttributeName = "EZRestAPI.AggregateAttribute";
    public const string FactoryAttributeName = "EZRestAPI.FactoryAttribute";
    public const string CommandAttributeName = "EZRestAPI.CommandAttribute";

    /// <summary>
    /// Collection shapes supported for nested model properties. The mutable
    /// forms back request DTOs and EF navigations directly; the read-only forms
    /// (<c>IReadOnlyList&lt;T&gt;</c>/<c>IReadOnlyCollection&lt;T&gt;</c>) are the
    /// idiomatic DDD projection over a private backing field, mapped as EF
    /// navigations via field access. All must resolve their element to a
    /// generated <c>{Nested}Dto</c> so the domain type never leaks into the API.
    /// </summary>
    private static readonly string[] SupportedCollectionTypes =
    [
        "System.Collections.Generic.List<T>",
        "System.Collections.Generic.IList<T>",
        "System.Collections.Generic.ICollection<T>",
        "System.Collections.Generic.IReadOnlyList<T>",
        "System.Collections.Generic.IReadOnlyCollection<T>",
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
        string? UserIdTypeName = null,
        EquatableArray<RelationshipInfo> ParentRelationships = default,
        EquatableArray<RelationshipInfo> ChildRelationships = default
    );

    public record RelationshipInfo(
        string ChildSingularName,
        string ChildPluralName,
        string ChildClassName,
        string ParentSingularName,
        string ParentPluralName,
        string ParentClassName,
        string ForeignKeyPropertyName,
        bool IsNullable
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

    /// <summary>
    /// How an aggregate is constructed from generated code: either a public
    /// static factory method (<c>Order.Place(customer)</c>) or a public
    /// constructor (<c>new Order(customer)</c>). Both are invoked directly at
    /// compile time — never via reflection.
    /// </summary>
    public enum FactoryKind
    {
        StaticMethod,
        Constructor,
    }

    /// <summary>
    /// A factory-method or command-method parameter. Value-object parameters
    /// carry a resolved <see cref="NestedType"/> so downstream generators can
    /// nest the VO's Dto exactly like a [Nested] property.
    /// </summary>
    public record Parameter(
        string TypeName,
        string ParameterName,
        bool IsNonNullableReferenceType,
        NestedKind Kind = NestedKind.None,
        NestedType? Nested = null,
        EquatableArray<string> DataAnnotations = default
    )
    {
        /// <summary>
        /// Projects a factory/command parameter onto the <see cref="Property"/>
        /// shape so it can flow through the same DTO emission, nested-mapper and
        /// <c>ToEntity</c>/<c>ToDto</c> machinery as a [Model] property. The
        /// parameter name is PascalCased into the DTO property name.
        /// </summary>
        public Property AsProperty() =>
            new(
                IsRequired: false,
                TypeName: TypeName,
                PropertyName: ParameterName.ToPascalCase(),
                IsNonNullableReferenceType: IsNonNullableReferenceType,
                Kind: Kind,
                Nested: Nested,
                DataAnnotations: DataAnnotations
            );
    }

    /// <summary>
    /// The single resolved creation entry point of an aggregate: its kind, the
    /// method name (for a static factory) or containing type name (for a
    /// constructor), and its parameters.
    /// </summary>
    public record FactoryInfo(FactoryKind Kind, string Name, EquatableArray<Parameter> Parameters);

    /// <summary>
    /// A guarded state-transition method marked [Command]. The route name is
    /// the explicit override or the kebab-cased method name; the method name is
    /// kept for the direct compile-time invocation.
    /// </summary>
    public record CommandInfo(
        string RouteName,
        string MethodName,
        EquatableArray<Parameter> Parameters
    );

    /// <summary>
    /// A DDD aggregate root. Reads use a distinct property-collection rule from
    /// [Model]: every public-getter instance property participates regardless
    /// of setter accessibility (get-only, private-set and init all included).
    /// </summary>
    public record Aggregate(
        string AssemblyName,
        string ModelNamespace,
        string ModelName,
        string ClassName,
        string SingularName,
        string PluralName,
        EquatableArray<Property> Properties,
        FactoryInfo? Factory,
        EquatableArray<CommandInfo> Commands
    );

    /// <summary>
    /// Diagnostics-only view of an aggregate: source location and the factory
    /// entry-point count (EZR012 fires when it is not exactly one). Locations
    /// and counts stay out of the cached <see cref="Aggregate"/> record.
    /// </summary>
    public record AggregateDiagnostics(
        Aggregate Aggregate,
        bool IsPartial,
        int FactoryCount,
        LocationInfo? Location
    );

    public record Property(
        bool IsRequired,
        string TypeName,
        string PropertyName,
        bool IsNonNullableReferenceType,
        NestedKind Kind = NestedKind.None,
        NestedType? Nested = null,
        bool IsModelReference = false,
        bool IsUnsupportedNestedShape = false,
        bool IsScalarOptOut = false,
        EquatableArray<string> DataAnnotations = default
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

    public static IncrementalValuesProvider<Model> GetModelsWithRelationships(
        this SyntaxValueProvider provider
    )
    {
        return provider
            .GetModels()
            .Collect()
            .SelectMany(static (models, _) => ResolveRelationships(models));
    }

    private static ImmutableArray<Model> ResolveRelationships(ImmutableArray<Model> models)
    {
        var bySingular = new System.Collections.Generic.Dictionary<string, Model>();
        foreach (var m in models)
        {
            bySingular[m.SingularName] = m;
        }

        // First pass: each model's own foreign keys (edges to its parents).
        var parentEdges = new System.Collections.Generic.Dictionary<
            string,
            System.Collections.Generic.List<RelationshipInfo>
        >();
        var childEdges = new System.Collections.Generic.Dictionary<
            string,
            System.Collections.Generic.List<RelationshipInfo>
        >();

        foreach (var child in models)
        {
            foreach (var p in child.Properties)
            {
                if (p.Kind != NestedKind.None || p.IsScalarOptOut)
                {
                    continue;
                }
                if (p.TypeName != "int" && p.TypeName != "int?")
                {
                    continue;
                }
                if (!p.PropertyName.EndsWith("Id") || p.PropertyName.Length <= 2)
                {
                    continue;
                }
                var parentSingular = p.PropertyName.Substring(0, p.PropertyName.Length - 2);
                if (parentSingular == child.SingularName)
                {
                    continue; // self-reference: out of scope this cycle
                }
                if (!bySingular.TryGetValue(parentSingular, out var parent))
                {
                    continue; // unresolved -> handled by EZR011, not here
                }

                var edge = new RelationshipInfo(
                    ChildSingularName: child.SingularName,
                    ChildPluralName: child.PluralName,
                    ChildClassName: child.ClassName,
                    ParentSingularName: parent.SingularName,
                    ParentPluralName: parent.PluralName,
                    ParentClassName: parent.ClassName,
                    ForeignKeyPropertyName: p.PropertyName,
                    IsNullable: p.TypeName == "int?"
                );

                (
                    parentEdges.TryGetValue(child.SingularName, out var pe)
                        ? pe
                        : parentEdges[child.SingularName] = new()
                ).Add(edge);
                (
                    childEdges.TryGetValue(parent.SingularName, out var ce)
                        ? ce
                        : childEdges[parent.SingularName] = new()
                ).Add(edge);
            }
        }

        return models
            .Select(m =>
                m with
                {
                    ParentRelationships = new EquatableArray<RelationshipInfo>(
                        parentEdges.TryGetValue(m.SingularName, out var pe) ? pe.ToArray() : []
                    ),
                    ChildRelationships = new EquatableArray<RelationshipInfo>(
                        childEdges.TryGetValue(m.SingularName, out var ce) ? ce.ToArray() : []
                    ),
                }
            )
            .ToImmutableArray();
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

    public static IncrementalValuesProvider<Aggregate> GetAggregates(
        this SyntaxValueProvider provider
    )
    {
        return provider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: AggregateAttributeName,
            predicate: static (syntaxNode, cancellationToken) =>
                syntaxNode is ClassDeclarationSyntax,
            transform: static (context, cancellationToken) => CreateAggregate(context).Aggregate
        );
    }

    /// <summary>
    /// Same as <see cref="GetAggregates"/> but wrapped with source locations,
    /// syntax facts and the factory entry-point count, for the diagnostics
    /// generator only.
    /// </summary>
    public static IncrementalValuesProvider<AggregateDiagnostics> GetAggregatesWithDiagnostics(
        this SyntaxValueProvider provider
    )
    {
        return provider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: AggregateAttributeName,
            predicate: static (syntaxNode, cancellationToken) =>
                syntaxNode is ClassDeclarationSyntax,
            transform: static (context, cancellationToken) =>
            {
                var isPartial =
                    context.TargetNode is ClassDeclarationSyntax classDeclaration
                    && classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));

                var (aggregate, factoryCount) = CreateAggregate(context);

                return new AggregateDiagnostics(
                    aggregate,
                    isPartial,
                    factoryCount,
                    LocationInfo.From(context.TargetSymbol.Locations.FirstOrDefault())
                );
            }
        );
    }

    private static (Aggregate Aggregate, int FactoryCount) CreateAggregate(
        GeneratorAttributeSyntaxContext context
    )
    {
        var symbol = context.TargetSymbol;
        var namedTypeSymbol = symbol as INamedTypeSymbol;

        var attribute = context.Attributes.First();
        var singularName = GetArgument(attribute, 0, "SingularNameNotSet");
        var pluralName = GetArgument(attribute, 1, "PluralNameNotSet");

        var className = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var visited = ImmutableHashSet.Create(className);

        var (factory, factoryCount) = ResolveFactory(namedTypeSymbol, visited);

        var aggregate = new Aggregate(
            AssemblyName: symbol.ContainingAssembly.Name.ToValidNamespace(),
            ModelNamespace: symbol.ContainingNamespace.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            ),
            ClassName: className,
            ModelName: symbol.Name,
            SingularName: singularName,
            PluralName: pluralName,
            Properties: CollectAggregateProperties(namedTypeSymbol, visited),
            Factory: factory,
            Commands: CollectCommands(namedTypeSymbol, visited)
        );

        return (aggregate, factoryCount);
    }

    /// <summary>
    /// Aggregate read rule (distinct from [Model]'s): every public-getter
    /// instance property participates regardless of setter accessibility — so
    /// get-only projections, { get; private set; } and { get; init; } are all
    /// collected, where the [Model] rule (which requires a setter) drops them.
    /// </summary>
    private static EquatableArray<Property> CollectAggregateProperties(
        INamedTypeSymbol? typeSymbol,
        ImmutableHashSet<string> visited
    )
    {
        var properties =
            typeSymbol
                ?.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p =>
                    !p.IsStatic
                    && !p.IsIndexer
                    && !p.IsImplicitlyDeclared
                    && p.GetMethod is not null
                    && p.DeclaredAccessibility == Accessibility.Public
                    && p.Name != "Id"
                )
            ?? [];

        return new EquatableArray<Property>(
            properties.Select(p => CreateProperty(p, visited)).ToArray()
        );
    }

    private static (FactoryInfo? Factory, int Count) ResolveFactory(
        INamedTypeSymbol? typeSymbol,
        ImmutableHashSet<string> visited
    )
    {
        if (typeSymbol is null)
        {
            return (null, 0);
        }

        var entryPoints = new System.Collections.Generic.List<FactoryInfo>();

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IMethodSymbol method || !HasFactoryAttribute(method))
            {
                continue;
            }

            if (method.MethodKind == MethodKind.Constructor)
            {
                if (method.DeclaredAccessibility == Accessibility.Public)
                {
                    entryPoints.Add(
                        new FactoryInfo(
                            FactoryKind.Constructor,
                            typeSymbol.Name,
                            CollectParameters(method, visited)
                        )
                    );
                }

                continue;
            }

            // A public static method returning the aggregate type.
            if (
                method.MethodKind == MethodKind.Ordinary
                && method.IsStatic
                && method.DeclaredAccessibility == Accessibility.Public
            )
            {
                entryPoints.Add(
                    new FactoryInfo(
                        FactoryKind.StaticMethod,
                        method.Name,
                        CollectParameters(method, visited)
                    )
                );
            }
        }

        return (entryPoints.Count == 1 ? entryPoints[0] : null, entryPoints.Count);
    }

    private static EquatableArray<CommandInfo> CollectCommands(
        INamedTypeSymbol? typeSymbol,
        ImmutableHashSet<string> visited
    )
    {
        if (typeSymbol is null)
        {
            return new EquatableArray<CommandInfo>([]);
        }

        var commands = new System.Collections.Generic.List<CommandInfo>();

        foreach (var member in typeSymbol.GetMembers())
        {
            if (
                member is not IMethodSymbol method
                || method.MethodKind != MethodKind.Ordinary
                || method.IsStatic
                || method.DeclaredAccessibility != Accessibility.Public
            )
            {
                continue;
            }

            var attribute = method
                .GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CommandAttributeName);

            if (attribute is null)
            {
                continue;
            }

            var overrideName =
                attribute.ConstructorArguments.Length > 0
                    ? attribute.ConstructorArguments[0].Value?.ToString()
                    : null;

            var routeName = string.IsNullOrEmpty(overrideName)
                ? method.Name.ToKebabCase()
                : overrideName!;

            commands.Add(
                new CommandInfo(routeName, method.Name, CollectParameters(method, visited))
            );
        }

        return new EquatableArray<CommandInfo>(commands.ToArray());
    }

    private static EquatableArray<Parameter> CollectParameters(
        IMethodSymbol method,
        ImmutableHashSet<string> visited
    )
    {
        return new EquatableArray<Parameter>(
            method.Parameters.Select(p => CreateParameter(p, visited)).ToArray()
        );
    }

    private static Parameter CreateParameter(
        IParameterSymbol parameter,
        ImmutableHashSet<string> visited
    )
    {
        var isNonNullableReferenceType =
            parameter.Type.IsReferenceType
            && parameter.NullableAnnotation == NullableAnnotation.NotAnnotated;

        var (kind, nested) = ResolveNested(parameter.Type, visited);

        var dataAnnotations = new EquatableArray<string>(
            parameter
                .GetAttributes()
                .Where(a =>
                    a.AttributeClass?.ContainingNamespace?.ToDisplayString()
                    == "System.ComponentModel.DataAnnotations"
                )
                .Select(RenderAttribute)
                .ToArray()
        );

        return new Parameter(
            TypeName: parameter.Type.ToDisplayString(),
            ParameterName: parameter.Name,
            IsNonNullableReferenceType: isNonNullableReferenceType,
            Kind: kind,
            Nested: nested,
            DataAnnotations: dataAnnotations
        );
    }

    /// <summary>
    /// Resolves whether a type is a [Nested] value object (single) or a
    /// supported collection of one, mirroring the [Model] property rule so
    /// factory/command VO parameters nest their Dto the same way.
    /// </summary>
    private static (NestedKind Kind, NestedType? Nested) ResolveNested(
        ITypeSymbol type,
        ImmutableHashSet<string> visited
    )
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return (NestedKind.None, null);
        }

        if (namedType.IsGenericType)
        {
            var nestedElement = namedType
                .TypeArguments.OfType<INamedTypeSymbol>()
                .FirstOrDefault(a => HasAttribute(a, NestedAttributeName));

            if (
                nestedElement is not null
                && namedType.TypeArguments.Length == 1
                && SupportedCollectionTypes.Contains(namedType.ConstructedFrom.ToDisplayString())
            )
            {
                return (NestedKind.Collection, TryCreateNestedType(nestedElement, visited));
            }

            return (NestedKind.None, null);
        }

        if (TryCreateNestedType(namedType, visited) is { } singleNested)
        {
            return (NestedKind.Single, singleNested);
        }

        return (NestedKind.None, null);
    }

    private static bool HasFactoryAttribute(IMethodSymbol method)
    {
        return method
            .GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == FactoryAttributeName);
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

        var dataAnnotations = new EquatableArray<string>(
            property
                .GetAttributes()
                .Where(a =>
                    a.AttributeClass?.ContainingNamespace?.ToDisplayString()
                    == "System.ComponentModel.DataAnnotations"
                )
                .Select(RenderAttribute)
                .ToArray()
        );

        Property Plain(bool isModelReference = false, bool isUnsupportedNestedShape = false) =>
            new(
                IsRequired: property.IsRequired,
                TypeName: property.Type.ToDisplayString(),
                PropertyName: property.Name,
                IsNonNullableReferenceType: isNonNullableReferenceType,
                IsModelReference: isModelReference,
                IsUnsupportedNestedShape: isUnsupportedNestedShape,
                IsScalarOptOut: property
                    .GetAttributes()
                    .Any(a => a.AttributeClass?.ToDisplayString() == ScalarAttributeName),
                DataAnnotations: dataAnnotations
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
                        Nested: TryCreateNestedType(nestedElement, visited),
                        DataAnnotations: dataAnnotations
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
                Nested: singleNested,
                DataAnnotations: dataAnnotations
            );
        }

        return Plain(isModelReference: HasAttribute(namedType, ModelAttributeName));
    }

    /// <summary>
    /// Reconstructs an attribute application (fully-qualified type name +
    /// positional and named argument literals) as a C# string so it can be
    /// re-emitted verbatim on a generated DTO property. Only literal-shaped
    /// arguments are supported; anything else falls back to a compilable cast.
    /// </summary>
    private static string RenderAttribute(AttributeData attribute)
    {
        var name = attribute.AttributeClass!.ToDisplayString();

        var arguments = attribute
            .ConstructorArguments.Select(RenderTypedConstant)
            .Concat(
                attribute.NamedArguments.Select(named =>
                    $"{named.Key} = {RenderTypedConstant(named.Value)}"
                )
            )
            .ToArray();

        return arguments.Length > 0 ? $"{name}({string.Join(", ", arguments)})" : name;
    }

    private static string RenderTypedConstant(TypedConstant constant)
    {
        if (constant.IsNull)
        {
            return "null";
        }

        switch (constant.Kind)
        {
            case TypedConstantKind.Array:
                var elementType =
                    (constant.Type as IArrayTypeSymbol)?.ElementType.ToDisplayString() ?? "object";
                return $"new {elementType}[] {{ {string.Join(", ", constant.Values.Select(RenderTypedConstant))} }}";

            case TypedConstantKind.Type:
                return constant.Value is ITypeSymbol type
                    ? $"typeof({type.ToDisplayString()})"
                    : "null";

            case TypedConstantKind.Enum:
                var enumType = constant.Type!;
                var member = enumType
                    .GetMembers()
                    .OfType<IFieldSymbol>()
                    .FirstOrDefault(f =>
                        f.HasConstantValue && Equals(f.ConstantValue, constant.Value)
                    );
                return member is not null
                    ? $"{enumType.ToDisplayString()}.{member.Name}"
                    : $"({enumType.ToDisplayString()}){constant.Value}";

            default:
                return SymbolDisplay.FormatPrimitive(
                        constant.Value!,
                        quoteStrings: true,
                        useHexadecimalNumbers: false
                    ) ?? "null";
        }
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
