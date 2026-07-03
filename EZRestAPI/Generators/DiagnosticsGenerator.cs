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

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModels().Collect();
        var nestedProvider = context.SyntaxProvider.GetNestedModels().Collect();

        context.RegisterSourceOutput(
            modelsProvider.Combine(nestedProvider),
            (ctx, pair) =>
            {
                var (models, nestedModels) = pair;

                foreach (var model in models)
                {
                    var location = model.Location?.ToLocation() ?? Location.None;

                    if (!model.IsPartial)
                    {
                        ctx.ReportDiagnostic(
                            Diagnostic.Create(ModelMustBePartial, location, model.ModelName)
                        );
                    }

                    ReportPropertyDiagnostics(ctx, model.ModelName, location, model.Properties);
                }

                foreach (var nested in nestedModels)
                {
                    ReportPropertyDiagnostics(
                        ctx,
                        nested.ClassName,
                        nested.Location?.ToLocation() ?? Location.None,
                        nested.Properties
                    );
                }

                ReportDuplicates(
                    ctx,
                    DuplicateSingularName,
                    models.Select(m => (m.SingularName, m.Location))
                );
                ReportDuplicates(
                    ctx,
                    DuplicatePluralName,
                    models.Select(m => (m.PluralName, m.Location))
                );
                ReportDuplicates(
                    ctx,
                    DuplicateNestedName,
                    nestedModels.Select(n => (n.SingularName, n.Location))
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

            if (property.Nested is { } nested)
            {
                ReportPropertyDiagnostics(ctx, nested.ClassName, location, nested.Properties);
            }
        }
    }

    private static void ReportDuplicates(
        SourceProductionContext ctx,
        DiagnosticDescriptor descriptor,
        IEnumerable<(string Name, LocationInfo? Location)> entries
    )
    {
        foreach (var duplicates in entries.GroupBy(e => e.Name).Where(group => group.Count() > 1))
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
