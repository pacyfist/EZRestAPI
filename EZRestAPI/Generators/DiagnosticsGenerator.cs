namespace EZRestAPI.Generators;

using EZRestAPI.Providers;
using Microsoft.CodeAnalysis;

[Generator(LanguageNames.CSharp)]
public class DiagnosticsGenerator : IIncrementalGenerator
{
    private const string Category = "EZRestAPI";

    public static readonly DiagnosticDescriptor ModelMustBePartial = new(
        "EZR001",
        "Model class must be partial",
        "Class '{0}' is marked [EZRestAPI.Model] and must be declared partial",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor DuplicateSingularName = new(
        "EZR002",
        "Duplicate model singular name",
        "Singular name '{0}' is used by more than one [EZRestAPI.Model] class",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor DuplicatePluralName = new(
        "EZR003",
        "Duplicate model plural name",
        "Plural name '{0}' is used by more than one [EZRestAPI.Model] class",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor ModelUsedAsNavigation = new(
        "EZR004",
        "Model type used as a navigation property",
        "Property '{0}' on '{1}' references an [EZRestAPI.Model] type; reference aggregate roots by id instead, or mark the property type [EZRestAPI.Nested]",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor NestedCycle = new(
        "EZR005",
        "Nested model cycle",
        "Property '{0}' on '{1}' creates a [EZRestAPI.Nested] containment cycle; nesting must be tree-shaped",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor DuplicateNestedName = new(
        "EZR006",
        "Duplicate nested singular name",
        "Singular name '{0}' is used by more than one [EZRestAPI.Nested] class",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor UnsupportedIdType = new(
        "EZR007",
        "Unsupported Id property type",
        "Class '{0}' declares an 'Id' property of type '{1}'; only 'int' keys are supported",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor NameNotValidIdentifier = new(
        "EZR008",
        "Name is not a valid identifier",
        "The name '{0}' on class '{1}' is not a valid C# identifier; it is used to build generated type names",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor UnsupportedNestedShape = new(
        "EZR009",
        "Unsupported container for a nested model",
        "Property '{0}' on '{1}' holds nested models in an unsupported container; use List<T>, IList<T>, ICollection<T>, IReadOnlyList<T> or IReadOnlyCollection<T>",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor ModelAndNested = new(
        "EZR010",
        "Class is both a model and a nested model",
        "Class '{0}' is marked with both [EZRestAPI.Model] and [EZRestAPI.Nested]; a class must be one or the other",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor UnresolvedForeignKey = new(
        "EZR011",
        "Foreign-key-shaped property has no matching model",
        "Property '{0}' on '{1}' is named like a foreign key but no [EZRestAPI.Model] has singular name '{2}'; create that model, or mark the property [EZRestAPI.Scalar] to treat it as a plain value",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor AggregateFactoryArity = new(
        "EZR012",
        "Aggregate must declare exactly one factory entry point",
        "Class '{0}' is marked [EZRestAPI.Aggregate] but declares {1} [EZRestAPI.Factory] entry point(s); mark exactly one public static factory method or public constructor",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModelsWithDiagnostics().Collect();
        var nestedProvider = context.SyntaxProvider.GetNestedModelsWithDiagnostics().Collect();
        var aggregatesProvider = context.SyntaxProvider.GetAggregatesWithDiagnostics().Collect();

        context.RegisterSourceOutput(
            modelsProvider.Combine(nestedProvider).Combine(aggregatesProvider),
            (ctx, pair) =>
            {
                var ((models, nestedModels), aggregates) = pair;

                foreach (var entry in models)
                {
                    var model = entry.Model;
                    var location = entry.Location?.ToLocation() ?? Location.None;

                    if (!entry.IsPartial)
                    {
                        ctx.ReportDiagnostic(
                            Diagnostic.Create(ModelMustBePartial, location, model.ModelName)
                        );
                    }

                    if (model.UserIdTypeName is not null and not "int")
                    {
                        ctx.ReportDiagnostic(
                            Diagnostic.Create(
                                UnsupportedIdType,
                                location,
                                model.ModelName,
                                model.UserIdTypeName
                            )
                        );
                    }

                    ReportInvalidName(ctx, model.SingularName, model.ModelName, location);
                    ReportInvalidName(ctx, model.PluralName, model.ModelName, location);
                    ReportPropertyDiagnostics(ctx, model.ModelName, location, model.Properties);
                }

                var knownSingulars = new HashSet<string>(models.Select(m => m.Model.SingularName));

                foreach (var entry in models)
                {
                    var model = entry.Model;
                    var location = entry.Location?.ToLocation() ?? Location.None;

                    foreach (var property in model.Properties)
                    {
                        if (
                            property.Kind != ProviderExtensions.NestedKind.None
                            || property.IsScalarOptOut
                        )
                        {
                            continue;
                        }

                        if (property.TypeName != "int" && property.TypeName != "int?")
                        {
                            continue;
                        }

                        if (
                            !property.PropertyName.EndsWith("Id")
                            || property.PropertyName.Length <= 2
                        )
                        {
                            continue;
                        }

                        var parentSingular = property.PropertyName.Substring(
                            0,
                            property.PropertyName.Length - 2
                        );
                        if (
                            parentSingular == model.SingularName
                            || knownSingulars.Contains(parentSingular)
                        )
                        {
                            continue;
                        }

                        ctx.ReportDiagnostic(
                            Diagnostic.Create(
                                UnresolvedForeignKey,
                                location,
                                property.PropertyName,
                                model.ModelName,
                                parentSingular
                            )
                        );
                    }
                }

                foreach (var entry in nestedModels)
                {
                    var nested = entry.Nested;
                    var location = entry.Location?.ToLocation() ?? Location.None;

                    ReportInvalidName(ctx, nested.SingularName, nested.ClassName, location);
                    ReportPropertyDiagnostics(ctx, nested.ClassName, location, nested.Properties);
                }

                // A class carrying both [Model] and [Nested] would get a DbSet
                // AND an owned-type configuration, which EF rejects at runtime;
                // [Model]+[Aggregate] mix the anemic and DDD paths on one class.
                var nestedClassNames = new HashSet<string>(
                    nestedModels.Select(n => n.Nested.ClassName)
                );
                var aggregateClassNames = new HashSet<string>(
                    aggregates.Select(a => a.Aggregate.ClassName)
                );
                foreach (
                    var entry in models.Where(m =>
                        nestedClassNames.Contains(m.Model.ClassName)
                        || aggregateClassNames.Contains(m.Model.ClassName)
                    )
                )
                {
                    ctx.ReportDiagnostic(
                        Diagnostic.Create(
                            ModelAndNested,
                            entry.Location?.ToLocation() ?? Location.None,
                            entry.Model.ModelName
                        )
                    );
                }

                // An aggregate needs exactly one creation entry point: zero
                // makes it uncreatable, more than one makes creation ambiguous.
                foreach (var entry in aggregates)
                {
                    if (entry.FactoryCount != 1)
                    {
                        ctx.ReportDiagnostic(
                            Diagnostic.Create(
                                AggregateFactoryArity,
                                entry.Location?.ToLocation() ?? Location.None,
                                entry.Aggregate.ModelName,
                                entry.FactoryCount
                            )
                        );
                    }
                }

                ReportDuplicates(
                    ctx,
                    DuplicateSingularName,
                    models.Select(m => (m.Model.SingularName, m.Location))
                );
                ReportDuplicates(
                    ctx,
                    DuplicatePluralName,
                    models.Select(m => (m.Model.PluralName, m.Location))
                );
                ReportDuplicates(
                    ctx,
                    DuplicateNestedName,
                    nestedModels.Select(n => (n.Nested.SingularName, n.Location))
                );
            }
        );
    }

    private static void ReportPropertyDiagnostics(
        SourceProductionContext ctx,
        string ownerName,
        Location location,
        IEnumerable<ProviderExtensions.Property> properties
    )
    {
        foreach (var property in properties)
        {
            if (property.IsModelReference)
            {
                ctx.ReportDiagnostic(
                    Diagnostic.Create(
                        ModelUsedAsNavigation,
                        location,
                        property.PropertyName,
                        ownerName
                    )
                );
            }

            if (property.IsNestedCycle)
            {
                ctx.ReportDiagnostic(
                    Diagnostic.Create(NestedCycle, location, property.PropertyName, ownerName)
                );
            }

            if (property.IsUnsupportedNestedShape)
            {
                ctx.ReportDiagnostic(
                    Diagnostic.Create(
                        UnsupportedNestedShape,
                        location,
                        property.PropertyName,
                        ownerName
                    )
                );
            }

            if (property.Nested is { } nested)
            {
                ReportPropertyDiagnostics(ctx, nested.ClassName, location, nested.Properties);
            }
        }
    }

    private static void ReportInvalidName(
        SourceProductionContext ctx,
        string name,
        string ownerName,
        Location location
    )
    {
        if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(name))
        {
            ctx.ReportDiagnostic(
                Diagnostic.Create(NameNotValidIdentifier, location, name, ownerName)
            );
        }
    }

    private static void ReportDuplicates(
        SourceProductionContext ctx,
        DiagnosticDescriptor descriptor,
        IEnumerable<(string Name, LocationInfo? Location)> entries
    )
    {
        // Case-insensitive: routes are lowercased plural names, so names
        // differing only by case still collide at the HTTP level.
        foreach (
            var duplicates in entries
                .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
        )
        {
            foreach (var entry in duplicates)
            {
                ctx.ReportDiagnostic(
                    Diagnostic.Create(
                        descriptor,
                        entry.Location?.ToLocation() ?? Location.None,
                        duplicates.Key
                    )
                );
            }
        }
    }
}
