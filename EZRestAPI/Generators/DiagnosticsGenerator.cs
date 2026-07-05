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
        "Property '{0}' on '{1}' holds nested models in an unsupported container; use List<T>, IList<T> or ICollection<T>",
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

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModelsWithDiagnostics().Collect();
        var nestedProvider = context.SyntaxProvider.GetNestedModelsWithDiagnostics().Collect();

        context.RegisterSourceOutput(
            modelsProvider.Combine(nestedProvider),
            (ctx, pair) =>
            {
                var (models, nestedModels) = pair;

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

                foreach (var entry in nestedModels)
                {
                    var nested = entry.Nested;
                    var location = entry.Location?.ToLocation() ?? Location.None;

                    ReportInvalidName(ctx, nested.SingularName, nested.ClassName, location);
                    ReportPropertyDiagnostics(ctx, nested.ClassName, location, nested.Properties);
                }

                // A class carrying both attributes would get a DbSet AND an
                // owned-type configuration, which EF rejects at runtime.
                var nestedClassNames = new HashSet<string>(
                    nestedModels.Select(n => n.Nested.ClassName)
                );
                foreach (
                    var entry in models.Where(m => nestedClassNames.Contains(m.Model.ClassName))
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
