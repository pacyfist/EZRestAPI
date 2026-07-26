namespace EZRestAPI.Generators;

using System.Text;
using EZRestAPI.Providers;
using EZRestAPI.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

[Generator(LanguageNames.CSharp)]
public class BootstrapGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModels().Collect();
        var aggregatesProvider = context.SyntaxProvider.GetAggregates().Collect();
        var combined = modelsProvider.Combine(aggregatesProvider);

        context.RegisterSourceOutput(
            combined,
            (ctx, pair) =>
            {
                var (models, aggregates) = pair;

                if (models.IsDefaultOrEmpty && aggregates.IsDefaultOrEmpty)
                {
                    return;
                }

                var assemblyName = models.IsDefaultOrEmpty
                    ? aggregates.First().AssemblyName
                    : models.First().AssemblyName;

                // Every model and aggregate gets a `{Singular}Repository`, so all
                // of them are registered.
                var repositoryNames = models
                    .Select(m => m.SingularName)
                    .Concat(aggregates.Select(a => a.SingularName))
                    .ToArray();

                // Only non-None models get a `Map{Singular}Endpoints`; naming an
                // Endpoints.None model here would dangle.
                var endpointNames = models
                    .Where(m => m.Endpoints != ProviderExtensions.EndpointFeatures.None)
                    .Select(m => m.SingularName)
                    .Concat(aggregates.Select(a => a.SingularName))
                    .ToArray();

                var writer = SourceWriter.Create();

                writer.WriteLine($"namespace {assemblyName};");
                writer.WriteLine();
                writer.WriteLine("using Microsoft.AspNetCore.Routing;");
                writer.WriteLine("using Microsoft.Extensions.DependencyInjection;");
                writer.WriteLine();
                writer.WriteLine("public static class EZRestAPIExtensions");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine(
                    "public static IServiceCollection AddEZRestAPI(this IServiceCollection services)"
                );
                writer.WriteLine("{");
                writer.Indent++;
                foreach (var name in repositoryNames)
                {
                    writer.WriteLine($"services.AddScoped<{name}Repository>();");
                }
                writer.WriteLine();
                writer.WriteLine("services.AddProblemDetails();");
                writer.WriteLine();
                writer.WriteLine("return services;");
                writer.Indent--;
                writer.WriteLine("}");
                writer.WriteLine();
                writer.WriteLine(
                    "public static IEndpointRouteBuilder MapEZRestAPI(this IEndpointRouteBuilder app)"
                );
                writer.WriteLine("{");
                writer.Indent++;
                foreach (var name in endpointNames)
                {
                    writer.WriteLine($"app.Map{name}Endpoints();");
                }
                writer.WriteLine();
                writer.WriteLine("return app;");
                writer.Indent--;
                writer.WriteLine("}");
                writer.Indent--;
                writer.WriteLine("}");

                ctx.AddSource(
                    "EZRestAPIExtensions.g.cs",
                    SourceText.From(writer.InnerWriter.ToString(), Encoding.UTF8)
                );
            }
        );
    }
}
