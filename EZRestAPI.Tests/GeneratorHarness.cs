namespace EZRestAPI.Tests;

using EZRestAPI.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class GeneratorHarness
{
    private static readonly MetadataReference[] References = (
        (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!
    )
        .Split(Path.PathSeparator)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    private static CSharpCompilation CreateCompilation(string source) =>
        CSharpCompilation.Create(
            "GeneratorTests",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

    /// <summary>
    /// Runs all EZRestAPI generators against the given source and returns the
    /// run result (generated sources + reported diagnostics).
    /// </summary>
    public static GeneratorDriverRunResult Run(string source)
    {
        IIncrementalGenerator[] generators =
        [
            new AttributesGenerator(),
            new ModelGenerator(),
            new DbContextGenerator(),
            new DtoGenerator(),
            new RepositoryGenerator(),
            new EndpointsGenerator(),
            new BootstrapGenerator(),
            new NestedGenerator(),
            new DiagnosticsGenerator(),
        ];

        var driver = CSharpGeneratorDriver.Create(
            generators.Select(GeneratorExtensions.AsSourceGenerator).ToArray()
        );

        return driver.RunGenerators(CreateCompilation(source)).GetRunResult();
    }

    /// <summary>
    /// Runs the attributes generator plus a probe that dumps the resolved
    /// <c>Aggregate</c> models to source, so tests can assert on the aggregate
    /// provider before any aggregate codegen exists.
    /// </summary>
    public static string RunAggregateProbe(string source)
    {
        IIncrementalGenerator[] generators =
        [
            new AttributesGenerator(),
            new AggregateProbeGenerator(),
        ];

        var driver = CSharpGeneratorDriver.Create(
            generators.Select(GeneratorExtensions.AsSourceGenerator).ToArray()
        );

        var result = driver.RunGenerators(CreateCompilation(source)).GetRunResult();

        return GetSource(result, "AggregateProbe.g.cs");
    }

    public static string GetSource(GeneratorDriverRunResult result, string hintName)
    {
        return result
            .Results.SelectMany(r => r.GeneratedSources)
            .Single(s => s.HintName == hintName)
            .SourceText.ToString();
    }

    public static string[] DiagnosticIds(GeneratorDriverRunResult result)
    {
        return result.Diagnostics.Select(d => d.Id).Distinct().OrderBy(id => id).ToArray();
    }
}
