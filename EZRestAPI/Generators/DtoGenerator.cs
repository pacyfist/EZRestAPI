﻿namespace EZRestAPI.Generators;

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using EZRestAPI.Providers;
using System.CodeDom.Compiler;

[Generator(LanguageNames.CSharp)]
public class DtoGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelsProvider = context.SyntaxProvider.GetModels();

        RegisterCreateRequest(context, modelsProvider);
        RegisterCreateResponse(context, modelsProvider);
        RegisterReadResponse(context, modelsProvider);
        RegisterUpdateRequest(context, modelsProvider);
    }

    private static void RegisterCreateRequest(IncrementalGeneratorInitializationContext context, IncrementalValuesProvider<ProviderExtensions.Model> modelsProvider)
    {
        context.RegisterSourceOutput(modelsProvider, (ctx, model) =>
        {
            var writer = new IndentedTextWriter(new StringWriter());

            writer.WriteLine($"namespace {model.AssemblyName};");
            writer.WriteLine();
            writer.WriteLine($"public class Create{model.SingularName}Request");
            writer.WriteLine("{");
            writer.Indent++;
            foreach (var property in model.Properties)
            {
                writer.WriteLine($"public {property.TypeName} {property.PropertyName} {{ get; set; }}");
            }
            writer.Indent--;
            writer.WriteLine("}");

            ctx.AddSource(
                $"Create{model.SingularName}Request.g.cs",
                SourceText.From(
                    writer.InnerWriter.ToString(),
                    Encoding.UTF8));
        });
    }

    private static void RegisterCreateResponse(IncrementalGeneratorInitializationContext context, IncrementalValuesProvider<ProviderExtensions.Model> modelsProvider)
    {
        context.RegisterSourceOutput(modelsProvider, (ctx, model) =>
        {
            var writer = new IndentedTextWriter(new StringWriter());

            writer.WriteLine($"namespace {model.AssemblyName};");
            writer.WriteLine();
            writer.WriteLine($"public class Create{model.SingularName}Response");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine("public int Id { get; set; }");
            foreach (var property in model.Properties)
            {
                writer.WriteLine($"public {property.TypeName} {property.PropertyName} {{ get; set; }}");
            }
            writer.Indent--;
            writer.WriteLine("}");

            ctx.AddSource(
                $"Create{model.SingularName}Response.g.cs",
                SourceText.From(
                    writer.InnerWriter.ToString(),
                    Encoding.UTF8));
        });
    }

    private static void RegisterReadResponse(IncrementalGeneratorInitializationContext context, IncrementalValuesProvider<ProviderExtensions.Model> modelsProvider)
    {
        context.RegisterSourceOutput(modelsProvider, (ctx, model) =>
        {
            var writer = new IndentedTextWriter(new StringWriter());

            writer.WriteLine($"namespace {model.AssemblyName};");
            writer.WriteLine();
            writer.WriteLine($"public class Read{model.SingularName}Response");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine("public int? Id { get; set; }");
            foreach (var property in model.Properties)
            {
                writer.WriteLine($"public {property.TypeName} {property.PropertyName} {{ get; set; }}");
            }
            writer.Indent--;
            writer.WriteLine("}");

            ctx.AddSource(
                $"Read{model.SingularName}Response.g.cs",
                SourceText.From(
                    writer.InnerWriter.ToString(),
                    Encoding.UTF8));
        });
    }

    private static void RegisterUpdateRequest(IncrementalGeneratorInitializationContext context, IncrementalValuesProvider<ProviderExtensions.Model> modelsProvider)
    {
        context.RegisterSourceOutput(modelsProvider, (ctx, model) =>
        {
            var writer = new IndentedTextWriter(new StringWriter());

            writer.WriteLine($"namespace {model.AssemblyName};");
            writer.WriteLine();
            writer.WriteLine($"public class Update{model.SingularName}Request");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine("public int Id { get; set; }");
            foreach (var property in model.Properties)
            {
                writer.WriteLine($"public {property.TypeName} {property.PropertyName} {{ get; set; }}");
            }
            writer.Indent--;
            writer.WriteLine("}");

            ctx.AddSource(
                $"Update{model.SingularName}Request.g.cs",
                SourceText.From(
                    writer.InnerWriter.ToString(),
                    Encoding.UTF8));
        });
    }
}
