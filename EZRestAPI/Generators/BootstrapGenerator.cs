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

        context.RegisterSourceOutput(
            modelsProvider,
            (ctx, models) =>
            {
                if (models.IsDefaultOrEmpty)
                {
                    return;
                }

                var assemblyName = models.First().AssemblyName;

                // Every model gets a `{Singular}Repository`, Endpoints.None
                // included, so all of them are registered.
                var repositoryNames = models.Select(m => m.SingularName).ToArray();

                // Only non-None models get a `Map{Singular}Endpoints`; naming an
                // Endpoints.None model here would dangle.
                var endpointNames = models
                    .Where(m => m.Endpoints != ProviderExtensions.EndpointFeatures.None)
                    .Select(m => m.SingularName)
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
