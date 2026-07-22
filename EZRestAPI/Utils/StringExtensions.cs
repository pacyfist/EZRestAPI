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

    /// <summary>
    /// Converts a PascalCase/camelCase method name into a kebab-case route
    /// segment: "AddLine" becomes "add-line", "Cancel" becomes "cancel". Runs
    /// of upper-case letters are collapsed so "AddSKU" becomes "add-sku".
    /// </summary>
    public static string ToKebabCase(this string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 4);

        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];

            if (char.IsUpper(character))
            {
                var previousIsLower = i > 0 && char.IsLower(value[i - 1]);
                var previousIsUpperNextIsLower =
                    i > 0
                    && char.IsUpper(value[i - 1])
                    && i + 1 < value.Length
                    && char.IsLower(value[i + 1]);

                if (i > 0 && (previousIsLower || previousIsUpperNextIsLower))
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Upper-cases the first character of an identifier so a factory/command
    /// parameter name ("customer") becomes a PascalCase DTO property name
    /// ("Customer"). Parameter names are already valid camelCase identifiers,
    /// so only the leading character needs adjusting.
    /// </summary>
    public static string ToPascalCase(this string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value.Substring(1);
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
