namespace EZRestAPI.Generators;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;

[Generator(LanguageNames.CSharp)]
public class AttributesGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
        {
            ctx.AddSource(
                "EZRestAPIModel.g.cs",
                SourceText.From("""
                    namespace EZRestAPI;

                    [AttributeUsage(AttributeTargets.Class)]
                    public partial class EZRestAPIModelAttribute(string SingularName, string PluralName)
                        : Attribute
                    {
                    }
                    """, Encoding.UTF8));
        });
    }
}
