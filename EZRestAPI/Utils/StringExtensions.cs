namespace EZRestAPI.Utils;

using System.Text;

public static class StringExtensions
{
    private static readonly HashSet<string> ReservedKeywords =
    [
        "abstract",
        "as",
        "base",
        "bool",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "checked",
        "class",
        "const",
        "continue",
        "decimal",
        "default",
        "delegate",
        "do",
        "double",
        "else",
        "enum",
        "event",
        "explicit",
        "extern",
        "false",
        "finally",
        "fixed",
        "float",
        "for",
        "foreach",
        "goto",
        "if",
        "implicit",
        "in",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "readonly",
        "ref",
        "return",
        "sbyte",
        "sealed",
        "short",
        "sizeof",
        "stackalloc",
        "static",
        "string",
        "struct",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "uint",
        "ulong",
        "unchecked",
        "unsafe",
        "ushort",
        "using",
        "virtual",
        "void",
        "volatile",
        "while",
    ];

    /// <summary>
    /// Converts an arbitrary assembly name into a valid C# namespace:
    /// characters illegal in identifiers become '_' (so "my-api" becomes
    /// "my_api", matching the SDK's RootNamespace convention), segments that
    /// start with a digit are prefixed with '_', and segments that collide
    /// with a reserved keyword are '@'-escaped.
    /// </summary>
    public static string ToValidNamespace(this string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "_";
        }

        var segments = value.Split('.').Select(SanitizeSegment);

        return string.Join(".", segments);
    }

    private static string SanitizeSegment(string segment)
    {
        if (segment.Length == 0)
        {
            return "_";
        }

        var builder = new StringBuilder(segment.Length + 1);

        foreach (var character in segment)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        if (char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        var result = builder.ToString();

        return ReservedKeywords.Contains(result) ? "@" + result : result;
    }

    public static string ToCleanNamespace(this string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.StartsWith("global::"))
        {
            return value.Substring(8);
        }

        return value;
    }
}
